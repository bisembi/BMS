using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;

namespace Bms.Application.Budgeting;

/// <summary>
/// Budget-instance commands and bounded member/approver reads. Approval records
/// an immutable version and its audit/idempotency provenance; readers project
/// the effective approved limits at read time without rewriting legacy positions.
/// </summary>
public sealed class BudgetInstanceService(
    IBudgetInstanceStore store,
    BudgetDocumentAuthority authority,
    IBudgetDirectoryStore directory,
    IAccessScope accessScope,
    ICurrentActor actor,
    IBusinessTransactionRunner transactions,
    IIdempotencyStore idempotency,
    IBusinessAuditSink audit,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxTake = 100;
    private const string ObjectType = "BudgetInstance";

    public async Task<BudgetInstancePageDto> ListAsync(
        Guid? costCenterId,
        DateOnly? periodFrom,
        DateOnly? periodTo,
        int skip,
        int take,
        CancellationToken cancellationToken,
        Guid? versionId = null)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        if (skip < 0 || take < 1 || take > MaxTake)
        {
            throw new DomainRuleViolationException("Paging parameters are invalid.");
        }

        EnsurePeriodFilter(periodFrom, periodTo);
        if (versionId == Guid.Empty) throw new DomainRuleViolationException("Version filter is invalid.");

        return await transactions.ExecuteAsync(async token =>
        {
            var employeeId = await authority.RequireEmployeeAsync(actor, token);
            var approverCanSeeAll = await authority.CanApproveAsync(actor, token);
            var readScope = await actor.ResolveBusinessDataReadScopeAsync(token);
            var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
            if (costCenterId is Guid requested && !readScope.Contains(requested))
                throw new ActorForbiddenException();

            // Access for today + ValidFrom is applied inside the store query
            // before COUNT and OFFSET/FETCH — no all-document materialization
            // and no per-document IAccessScope loop.
            var (items, total) = versionId is Guid selectedVersion
                ? await store.ListOwnedBudgetPageForVersionAsync(
                    externalId, employeeId, costCenterId, periodFrom, periodTo, today,
                    skip, take, readScope.CanReadAll, selectedVersion, token)
                : await store.ListOwnedBudgetPageAsync(
                externalId,
                employeeId,
                costCenterId,
                periodFrom,
                periodTo,
                today,
                skip,
                take,
                readScope.CanReadAll,
                token);
            return new BudgetInstancePageDto(
                items.Select(item => new BudgetInstanceListItemDto(
                    item.Id,
                    item.Number,
                    item.CostCenterId,
                    item.ValidFrom,
                    item.ValidTo,
                    item.Status,
                    item.LineCount,
                    item.TotalLimitAmountKzt,
                    item.ConcurrencyStamp,
                    item.CostCenterCode,
                    item.CostCenterName,
                    item.TotalReceiptsAmountKzt,
                    item.TotalPaymentsAmountKzt,
                    item.NetCashFlowAmountKzt,
                    item.IsEffective,
                    CanDelete: !actor.IsBmsAdministrator && approverCanSeeAll
                        && item.Status == nameof(BudgetInstanceStatus.Draft) && item.ApprovalState != "Pending" && !item.IsInCompanyVersion,
                    ApprovalState: item.ApprovalState, AuthorName: item.AuthorName,
                    IncludedVersions: item.IncludedVersions ?? [])).ToArray(),
                skip,
                take,
                total);
        }, cancellationToken);
    }

    public async Task<BudgetInstanceDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        if (id == Guid.Empty)
        {
            throw new BudgetInstanceNotFoundException();
        }

        _ = await authority.RequireEmployeeAsync(actor, cancellationToken);
        var costCenterId = await store.FindCostCenterIdAsync(id, cancellationToken)
            ?? throw new BudgetInstanceNotFoundException();
        return await transactions.ExecuteAsync(async token =>
        {
            await LockVisibleCostCenterAsync(costCenterId, token);
            var instance = await store.FindAsync(id, token)
                ?? throw new BudgetInstanceNotFoundException();
            if (instance.CostCenterId != costCenterId) { throw new BudgetInstanceNotFoundException(); }
            if (!await store.CostCenterIsActiveNonGroupAsync(instance.CostCenterId, token))
            {
                throw new BudgetInstanceNotFoundException();
            }
            var approverCanSeeAll = await authority.CanApproveAsync(actor, token);
            var readScope = await actor.ResolveBusinessDataReadScopeAsync(token);
            if (!readScope.Contains(instance.CostCenterId)) throw new BudgetInstanceNotFoundException();
            var canEdit = false;
            // Visibility does not imply editor/approver authority. Treasury,
            // accounting and management may read a draft without editing it.
            if (!actor.IsBmsAdministrator && instance.Status == BudgetInstanceStatus.Draft
                && !await store.IsInApprovalAsync(instance.Id, token))
            {
                try { _ = await RequireVisibleEditorAsync(externalId, instance, token); canEdit = true; }
                catch (BudgetInstanceNotFoundException) { }
            }
            EnsureDocumentValidityDates(instance);
            var label = await RequireCostCenterLabelAsync(instance.CostCenterId, token);
            return BudgetInstanceMapper.ToDto(instance, canEdit: canEdit,
                canApprove: false,
                costCenterCode: label.Code, costCenterName: label.Name);
        }, cancellationToken);
    }

    public async Task<BudgetInstanceCommandResult> CreateAsync(
        CreateBudgetInstanceCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        RequireBusinessProfile();
        RequireKey(idempotencyKey);
        if (command.Lines is null) { throw new DomainRuleViolationException("Budget lines are required.",
            DomainRuleViolationReason.BudgetLinesInvalid); }
        if (command.CostCenterId == Guid.Empty) { throw new DomainRuleViolationException("Cost center is required."); }
        BudgetInstance.EnsureValidityPeriod(command.ValidFrom, command.ValidTo);
        _ = await authority.RequireEmployeeAsync(actor, cancellationToken);
        var requestHash = Hash(command);
        return await transactions.ExecuteAsync(async token =>
        {
            // CFO is the first retained lock; authority is freshly resolved on
            // every transaction/retry before looking up a stored response.
            var exists = await directory.LockCostCenterAsync(command.CostCenterId, token);
            var readScope = await actor.ResolveBusinessDataReadScopeAsync(token);
            if (!readScope.Contains(command.CostCenterId)) { throw new ActorForbiddenException(); }
            var editor = await RequireEditorAsync(command.CostCenterId, token);
            var employeeId = editor.EmployeeId.GetValueOrDefault();
            if (!exists) { throw new ActorForbiddenException(); }
            // A replay is still a read of the prior result. Do not disclose or
            // return it after the CFO leaves the active, non-group population;
            // the same guard also keeps fresh creation away from the store's
            // lower-level directory-not-found failure.
            if (!await store.CostCenterIsActiveNonGroupAsync(command.CostCenterId, token))
            {
                throw new ActorForbiddenException();
            }
            if (!editor.HasFullBudgetAccess)
            {
                await EnsureBudgetDocumentAccessAsync(
                    externalId,
                    command.CostCenterId,
                    command.ValidFrom,
                    token);
            }
            var scope = Scope("create", employeeId, externalId, command.CostCenterId);
            var replay = await TryReplayAsync(scope, idempotencyKey, requestHash,
                command.CostCenterId, expectedId: null, created: true, token);
            if (replay is not null) { return replay; }

            // Validate the entire draft before spending the ordinal. This
            // temporary aggregate is never attached, audited or returned.
            var drafts = BudgetInstanceMapper.ToDrafts(command.Lines);
            _ = BudgetInstance.CreateDraft("VALIDATION", command.CostCenterId, command.ValidFrom,
                command.ValidTo, employeeId, externalId, drafts);
            await EnsureCashFlowItemsExistAsync(command.Lines, token, command.CostCenterId);
            var label = await RequireCostCenterLabelAsync(command.CostCenterId, token);
            var now = RequireUtc(clock.UtcNow);
            var number = await directory.AllocateNumberAsync(command.CostCenterId,
                BudgetDocumentNumber.CreationDate(now), token);
            var instance = BudgetInstance.CreateDraft(number, command.CostCenterId, command.ValidFrom,
                command.ValidTo, employeeId, externalId, drafts);
            await store.AddAsync(instance, token);
            await store.RecordCreationAsync(instance.Id, now, token);
            var after = BudgetInstanceMapper.ToDto(instance, canEdit: true, canApprove: false,
                costCenterCode: label.Code, costCenterName: label.Name);
            audit.Append(Guid.NewGuid(), now, externalId, ObjectType, instance.Id.ToString("D"),
                "BudgetInstanceDraftCreated", null, JsonSerializer.Serialize(after, JsonOptions),
                Correlation(scope, idempotencyKey));
            idempotency.Add(scope, idempotencyKey, requestHash, 201,
                JsonSerializer.Serialize(new StoredResult(after, Created: true), JsonOptions), now);
            await unitOfWork.SaveChangesAsync(token);
            return new BudgetInstanceCommandResult(after, Created: true);
        }, cancellationToken);
    }

    public async Task<BudgetInstanceCommandResult> UpdateAsync(
        Guid id,
        UpdateBudgetInstanceCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        RequireBusinessProfile();
        ArgumentNullException.ThrowIfNull(command);
        RequireKey(idempotencyKey);
        if (id == Guid.Empty)
        {
            throw new BudgetInstanceNotFoundException();
        }

        if (command.Lines is null || command.Lines.Count == 0)
        {
            throw new DomainRuleViolationException("At least one line is required.",
                DomainRuleViolationReason.BudgetLinesInvalid);
        }

        // Null JSON array elements must fail closed before cash-flow lookups or
        // mapper dereference (safe domain 400, no side effects).
        if (command.Lines.Any(line => line is null))
        {
            throw new DomainRuleViolationException("A budget line draft cannot be null.",
                DomainRuleViolationReason.BudgetLinesInvalid);
        }

        BudgetInstance.EnsureValidityPeriod(command.ValidFrom, command.ValidTo);

        var requestHash = Hash(new { id, command });
        _ = await authority.RequireEmployeeAsync(actor, cancellationToken);
        // A scalar outside the transaction is only a lock-location hint. Never
        // retain document locks before the shared directory/CFO anchor.
        var costCenterId = await store.FindCostCenterIdAsync(id, cancellationToken)
            ?? throw new BudgetInstanceNotFoundException();
        return await transactions.ExecuteAsync(async token =>
        {
            await LockVisibleCostCenterAsync(costCenterId, token);
            var instance = await store.FindAsync(id, token)
                ?? throw new BudgetInstanceNotFoundException();
            if (instance.CostCenterId != costCenterId) { throw new BudgetInstanceNotFoundException(); }
            // Keep direct PUT aligned with list/detail/create/approve: an
            // inactive or group cost centre is outside the budget document
            // boundary even when old membership/access rows remain present.
            if (!await store.CostCenterIsActiveNonGroupAsync(instance.CostCenterId, token))
            {
                throw new BudgetInstanceNotFoundException();
            }
            var editor = await RequireVisibleEditorAsync(externalId, instance, token);
            if (await store.IsInApprovalAsync(instance.Id, token))
                throw new DomainRuleViolationException("Budget is in approval; return it before editing.",
                    DomainRuleViolationReason.BudgetInApproval);
            var employeeId = editor.EmployeeId.GetValueOrDefault();
            if (!editor.HasFullBudgetAccess)
            {
                await EnsureBudgetDocumentAccessAsync(
                    externalId,
                    instance.CostCenterId,
                    command.ValidFrom,
                    token);
            }
            var scope = Scope("update", employeeId, externalId, instance.Id);
            var replay = await TryReplayAsync(scope, idempotencyKey, requestHash,
                instance.CostCenterId, instance.Id, created: false, token);
            if (replay is not null)
            {
                return replay;
            }

            await EnsureCashFlowItemsExistAsync(command.Lines, token, instance.CostCenterId,
                instance.Lines.GroupBy(line => (line.CashFlowItemId, line.BudgetMonth, line.FlowDirection.ToString()))
                    .ToDictionary(group => group.Key, group => group.Count()));
            var label = await RequireCostCenterLabelAsync(instance.CostCenterId, token);
            var before = BudgetInstanceMapper.ToDto(instance, canEdit: true, canApprove: false,
                costCenterCode: label.Code, costCenterName: label.Name);
            instance.ReplaceDraft(
                command.ExpectedConcurrencyStamp,
                command.ValidFrom,
                command.ValidTo,
                BudgetInstanceMapper.ToDrafts(command.Lines));
            await store.ValidateCompanyCompositionWarningsAsync(instance, token);
            var after = BudgetInstanceMapper.ToDto(instance, canEdit: true, canApprove: false,
                costCenterCode: label.Code, costCenterName: label.Name);
            var now = RequireUtc(clock.UtcNow);
            var correlationId = Correlation(scope, idempotencyKey);
            audit.Append(
                Guid.NewGuid(),
                now,
                externalId,
                ObjectType,
                instance.Id.ToString("D"),
                "BudgetInstanceDraftUpdated",
                JsonSerializer.Serialize(before, JsonOptions),
                HistorySnapshot(after, employeeId),
                correlationId);
            idempotency.Add(
                scope,
                idempotencyKey,
                requestHash,
                200,
                JsonSerializer.Serialize(new StoredResult(after, Created: false), JsonOptions),
                now);
            try
            {
                await unitOfWork.SaveChangesAsync(token);
            }
            catch (ExpenseRequestVersionConflictException)
            {
                throw new BudgetInstanceConcurrencyConflictException();
            }

            return new BudgetInstanceCommandResult(after, Created: false);
        }, cancellationToken);
    }

    public async Task<BudgetInstanceCommandResult> ApproveAsync(
        Guid id,
        ApproveBudgetInstanceCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        RequireBusinessProfile();
        RequireKey(idempotencyKey);
        if (id == Guid.Empty)
        {
            throw new BudgetInstanceNotFoundException();
        }

        var requestHash = Hash(new { id, command });
        var costCenterId = await store.FindCostCenterIdAsync(id, cancellationToken)
            ?? throw new BudgetInstanceNotFoundException();

        return await transactions.ExecuteAsync(async token =>
        {
            // The CFO row is the serialization anchor for both approval
            // authority and the per-CFO approval ordinal.
            await LockVisibleCostCenterAsync(costCenterId, token);
            var instance = await store.FindAsync(id, token)
                ?? throw new BudgetInstanceNotFoundException();
            if (instance.CostCenterId != costCenterId)
            {
                throw new BudgetInstanceNotFoundException();
            }

            // Approval is reachable without CFO membership, but it must still
            // target the same active, non-group cost-centre population exposed
            // by the approver queue and detail endpoint. Keep the failure
            // indistinguishable from an unknown document and do it before
            // resolving the approver or allocating an approval ordinal.
            if (!await store.CostCenterIsActiveNonGroupAsync(instance.CostCenterId, token))
            {
                throw new BudgetInstanceNotFoundException();
            }

            var approverEmployeeId = await authority.RequireApproverAsync(actor, token);
            if (instance.Status == BudgetInstanceStatus.Draft)
                throw new DomainRuleViolationException("Submit the budget to the approval route instead of direct approval.");
            // List and detail both fail closed on legacy documents whose
            // header/line period is inconsistent. Approval must not turn such
            // a row into a new authoritative version before that same check.
            EnsureDocumentValidityDates(instance);
            var scope = Scope("approve", approverEmployeeId, externalId, instance.Id);
            var replay = await TryReplayAsync(
                scope,
                idempotencyKey,
                requestHash,
                instance.CostCenterId,
                instance.Id,
                created: false,
                token);
            if (replay is not null)
            {
                return replay;
            }

            var ordinal = await store.AllocateApprovalOrdinalAsync(instance.CostCenterId, token);
            var label = await RequireCostCenterLabelAsync(instance.CostCenterId, token);
            var before = BudgetInstanceMapper.ToDto(
                instance,
                canEdit: true,
                canApprove: false,
                costCenterCode: label.Code,
                costCenterName: label.Name);
            instance.Approve(
                command.ExpectedConcurrencyStamp,
                ordinal,
                RequireUtc(clock.UtcNow),
                approverEmployeeId,
                externalId);
            var after = BudgetInstanceMapper.ToDto(
                instance,
                canEdit: false,
                canApprove: false,
                costCenterCode: label.Code,
                costCenterName: label.Name);
            var now = RequireUtc(clock.UtcNow);
            var correlationId = Correlation(scope, idempotencyKey);
            audit.Append(
                Guid.NewGuid(),
                now,
                externalId,
                ObjectType,
                instance.Id.ToString("D"),
                "BudgetInstanceApproved",
                JsonSerializer.Serialize(before, JsonOptions),
                HistorySnapshot(after, approverEmployeeId),
                correlationId);
            idempotency.Add(
                scope,
                idempotencyKey,
                requestHash,
                200,
                JsonSerializer.Serialize(new StoredResult(after, Created: false), JsonOptions),
                now);
            await unitOfWork.SaveChangesAsync(token);
            return new BudgetInstanceCommandResult(after, Created: false);
        }, cancellationToken);
    }

    public async Task DeleteAsync(
        Guid id, DeleteBudgetInstanceCommand command, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        RequireBusinessProfile();
        RequireKey(idempotencyKey);
        if (id == Guid.Empty || command.CostCenterId == Guid.Empty
            || command.ExpectedConcurrencyStamp == Guid.Empty)
        {
            throw new DomainRuleViolationException("A document, cost center and version are required.");
        }

        var requestHash = Hash(new { id, command });
        await transactions.ExecuteAsync(async token =>
        {
            // The body CFO is a hint, never authority. Lock it BEFORE any
            // retained document/idempotency lock, then recheck the real CFO.
            await LockVisibleCostCenterAsync(command.CostCenterId, token);
            var employeeId = await authority.RequireApproverAsync(actor, token);
            if (!await store.CostCenterIsActiveNonGroupAsync(command.CostCenterId, token))
            {
                throw new BudgetInstanceNotFoundException();
            }
            var scope = Scope("delete", employeeId, externalId, id);
            var stored = await idempotency.FindAsync(scope, idempotencyKey, token);
            if (stored is not null)
            {
                if (!string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    throw new IdempotencyConflictException();
                }
                var replay = JsonSerializer.Deserialize<DeletedResult>(stored.ResponseJson, JsonOptions);
                if (stored.ResponseStatusCode != 204 || replay is null || !replay.Deleted
                    || replay.Id != id || replay.CostCenterId != command.CostCenterId
                    || replay.ExpectedConcurrencyStamp != command.ExpectedConcurrencyStamp
                    || await store.FindAsync(id, token) is not null)
                {
                    throw new DomainRuleViolationException("Stored budget deletion replay is invalid.");
                }
                return true;
            }

            var instance = await store.FindAsync(id, token)
                ?? throw new BudgetInstanceNotFoundException();
            if (instance.CostCenterId != command.CostCenterId)
            {
                throw new BudgetInstanceNotFoundException();
            }
            if (await store.IsInApprovalAsync(instance.Id, token))
                throw new DomainRuleViolationException("An in-approval budget cannot be deleted.");
            instance.EnsureDraftCanBeDeleted(command.ExpectedConcurrencyStamp);
            EnsureDocumentValidityDates(instance);
            var label = await RequireCostCenterLabelAsync(instance.CostCenterId, token);
            var before = BudgetInstanceMapper.ToDto(instance, canEdit: true, canApprove: false,
                costCenterCode: label.Code, costCenterName: label.Name);
            var after = new DeletedResult(id, instance.CostCenterId, command.ExpectedConcurrencyStamp,
                instance.Number, label.Code, label.Name, instance.ValidFrom, instance.ValidTo, Deleted: true);
            var beforeNode = JsonSerializer.SerializeToNode(before, JsonOptions)!.AsObject();
            var deletionHistory = await store.ReadDraftDeletionHistoryAsync(id, token);
            if (deletionHistory is not null)
                beforeNode.Add("deletionHistory", JsonNode.Parse(deletionHistory)!.AsObject());
            await store.RemoveDraftAsync(instance, token);
            var now = RequireUtc(clock.UtcNow);
            var afterJson = JsonSerializer.Serialize(after, JsonOptions);
            audit.Append(Guid.NewGuid(), now, externalId, ObjectType, id.ToString("D"),
                "BudgetInstanceDraftDeleted", beforeNode.ToJsonString(JsonOptions),
                afterJson, Correlation(scope, idempotencyKey));
            idempotency.Add(scope, idempotencyKey, requestHash, 204, afterJson, now);
            try { await unitOfWork.SaveChangesAsync(token); }
            catch (ExpenseRequestVersionConflictException) { throw new BudgetInstanceConcurrencyConflictException(); }
            return true;
        }, cancellationToken);
    }

    public async Task<BudgetDirectoryOptionPageDto> ListCostCentersAsync(
        string? query, DateOnly validFrom, int skip, int take, CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        var search = query?.Trim() ?? string.Empty;
        if (validFrom == default || validFrom.Day != 1 || skip < 0 || take is < 1 or > MaxTake
            || search.Length > 200 || search.Any(char.IsControl))
        {
            throw new DomainRuleViolationException("Budget cost-center query is invalid.");
        }
        return await transactions.ExecuteAsync(async token =>
        {
            var employeeId = await authority.RequireEmployeeAsync(actor, token);
            var readScope = await actor.ResolveBusinessDataReadScopeAsync(token);
            if (readScope.CanReadAll)
                return await store.ListActiveCostCentersAsync(search, skip, take, token);
            return await store.ListVisibleCostCentersAsync(externalId, employeeId, search, validFrom,
                DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), skip, take, token);
        }, cancellationToken);
    }

    public async Task<BudgetDirectoryOptionDto> GetCreatorCostCenterAsync(
        DateOnly validFrom, CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        if (validFrom == default || validFrom.Day != 1)
        {
            throw new DomainRuleViolationException("A valid budget start month is required.");
        }

        return await transactions.ExecuteAsync(async token =>
        {
            var employeeId = await authority.RequireEmployeeAsync(actor, token);
            var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
            var page = await store.ListMemberCostCentersAsync(
                externalId, employeeId, string.Empty, validFrom, today, 0, 2, token);
            if (page.Total != 1 || page.Items.Count != 1)
            {
                throw new DomainRuleViolationException(
                    "The creator must have exactly one active CFO for the budget start month.");
            }

            var item = page.Items[0];
            if (item is null || item.Id == Guid.Empty || !item.IsActive)
            {
                throw new BudgetDocumentAuthorityUnavailableException();
            }

            return item;
        }, cancellationToken);
    }

    public async Task<BudgetInstanceCreationOptionsDto> GetCreationOptionsAsync(
        DateOnly validFrom, CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        if (validFrom == default || validFrom.Day != 1)
            throw new DomainRuleViolationException("A valid budget start month is required.");

        return await transactions.ExecuteAsync(async token =>
        {
            var employeeId = await authority.RequireEmployeeAsync(actor, token);
            var canChooseAny = await authority.CanChooseAnyCostCenterAsync(actor, token);
            // The canonical owner directory is the default, never the first
            // global option or a participant assignment. Ambiguity is not guessed.
            var page = canChooseAny
                ? await store.ListOwnerCostCentersAsync(employeeId, 0, 2, token)
                : await store.ListMemberCostCentersAsync(externalId, employeeId, string.Empty,
                    validFrom, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), 0, 2, token);
            var item = page.Total == 1 && page.Items.Count == 1 ? page.Items[0] : null;
            if (item is not null && (item.Id == Guid.Empty || !item.IsActive))
                throw new BudgetDocumentAuthorityUnavailableException();
            return new BudgetInstanceCreationOptionsDto(canChooseAny, item);
        }, cancellationToken);
    }

    private async Task<bool> HasBudgetDocumentAccessAsync(
        string actorExternalId,
        Guid costCenterId,
        DateOnly validFrom,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var ids = new[] { costCenterId };
        return await accessScope.CanUseAsync(
                   actorExternalId,
                   organizationId: null,
                   ids,
                   today,
                   cancellationToken)
               && await accessScope.CanUseAsync(
                   actorExternalId,
                   organizationId: null,
                   ids,
                   validFrom,
                   cancellationToken);
    }

    private async Task EnsureBudgetDocumentAccessAsync(
        string actorExternalId,
        Guid costCenterId,
        DateOnly validFrom,
        CancellationToken cancellationToken)
    {
        if (!await HasBudgetDocumentAccessAsync(
                actorExternalId,
                costCenterId,
                validFrom,
                cancellationToken))
        {
            throw new ActorForbiddenException();
        }
    }

    private async Task EnsureCashFlowItemsExistAsync(
        IReadOnlyList<BudgetInstanceLineCommand> lines,
        CancellationToken cancellationToken,
        Guid costCenterId,
        IReadOnlyDictionary<(Guid ItemId, DateOnly Month, string Direction), int>? retainedLines = null)
    {
        var remaining = retainedLines?.ToDictionary(pair => pair.Key, pair => pair.Value);
        // The caller retains the same Serializable transaction/CFO anchor.
        // Repeated document rows must not repeat SQL for the same reference;
        // never retain these decisions across attempts or requests.
        var existence = new Dictionary<Guid, bool>();
        var assignments = new Dictionary<Guid, bool>();
        for (var index = 0; index < lines.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = lines[index];
            if (line is null)
            {
                throw new DomainRuleViolationException("A budget line draft cannot be null.",
                    DomainRuleViolationReason.BudgetLinesInvalid, index);
            }

            if (line.CashFlowItemId == Guid.Empty)
            {
                throw new DomainRuleViolationException("Cash-flow item is required on every line.",
                    DomainRuleViolationReason.BudgetLinesInvalid, index);
            }

            if (!existence.TryGetValue(line.CashFlowItemId, out var exists))
            {
                exists = await store.CashFlowItemExistsAsync(line.CashFlowItemId, cancellationToken);
                existence.Add(line.CashFlowItemId, exists);
            }
            if (!exists)
            {
                throw new DomainRuleViolationException("A cash-flow item on the draft does not exist.",
                    DomainRuleViolationReason.BudgetArticleUnavailable, index);
            }
            // Legacy documents remain editable, but new cells and every fresh
            // document (including Copy) must obey the current single-CFO mapping.
            var canonicalDirection = BudgetInstanceMapper.ParseDirection(line.FlowDirection).ToString();
            var key = (line.CashFlowItemId, line.BudgetMonth, canonicalDirection);
            var retained = remaining is not null && remaining.TryGetValue(key, out var count) && count > 0;
            if (retained) remaining![key]--;
            if (!retained)
            {
                if (!assignments.TryGetValue(line.CashFlowItemId, out var assigned))
                {
                    assigned = await store.CashFlowItemBelongsToCostCenterAsync(line.CashFlowItemId, costCenterId, cancellationToken);
                    assignments.Add(line.CashFlowItemId, assigned);
                }
                if (!assigned)
                    throw new DomainRuleViolationException("A new budget line must use an article assigned to its cost center.",
                        DomainRuleViolationReason.BudgetArticleCfoMismatch, index);
            }
        }
    }

    private async Task<BudgetInstanceCostCenterLabelProjection> RequireCostCenterLabelAsync(
        Guid costCenterId, CancellationToken cancellationToken)
    {
        var label = await store.ReadCostCenterLabelAsync(costCenterId, cancellationToken);
        if (label is null || label.CostCenterId != costCenterId
            || !UsableLabel(label.Code, 100) || !UsableLabel(label.Name, 500))
        {
            throw new BudgetDocumentAuthorityUnavailableException();
        }
        return label;
    }

    private static bool UsableLabel(string? value, int limit) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= limit
        && string.Equals(value, value.Trim(), StringComparison.Ordinal) && !value.Any(char.IsControl);

    private async Task<BudgetDocumentEditorResolution> RequireEditorAsync(
        Guid costCenterId,
        CancellationToken cancellationToken)
    {
        var resolution = await authority.ResolveEditorAsync(actor, costCenterId, cancellationToken);
        if (resolution.EmployeeId is not Guid employeeId || employeeId == Guid.Empty)
        {
            throw new ActorForbiddenException();
        }
        return resolution;
    }

    private static string HistorySnapshot(BudgetInstanceDto snapshot, Guid employeeId)
    {
        var json = JsonSerializer.SerializeToNode(snapshot, JsonOptions)!.AsObject();
        // Audit-only provenance: document/API/idempotency contracts remain unchanged.
        json["historyActorEmployeeId"] = employeeId;
        return json.ToJsonString(JsonOptions);
    }

    private async Task LockVisibleCostCenterAsync(Guid costCenterId, CancellationToken cancellationToken)
    {
        var exists = await directory.LockCostCenterAsync(costCenterId, cancellationToken);
        _ = await authority.RequireEmployeeAsync(actor, cancellationToken);
        if (!exists) { throw new BudgetInstanceNotFoundException(); }
    }

    private async Task<BudgetDocumentEditorResolution> RequireVisibleEditorAsync(
        string externalId, BudgetInstance instance, CancellationToken cancellationToken)
    {
        var readScope = await actor.ResolveBusinessDataReadScopeAsync(cancellationToken);
        if (!readScope.Contains(instance.CostCenterId)) throw new BudgetInstanceNotFoundException();
        BudgetDocumentEditorResolution editor;
        try
        {
            editor = await RequireEditorAsync(instance.CostCenterId, cancellationToken);
            if (!editor.HasFullBudgetAccess)
            {
                await EnsureBudgetDocumentAccessAsync(
                    externalId,
                    instance.CostCenterId,
                    instance.ValidFrom,
                    cancellationToken);
            }
        }
        catch (ActorForbiddenException)
        {
            // Missing and inaccessible documents remain indistinguishable.
            throw new BudgetInstanceNotFoundException();
        }
        // Creator fields are provenance only. Validate after visibility so
        // inaccessible corrupt documents cannot disclose their existence.
        EnsureDocumentValidityDates(instance);
        return editor;
    }

    private static void EnsureDocumentValidityDates(BudgetInstance instance)
    {
        BudgetInstance.EnsureValidityPeriod(instance.ValidFrom, instance.ValidTo);

        foreach (var line in instance.Lines)
        {
            if (line.BudgetMonth.Day != 1
                || line.BudgetMonth < instance.ValidFrom
                || line.BudgetMonth > instance.ValidTo)
            {
                throw new DomainRuleViolationException("Budget instance line months are inconsistent with the header.");
            }
        }
    }

    private static void EnsurePeriodFilter(DateOnly? periodFrom, DateOnly? periodTo)
    {
        if ((periodFrom is DateOnly from && from.Day != 1)
            || (periodTo is DateOnly to && to.Day != DateTime.DaysInMonth(to.Year, to.Month))
            || (periodFrom.HasValue && periodTo.HasValue && periodFrom.Value > periodTo.Value))
        {
            throw new DomainRuleViolationException("Budget period filter is invalid.");
        }
    }

    private async Task<BudgetInstanceCommandResult?> TryReplayAsync(
        string scope,
        string idempotencyKey,
        string requestHash,
        Guid costCenterId,
        Guid? expectedId,
        bool created,
        CancellationToken cancellationToken)
    {
        var stored = await idempotency.FindAsync(scope, idempotencyKey, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        if (!string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new IdempotencyConflictException();
        }

        var payload = JsonSerializer.Deserialize<StoredResult>(stored.ResponseJson, JsonOptions)
            ?? throw new DomainRuleViolationException("Stored budget-instance replay is unreadable.");
        if (payload.Instance is null || payload.Instance.Id == Guid.Empty
            || payload.Instance.CostCenterId != costCenterId
            || (expectedId.HasValue && payload.Instance.Id != expectedId.Value)
            || payload.Created != created
            || stored.ResponseStatusCode != (payload.Created ? 201 : 200))
        {
            throw new DomainRuleViolationException("Stored budget-instance replay is invalid.");
        }
        // A renumbering does not rewrite the historical response or its financial
        // payload. Resolve only the current display number under the same CFO
        // lock and fresh authorization; never resurrect a deleted document.
        var currentNumber = await store.ReadNumberAsync(payload.Instance.Id, costCenterId, cancellationToken)
            ?? throw new BudgetInstanceNotFoundException();
        return new BudgetInstanceCommandResult(payload.Instance with { Number = currentNumber }, payload.Created);
    }

    private static string Scope(string operation, Guid employeeId, string externalId, Guid anchor)
    {
        var actorHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(externalId)));
        return $"budget-instance:{operation}:v1:{anchor:N}:{employeeId:N}:{actorHash}";
    }

    private static string Correlation(string scope, string key) =>
        new Guid(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { scope, key }, JsonOptions)).AsSpan(0, 16))
            .ToString("N");

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions)));

    private void RequireBusinessProfile()
    {
        if (actor.IsBmsAdministrator)
        {
            throw new ActorForbiddenException();
        }
    }

    private static void RequireKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 127)
        {
            throw new InvalidIdempotencyKeyException("One idempotency key is required.");
        }
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset now)
    {
        if (now == default || now.Offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException("A UTC clock is required for budget-instance audit.");
        }

        return now;
    }

    private sealed record StoredResult(BudgetInstanceDto Instance, bool Created);

    private sealed record DeletedResult(Guid Id, Guid CostCenterId, Guid ExpectedConcurrencyStamp,
        string Number, string CostCenterCode, string CostCenterName, DateOnly ValidFrom, DateOnly ValidTo, bool Deleted);
}
