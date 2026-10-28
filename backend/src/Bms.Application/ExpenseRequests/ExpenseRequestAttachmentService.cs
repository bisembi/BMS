using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;

namespace Bms.Application.ExpenseRequests;

/// <summary>
/// Owns the attachment lifecycle: actor revalidation, scanning, immutable
/// storage, optimistic concurrency, idempotency, audit and safe projections.
/// </summary>
public sealed class ExpenseRequestAttachmentService(
    IExpenseRequestRepository requestRepository,
    IExpenseRequestAttachmentStore attachmentStore,
    IExpenseRequestAttachmentContentScanner contentScanner,
    IApprovalRouteSnapshotStore approvalRouteSnapshotStore,
    EmployeeActorResolver employeeActorResolver,
    EmployeeFunctionalRoleResolver employeeFunctionalRoleResolver,
    BudgetOwnerCreationAuthority budgetOwnerCreationAuthority,
    IAccessScope accessScope,
    IBusinessAuditSink auditSink,
    IOutboxSink outboxSink,
    IIdempotencyStore idempotencyStore,
    IBusinessTransactionRunner transactionRunner,
    IUnitOfWork unitOfWork,
    IClock clock,
    ICurrentActor currentActor,
    ExpenseRequestSubstitutionAuthority? substitutionAuthority = null)
{
    private const int MaximumCommentLength = 1_000;
    private const int MaximumIdempotencyKeyLength = 127;
    private const int StatusOk = 200;
    private const int StatusCreated = 201;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ExpenseRequestAttachmentListDto?> ListAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        return await ExecuteReadAsync(async attemptCancellationToken =>
        {
            var request = await requestRepository.FindForAttachmentAuthorizationAsync(
                expenseRequestId,
                attemptCancellationToken);
            if (request is null)
            {
                return null;
            }

            var authority = await ResolveAuthorityAsync(
                request,
                actorExternalId,
                attemptCancellationToken);
            if (!authority.CanRead)
            {
                return null;
            }

            var attachments = await attachmentStore.ListAsync(
                request.Id,
                attemptCancellationToken);
            EnsureStoredMetadataIsConsistent(request.Id, attachments);
            return new ExpenseRequestAttachmentListDto(
                request.ConcurrencyStamp.ToString("D"),
                authority.CanAdd,
                authority.CanAdd && !request.Status.IsEditableDraft(),
                attachments.Select(attachment => ToDto(
                    attachment,
                    authority.CanDelete && !attachment.IsSealed)).ToArray());
        }, cancellationToken);
    }

    public async Task<ExpenseRequestAttachmentDownload?> DownloadAsync(
        Guid expenseRequestId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        return await ExecuteReadAsync(async attemptCancellationToken =>
        {
            var request = await requestRepository.FindForAttachmentAuthorizationAsync(
                expenseRequestId,
                attemptCancellationToken);
            if (request is null)
            {
                return null;
            }

            var authority = await ResolveAuthorityAsync(
                request,
                actorExternalId,
                attemptCancellationToken);
            if (!authority.CanRead)
            {
                return null;
            }

            var stored = await attachmentStore.FindContentAsync(
                request.Id,
                attachmentId,
                attemptCancellationToken);
            if (stored?.Content is not { } content)
            {
                return null;
            }

            EnsureStoredContentIsConsistent(stored, content);
            return new ExpenseRequestAttachmentDownload(
                stored.FileName,
                content,
                stored.UploadedAtUtc);
        }, cancellationToken);
    }

    public async Task<ExpenseRequestAttachmentStreamDownload415?> OpenDownload415Async(
        Guid requestId, Guid attachmentId, CancellationToken token)
    {
        var actor = ActorIdentity.RequireBusinessDataAccess(currentActor);
        ExpenseRequestAttachmentStreamDownload415? opened = null;
        try
        {
            return await ExecuteReadAsync(async ct =>
            {
                // A transaction retry must close its previous attempt's file.
                if (opened is not null) { await opened.DisposeAsync(); opened = null; }
                var request = await requestRepository.FindForAttachmentAuthorizationAsync(requestId, ct);
                if (request is null || !(await ResolveAuthorityAsync(request, actor, ct)).CanRead) return null;
                StoredExpenseRequestAttachment metadata;
                if (attachmentStore is IExpenseRequestAttachmentStreamStore415 streams)
                {
                    var stored = await streams.OpenContent415Async(request.Id, attachmentId, ct);
                    if (stored is null) return null;
                    metadata = stored.Metadata;
                    opened = new(metadata.FileName, stored.Content, metadata.UploadedAtUtc);
                }
                else
                {
                    var stored = await attachmentStore.FindContentAsync(request.Id, attachmentId, ct);
                    if (stored?.Content is not { } bytes) return null;
                    EnsureStoredContentIsConsistent(stored, bytes);
                    metadata = stored with { Content = null };
                    opened = new(metadata.FileName, new MemoryStream(bytes, writable: false), metadata.UploadedAtUtc);
                }
                EnsureStoredMetadataIsConsistent(request.Id, [metadata]);
                if (metadata.Id != attachmentId || !opened.Content.CanRead || !opened.Content.CanSeek || opened.Content.CanWrite
                    || opened.Content.Length != metadata.FileSizeBytes)
                    throw new ExpenseRequestAttachmentUnavailableException();
                var hash = await SHA256.HashDataAsync(opened.Content, ct);
                if (!CryptographicOperations.FixedTimeEquals(hash, metadata.ContentSha256))
                    throw new ExpenseRequestAttachmentUnavailableException();
                opened.Content.Position = 0;
                ct.ThrowIfCancellationRequested();
                return opened;
            }, token);
        }
        catch { if (opened is not null) await opened.DisposeAsync(); throw; }
    }

    public async Task<ExpenseRequestAttachmentMutationResult> AddAsync(
        Guid expenseRequestId,
        AddExpenseRequestAttachmentCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var actorExternalId = RequireMutationActor();
        ValidateIdempotencyKey(idempotencyKey);
        var candidate = ValidateAndNormalizeCandidate(command);
        // Freeze the caller-owned buffer before hashing, scanning and storing it.
        // Otherwise a concurrent mutation could turn the scan into a TOCTOU check
        // over bytes different from the persisted payload.
        var content = command.Content.ToArray();
        var contentHash = SHA256.HashData(content);
        var requestHash = ComputeAddHash(expenseRequestId, command.ExpectedVersion, candidate, contentHash);
        var scope = BuildScope("add", expenseRequestId, null, actorExternalId);

        var replay = await TryResolveMutationAsync(
            expenseRequestId,
            scope,
            idempotencyKey,
            requestHash,
            actorExternalId,
            command.ExpectedVersion,
            candidate,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var verdict = await contentScanner.ScanAsync(
            candidate.FileName,
            content,
            cancellationToken);
        if (verdict == ExpenseRequestAttachmentScanVerdict.Unavailable)
        {
            throw new ExpenseRequestAttachmentScanUnavailableException();
        }

        if (verdict != ExpenseRequestAttachmentScanVerdict.Accepted)
        {
            throw new ExpenseRequestAttachmentContentRejectedException();
        }

        try
        {
            return await transactionRunner.ExecuteAsync(async transactionCancellationToken =>
            {
                var storedResponse = await idempotencyStore.FindAsync(
                    scope,
                    idempotencyKey,
                    transactionCancellationToken);
                EnsureIdempotencyHash(storedResponse, requestHash);

                var request = await requestRepository.FindForWorkflowUpdateAsync(
                    expenseRequestId,
                    transactionCancellationToken)
                    ?? throw new ExpenseRequestNotFoundException();
                var authority = await ResolveAuthorityAsync(
                    request,
                    actorExternalId,
                    transactionCancellationToken);
                if (storedResponse is not null)
                {
                    EnsureReplayAuthority(authority, requiresDeleteAuthority: false);
                    return DeserializeMutation(
                        storedResponse,
                        authority,
                        expectsAttachment: true,
                        isReplay: true);
                }

                RequireAddAuthority(request, authority, command.ExpectedVersion, candidate);
                var now = clock.UtcNow;
                var attachment = new StoredExpenseRequestAttachment(
                    Guid.NewGuid(),
                    request.Id,
                    candidate.Kind,
                    candidate.FileName,
                    content.LongLength,
                    contentHash,
                    content,
                    now,
                    actorExternalId,
                    candidate.Comment,
                    IsPostSubmission: !request.Status.IsEditableDraft(),
                    IsSealed: !request.Status.IsEditableDraft());
                attachmentStore.Add(attachment);
                request.RecordAttachmentMutation(
                    command.ExpectedVersion,
                    now,
                    isRemoval: false,
                    attachment.IsPostSubmission);

                var response = new ExpenseRequestAttachmentMutationDto(
                    request.ConcurrencyStamp.ToString("D"),
                    ToDto(attachment, canDelete: !attachment.IsSealed));
                var responseJson = JsonSerializer.Serialize(response, JsonOptions);
                var correlationId = Guid.NewGuid().ToString("N");
                var auditJson = SerializeAuditPayload(attachment, request.ConcurrencyStamp);
                auditSink.Append(
                    Guid.NewGuid(),
                    now,
                    actorExternalId,
                    "ExpenseRequest",
                    request.Id.ToString("D"),
                    "AttachmentAdded",
                    null,
                    auditJson,
                    correlationId);
                outboxSink.Enqueue(
                    Guid.NewGuid(),
                    now,
                    "expense-request.attachment-added.v1",
                    auditJson,
                    correlationId);
                idempotencyStore.Add(
                    scope,
                    idempotencyKey,
                    requestHash,
                    StatusCreated,
                    responseJson,
                    now);
                await unitOfWork.SaveChangesAsync(transactionCancellationToken);
                return new ExpenseRequestAttachmentMutationResult(
                    response,
                    false,
                    StatusCreated);
            }, cancellationToken);
        }
        catch (Exception exception) when (ShouldWrap(exception))
        {
            throw new ExpenseRequestAttachmentUnavailableException(exception);
        }
    }

    public async Task<ExpenseRequestAttachmentMutationResult> DeleteAsync(
        Guid expenseRequestId,
        Guid attachmentId,
        DeleteExpenseRequestAttachmentCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var actorExternalId = RequireMutationActor();
        ValidateExpectedVersion(command.ExpectedVersion);
        ValidateIdempotencyKey(idempotencyKey);
        if (expenseRequestId == Guid.Empty || attachmentId == Guid.Empty)
        {
            throw new ExpenseRequestAttachmentNotFoundException();
        }

        var scope = BuildScope("delete", expenseRequestId, attachmentId, actorExternalId);
        var requestHash = ComputeDeleteHash(
            expenseRequestId,
            attachmentId,
            command.ExpectedVersion);
        try
        {
            return await transactionRunner.ExecuteAsync(async transactionCancellationToken =>
            {
                var storedResponse = await idempotencyStore.FindAsync(
                    scope,
                    idempotencyKey,
                    transactionCancellationToken);
                EnsureIdempotencyHash(storedResponse, requestHash);

                var request = await requestRepository.FindForWorkflowUpdateAsync(
                    expenseRequestId,
                    transactionCancellationToken)
                    ?? throw new ExpenseRequestNotFoundException();
                var authority = await ResolveAuthorityAsync(
                    request,
                    actorExternalId,
                    transactionCancellationToken);
                if (storedResponse is not null)
                {
                    EnsureReplayAuthority(authority, requiresDeleteAuthority: true);
                    return DeserializeMutation(
                        storedResponse,
                        authority,
                        expectsAttachment: false,
                        isReplay: true);
                }

                if (!authority.CanDelete)
                {
                    throw new ActorForbiddenException();
                }

                ValidateExpectedVersion(command.ExpectedVersion);
                if (request.ConcurrencyStamp != command.ExpectedVersion)
                {
                    throw new ExpenseRequestVersionConflictException();
                }

                var removed = await attachmentStore.RemoveAsync(
                    request.Id,
                    attachmentId,
                    transactionCancellationToken)
                    ?? throw new ExpenseRequestAttachmentNotFoundException();
                if (removed.IsSealed)
                {
                    throw new DomainRuleViolationException(
                        "A submitted attachment cannot be deleted.");
                }

                var now = clock.UtcNow;
                request.RecordAttachmentMutation(
                    command.ExpectedVersion,
                    now,
                    isRemoval: true,
                    isPostSubmission: false);
                var response = new ExpenseRequestAttachmentMutationDto(
                    request.ConcurrencyStamp.ToString("D"),
                    null);
                var responseJson = JsonSerializer.Serialize(response, JsonOptions);
                var correlationId = Guid.NewGuid().ToString("N");
                var auditJson = SerializeAuditPayload(removed, request.ConcurrencyStamp);
                auditSink.Append(
                    Guid.NewGuid(),
                    now,
                    actorExternalId,
                    "ExpenseRequest",
                    request.Id.ToString("D"),
                    "AttachmentDeleted",
                    auditJson,
                    responseJson,
                    correlationId);
                outboxSink.Enqueue(
                    Guid.NewGuid(),
                    now,
                    "expense-request.attachment-deleted.v1",
                    auditJson,
                    correlationId);
                idempotencyStore.Add(
                    scope,
                    idempotencyKey,
                    requestHash,
                    StatusOk,
                    responseJson,
                    now);
                await unitOfWork.SaveChangesAsync(transactionCancellationToken);
                return new ExpenseRequestAttachmentMutationResult(
                    response,
                    false,
                    StatusOk);
            }, cancellationToken);
        }
        catch (Exception exception) when (ShouldWrap(exception))
        {
            throw new ExpenseRequestAttachmentUnavailableException(exception);
        }
    }

    public async Task<ExpenseRequestAttachmentMutationDto?> ResolveAddIdempotencyAsync(
        Guid expenseRequestId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actorExternalId = RequireMutationActor();
        ValidateIdempotencyKey(idempotencyKey);
        var scope = BuildScope("add", expenseRequestId, null, actorExternalId);
        return await ResolveStoredResponseAsync(
            expenseRequestId,
            scope,
            idempotencyKey,
            actorExternalId,
            requiresDeleteAuthority: false,
            cancellationToken);
    }

    public async Task<ExpenseRequestAttachmentMutationDto?> ResolveDeleteIdempotencyAsync(
        Guid expenseRequestId,
        Guid attachmentId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actorExternalId = RequireMutationActor();
        ValidateIdempotencyKey(idempotencyKey);
        var scope = BuildScope("delete", expenseRequestId, attachmentId, actorExternalId);
        return await ResolveStoredResponseAsync(
            expenseRequestId,
            scope,
            idempotencyKey,
            actorExternalId,
            requiresDeleteAuthority: true,
            cancellationToken);
    }

    private async Task<ExpenseRequestAttachmentMutationResult?> TryResolveMutationAsync(
        Guid expenseRequestId,
        string scope,
        string idempotencyKey,
        string requestHash,
        string actorExternalId,
        Guid expectedVersion,
        NormalizedAttachmentCandidate candidate,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionRunner.ExecuteAsync(async attemptCancellationToken =>
            {
                var stored = await idempotencyStore.FindAsync(
                    scope,
                    idempotencyKey,
                    attemptCancellationToken);
                EnsureIdempotencyHash(stored, requestHash);
                var request = await requestRepository.FindForAttachmentAuthorizationAsync(
                    expenseRequestId,
                    attemptCancellationToken)
                    ?? throw new ExpenseRequestNotFoundException();
                var authority = await ResolveAuthorityAsync(
                    request,
                    actorExternalId,
                    attemptCancellationToken);
                if (stored is not null)
                {
                    EnsureReplayAuthority(authority, requiresDeleteAuthority: false);
                    return DeserializeMutation(
                        stored,
                        authority,
                        expectsAttachment: true,
                        isReplay: true);
                }

                RequireAddAuthority(request, authority, expectedVersion, candidate);
                return null;
            }, cancellationToken);
        }
        catch (Exception exception) when (ShouldWrap(exception))
        {
            throw new ExpenseRequestAttachmentUnavailableException(exception);
        }
    }

    private async Task<ExpenseRequestAttachmentMutationDto?> ResolveStoredResponseAsync(
        Guid expenseRequestId,
        string scope,
        string idempotencyKey,
        string actorExternalId,
        bool requiresDeleteAuthority,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionRunner.ExecuteAsync(async attemptCancellationToken =>
            {
                var request = await requestRepository.FindForAttachmentAuthorizationAsync(
                    expenseRequestId,
                    attemptCancellationToken);
                if (request is null)
                {
                    throw new ExpenseRequestNotFoundException();
                }

                var authority = await ResolveAuthorityAsync(
                    request,
                    actorExternalId,
                    attemptCancellationToken);
                EnsureReplayAuthority(authority, requiresDeleteAuthority);
                var stored = await idempotencyStore.FindAsync(
                    scope,
                    idempotencyKey,
                    attemptCancellationToken);
                return stored is null
                    ? null
                    : ReprojectReplayCapabilities(
                        DeserializeStoredResponse(
                            stored,
                            expectsAttachment: !requiresDeleteAuthority),
                        authority);
            }, cancellationToken);
        }
        catch (Exception exception) when (ShouldWrap(exception))
        {
            throw new ExpenseRequestAttachmentUnavailableException(exception);
        }
    }

    private async Task<AttachmentAuthority> ResolveAuthorityAsync(
        ExpenseRequest request,
        string actorExternalId,
        CancellationToken cancellationToken)
    {
        if (currentActor.UsesActiveRoleSelection
            && !employeeFunctionalRoleResolver.UsesActiveRoleSelection)
        {
            throw new ExpenseRequestAttachmentUnavailableException();
        }
        var readScope = await currentActor.ResolveBusinessDataReadScopeAsync(cancellationToken);
        if (!readScope.Contains(request)) return new AttachmentAuthority(false, false, false);
        if (currentActor.IsBmsAdministrator)
        {
            return AttachmentAuthority.AdministratorReadOnly;
        }

        var actsForInitiator = string.Equals(
            request.InitiatorExternalId,
            actorExternalId,
            StringComparison.Ordinal);
        if (!actsForInitiator && substitutionAuthority is not null)
        {
            actsForInitiator = (await substitutionAuthority.ResolveAsync(
                request.InitiatorExternalId,
                actorExternalId,
                clock.UtcNow,
                cancellationToken)).IsAuthorized;
        }

        var actor = await employeeActorResolver.ResolveAsync(
            actorExternalId,
            cancellationToken);
        var routeParticipant = false;
        EmployeeFunctionalRoleResolution? roles = null;
        if (actor.EmployeeId is { } employeeId)
        {
            roles = await employeeFunctionalRoleResolver.ResolveAsync(
                employeeId,
                cancellationToken);
            if (request.Status != ExpenseRequestStatus.Draft)
            {
                var snapshot = await approvalRouteSnapshotStore.FindLatestAsync(
                    request.Id,
                    cancellationToken);
                routeParticipant = snapshot?.Stages.Any(stage =>
                    stage.EligibleEmployeeIds.Contains(employeeId)
                    && (!currentActor.UsesActiveRoleSelection
                        || (stage.RoleCode == currentActor.ActiveRoleCode
                            && roles.HasExactRole(stage.RoleCode)))) == true;
            }
        }

        var nsi = request.Status == ExpenseRequestStatus.AwaitingNsi
            && roles?.HasExactRole(ExpenseRequestWorkflowCodes.NsiSpecialistRoleCode) == true;
        var treasury = request.Status is ExpenseRequestStatus.Approved
                or ExpenseRequestStatus.ToPay
                or ExpenseRequestStatus.PartiallyPaid
                or ExpenseRequestStatus.Paid
                or ExpenseRequestStatus.Closed
            && roles?.HasExactRole(ExpenseRequestWorkflowCodes.TreasurerRoleCode) == true;
        var accountingRole = roles?.HasExactRole(ExpenseRequestWorkflowCodes.AccountantRoleCode) == true
            || roles?.HasExactRole(ExpenseRequestWorkflowCodes.ResponsibleAccountantRoleCode) == true;
        var accounting = false;
        if (accountingRole
            && request.Status is (ExpenseRequestStatus.ToPay
                or ExpenseRequestStatus.PartiallyPaid
                or ExpenseRequestStatus.Paid
                or ExpenseRequestStatus.Closed))
        {
            accounting = await HasRequestScopeAsync(
                request,
                actorExternalId,
                cancellationToken);
        }

        var canRead = true; // Already admitted by the unified read scope.
        var canEditDraft = request.Status.IsEditableDraft()
            && actsForInitiator
            && await HasDraftMutationAuthorityAsync(request, actorExternalId, cancellationToken);
        var processParticipant = actsForInitiator
            || routeParticipant
            || nsi
            || treasury
            || accounting;
        var canAdd = canEditDraft
            || (request.Status.CanAcceptAdditionalAttachment() && processParticipant);
        return new AttachmentAuthority(canRead, canAdd, canEditDraft);
    }

    private async Task<bool> HasDraftMutationAuthorityAsync(
        ExpenseRequest request,
        string actorExternalId,
        CancellationToken cancellationToken)
    {
        // ResolveAuthorityAsync has already proved active substitution for
        // this immutable initiator. Resolve the principal's live creator
        // assignments without applying the deputy's employee selector to them.
        // The helper independently rechecks the deputy's selected creator role.
        var principalAuthority = string.Equals(request.InitiatorExternalId, actorExternalId, StringComparison.Ordinal)
            ? budgetOwnerCreationAuthority
            : await budgetOwnerCreationAuthority.ForAuthorizedSubstitutionPrincipalAsync(
                currentActor, cancellationToken);
        var costCenterIds = request.Lines
            .Select(line => line.CostCenterId)
            .Append(request.DefaultCostCenterId)
            .Where(value => value.HasValue && value.Value != Guid.Empty)
            .Select(value => value.GetValueOrDefault())
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        var isDirectInitiator = string.Equals(request.InitiatorExternalId, actorExternalId, StringComparison.Ordinal);
        var asOf = request.PlannedPaymentDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var scopeAllowed = isDirectInitiator
            ? await accessScope.CanUseForExpenseDraftAsync(
                request.InitiatorExternalId,
                request.OrganizationId,
                costCenterIds,
                asOf,
                cancellationToken)
            : await accessScope.CanUseForAuthorizedExpenseDraftAsync(
                request.InitiatorExternalId, request.OrganizationId, costCenterIds, asOf,
                principalAuthority, cancellationToken);
        if (!scopeAllowed)
        {
            return false;
        }

        if (await principalAuthority.HasFinancialDraftAuthorityAsync(
                request.InitiatorExternalId, cancellationToken))
        {
            return true;
        }

        var ownership = request.DefaultCostCenterId is { } costCenterId
            && costCenterId != Guid.Empty
            ? await principalAuthority.ResolveAsync(
                request.InitiatorExternalId,
                costCenterId,
                cancellationToken)
            : await principalAuthority.ResolveAnyAsync(
                request.InitiatorExternalId,
                cancellationToken);
        return ownership.CanCreateExpenseRequest;
    }

    private async Task<bool> HasRequestScopeAsync(
        ExpenseRequest request,
        string actorExternalId,
        CancellationToken cancellationToken)
    {
        var costCenterIds = request.Lines
            .Select(line => line.CostCenterId)
            .Append(request.DefaultCostCenterId)
            .Where(value => value.HasValue && value.Value != Guid.Empty)
            .Select(value => value.GetValueOrDefault())
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        return await accessScope.CanUseAsync(
            actorExternalId,
            request.OrganizationId,
            costCenterIds,
            request.PlannedPaymentDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
            cancellationToken);
    }

    private static NormalizedAttachmentCandidate ValidateAndNormalizeCandidate(
        AddExpenseRequestAttachmentCommand command)
    {
        ValidateExpectedVersion(command.ExpectedVersion);
        if (!Enum.IsDefined(command.Kind) || command.Content is null)
        {
            throw new DomainRuleViolationException("Attachment command is invalid.");
        }

        var result = ExpenseRequestAttachmentUploadPolicy.Evaluate(
            command.FileName,
            command.Content.LongLength);
        if (!result.IsAccepted)
        {
            throw new ExpenseRequestAttachmentContentRejectedException();
        }

        var normalizedFileName = command.FileName.Trim().TrimEnd('.', ' ');
        var normalizedComment = NormalizeOptionalComment(command.Comment);
        return new NormalizedAttachmentCandidate(
            command.Kind,
            normalizedFileName,
            normalizedComment);
    }

    private static string? NormalizeOptionalComment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > MaximumCommentLength || normalized.Any(char.IsControl))
        {
            throw new DomainRuleViolationException("Attachment comment is invalid.");
        }

        return normalized;
    }

    private static void RequireAddAuthority(
        ExpenseRequest request,
        AttachmentAuthority authority,
        Guid expectedVersion,
        NormalizedAttachmentCandidate candidate)
    {
        if (!authority.CanAdd)
        {
            throw new ActorForbiddenException();
        }

        if (request.ConcurrencyStamp != expectedVersion)
        {
            throw new ExpenseRequestVersionConflictException();
        }

        if (request.Status.IsEditableDraft())
        {
            if (candidate.Kind == ExpenseRequestAttachmentKind.NewContractDocument && !request.IsNewContract)
            {
                throw new DomainRuleViolationException(
                    "A new-contract attachment requires the saved new-contract flag.",
                    DomainRuleViolationReason.ExpenseAttachmentNewContractRequiresSave);
            }

            if (candidate.Kind == ExpenseRequestAttachmentKind.AdditionalDocument
                || candidate.Comment is not null)
            {
                throw new DomainRuleViolationException(
                    "Draft attachment kind or comment is invalid.");
            }

            return;
        }

        if (candidate.Kind != ExpenseRequestAttachmentKind.AdditionalDocument
            || candidate.Comment is null)
        {
            throw new DomainRuleViolationException(
                "A post-submission attachment requires its additional-document kind and comment.");
        }
    }

    private string RequireMutationActor()
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (currentActor.IsBmsAdministrator)
        {
            throw new ActorForbiddenException();
        }

        return actorExternalId;
    }

    private static ExpenseRequestAttachmentDto ToDto(
        StoredExpenseRequestAttachment attachment,
        bool canDelete) => new(
        attachment.Id,
        attachment.Kind.ToCode(),
        attachment.FileName,
        attachment.FileSizeBytes,
        attachment.UploadedAtUtc,
        attachment.Comment,
        canDelete);

    private static void EnsureStoredMetadataIsConsistent(
        Guid requestId,
        IReadOnlyCollection<StoredExpenseRequestAttachment> attachments)
    {
        if (attachments.Any(attachment =>
                attachment is null
                || attachment.Id == Guid.Empty
                || attachment.ExpenseRequestId != requestId
                || attachment.FileSizeBytes <= 0
                || attachment.FileSizeBytes > ExpenseRequestAttachmentUploadPolicy.MaximumFileSizeBytes
                || attachment.ContentSha256.Length != 32
                || attachment.Content is not null
                || !ExpenseRequestAttachmentUploadPolicy.Evaluate(
                    attachment.FileName,
                    attachment.FileSizeBytes).IsAccepted
                || (attachment.IsPostSubmission && !attachment.IsSealed)))
        {
            throw new ExpenseRequestAttachmentUnavailableException();
        }
    }

    private static void EnsureStoredContentIsConsistent(
        StoredExpenseRequestAttachment attachment,
        byte[] content)
    {
        if (content.LongLength != attachment.FileSizeBytes
            || attachment.ContentSha256.Length != 32
            || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(content),
                attachment.ContentSha256)
            || !ExpenseRequestAttachmentUploadPolicy.Evaluate(
                attachment.FileName,
                content.LongLength).IsAccepted)
        {
            throw new ExpenseRequestAttachmentUnavailableException();
        }
    }

    private static string ComputeAddHash(
        Guid requestId,
        Guid expectedVersion,
        NormalizedAttachmentCandidate candidate,
        byte[] contentHash) => ComputeHash(new
        {
            Operation = "add",
            RequestId = requestId,
            ExpectedVersion = expectedVersion,
            Kind = candidate.Kind.ToCode(),
            candidate.FileName,
            ContentSha256 = Convert.ToHexString(contentHash),
            candidate.Comment
        });

    private static string ComputeDeleteHash(
        Guid requestId,
        Guid attachmentId,
        Guid expectedVersion) => ComputeHash(new
        {
            Operation = "delete",
            RequestId = requestId,
            AttachmentId = attachmentId,
            ExpectedVersion = expectedVersion
        });

    private static string ComputeHash<T>(T value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions))));

    private static string BuildScope(
        string operation,
        Guid requestId,
        Guid? attachmentId,
        string actorExternalId)
    {
        var actorHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(actorExternalId)));
        return attachmentId.HasValue
            ? $"expense-request|attachment-{operation}|{requestId:N}|{attachmentId.Value:N}|{actorHash}"
            : $"expense-request|attachment-{operation}|{requestId:N}|{actorHash}";
    }

    private static void ValidateExpectedVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty)
        {
            throw new DomainRuleViolationException("ExpectedVersion is required.");
        }
    }

    private static void ValidateIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)
            || idempotencyKey.Length > MaximumIdempotencyKeyLength
            || !string.Equals(idempotencyKey, idempotencyKey.Trim(), StringComparison.Ordinal)
            || idempotencyKey.Any(char.IsControl))
        {
            throw new InvalidIdempotencyKeyException("Idempotency-Key is invalid.");
        }
    }

    private static void EnsureIdempotencyHash(
        StoredIdempotentResponse? stored,
        string requestHash)
    {
        if (stored is not null
            && !string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new IdempotencyConflictException();
        }
    }

    private static void EnsureReplayAuthority(
        AttachmentAuthority authority,
        bool requiresDeleteAuthority)
    {
        var authorized = requiresDeleteAuthority
            ? authority.CanDelete
            : authority.CanAdd;
        if (!authorized || authority.IsAdministrator)
        {
            throw new ActorForbiddenException();
        }
    }

    private static ExpenseRequestAttachmentMutationResult DeserializeMutation(
        StoredIdempotentResponse stored,
        AttachmentAuthority authority,
        bool expectsAttachment,
        bool isReplay) => new(
        ReprojectReplayCapabilities(
            DeserializeStoredResponse(stored, expectsAttachment),
            authority),
        isReplay,
        stored.ResponseStatusCode);

    private static ExpenseRequestAttachmentMutationDto DeserializeStoredResponse(
        StoredIdempotentResponse stored,
        bool expectsAttachment)
    {
        try
        {
            var expectedStatus = expectsAttachment ? StatusCreated : StatusOk;
            var response = JsonSerializer.Deserialize<ExpenseRequestAttachmentMutationDto>(
                stored.ResponseJson,
                JsonOptions) ?? throw new InvalidOperationException();
            if (stored.ResponseStatusCode != expectedStatus
                || !Guid.TryParseExact(response.RequestVersion, "D", out var requestVersion)
                || requestVersion == Guid.Empty
                || expectsAttachment != (response.Attachment is not null)
                || (response.Attachment is { } attachment
                    && !IsValidStoredAttachmentResponse(attachment)))
            {
                throw new InvalidOperationException();
            }

            return response;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new ExpenseRequestAttachmentUnavailableException(exception);
        }
    }

    private static bool IsValidStoredAttachmentResponse(
        ExpenseRequestAttachmentDto attachment)
    {
        if (attachment.Id == Guid.Empty
            || !ExpenseRequestAttachmentKindCode.TryParse(attachment.Kind, out var kind)
            || attachment.FileName != attachment.FileName.Trim()
            || !ExpenseRequestAttachmentUploadPolicy.Evaluate(
                attachment.FileName,
                attachment.FileSizeBytes).IsAccepted
            || attachment.UploadedAtUtc == default
            || attachment.UploadedAtUtc.Offset != TimeSpan.Zero
            || attachment.UploadedAtUtc.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            return false;
        }

        var additional = kind == ExpenseRequestAttachmentKind.AdditionalDocument;
        if (additional != (attachment.Comment is not null)
            || attachment.CanDelete == additional)
        {
            return false;
        }

        return attachment.Comment is null
            || (attachment.Comment.Length <= MaximumCommentLength
                && attachment.Comment == attachment.Comment.Trim()
                && !attachment.Comment.Any(char.IsControl));
    }

    private static ExpenseRequestAttachmentMutationDto ReprojectReplayCapabilities(
        ExpenseRequestAttachmentMutationDto response,
        AttachmentAuthority authority) => response.Attachment is null
        ? response
        : response with
        {
            Attachment = response.Attachment with
            {
                CanDelete = response.Attachment.CanDelete && authority.CanDelete
            }
        };

    private static string SerializeAuditPayload(
        StoredExpenseRequestAttachment attachment,
        Guid requestVersion) => JsonSerializer.Serialize(new
        {
            attachment.ExpenseRequestId,
            AttachmentId = attachment.Id,
            Kind = attachment.Kind.ToCode(),
            attachment.FileName,
            attachment.FileSizeBytes,
            attachment.Comment,
            attachment.IsPostSubmission,
            RequestVersion = requestVersion
        }, JsonOptions);

    private async Task<T> ExecuteReadAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionRunner.ExecuteAsync(action, cancellationToken);
        }
        catch (DomainRuleViolationException exception)
        {
            throw new ExpenseRequestAttachmentUnavailableException(exception);
        }
        catch (Exception exception) when (ShouldWrap(exception))
        {
            throw new ExpenseRequestAttachmentUnavailableException(exception);
        }
    }

    private static bool ShouldWrap(Exception exception) => exception is not
        (OperationCanceledException
        or ActorNotAuthenticatedException
        or ActorForbiddenException
        or ExpenseRequestNotFoundException
        or ExpenseRequestAttachmentNotFoundException
        or ExpenseRequestAttachmentUnavailableException
        or ExpenseRequestAttachmentScanUnavailableException
        or ExpenseRequestAttachmentContentRejectedException
        or InvalidIdempotencyKeyException
        or IdempotencyConflictException
        or ExpenseRequestVersionConflictException
        or DomainRuleViolationException);

    private sealed record NormalizedAttachmentCandidate(
        ExpenseRequestAttachmentKind Kind,
        string FileName,
        string? Comment);

    private sealed record AttachmentAuthority(
        bool CanRead,
        bool CanAdd,
        bool CanDelete,
        bool IsAdministrator = false)
    {
        internal static AttachmentAuthority AdministratorReadOnly { get; } =
            new(true, false, false, true);
    }
}
