using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;

namespace Bms.Application.ExpenseRequests;

/// <summary>
/// Executes the approved expense-request workflow. The submission path is
/// intentionally separate from draft editing: it resolves every authoritative
/// prerequisite, captures the route, materializes immutable accrual data and
/// changes status in one Serializable transaction.
/// </summary>
public sealed partial class ExpenseRequestWorkflowService(
    IExpenseRequestRepository repository,
    ISubmissionBudgetSnapshotReader budgetSnapshotReader,
    IAccrualSnapshotMaterializer accrualSnapshotMaterializer,
    IExpenseRequestWorkflowDefinitionReader workflowDefinitionReader,
    IApprovalRouteSnapshotStore approvalRouteSnapshotStore,
    ApprovalRouteParticipantResolver approvalRouteParticipantResolver,
    BudgetOwnerCreationAuthority budgetOwnerCreationAuthority,
    IBusinessAuditSink auditSink,
    IOutboxSink outboxSink,
    IIdempotencyStore idempotencyStore,
    IBusinessTransactionRunner transactionRunner,
    IUnitOfWork unitOfWork,
    IClock clock,
    ICurrentActor currentActor,
    EmployeeActorResolver employeeActorResolver,
    EmployeeFunctionalRoleResolver employeeFunctionalRoleResolver,
    INsiReferenceProjectionReader nsiReferenceProjectionReader,
    IBudgetRepository budgetRepository,
    ExpenseRequestSubstitutionAuthority? substitutionAuthority = null,
    IExpenseRequestAttachmentStore? attachmentStore = null,
    ExpenseInitiatorRouteResolver? initiatorRoutes = null,
    CfoWorkflowAuthority? cfoWorkflowAuthority = null,
    CurrencySubmissionReadiness? currencyReadiness = null,
    IExpenseRequestContractValidator? contractValidator = null,
    ITreasuryPaymentDateJournal? treasuryPaymentDates = null,
    IAccrualDepartmentCatalog? accrualDepartments = null,
    ApprovalAccrualEditPolicy? accrualEditPolicy = null,
    SourceCashBudgetControlPolicy? sourceCashPolicy = null)
{
    private const int MaximumIdempotencyKeyLength = 127;
    private const int MaximumIdempotencyScopeLength = 323;
    private const int StatusOk = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SubmitExpenseRequestResult> SubmitAsync(
        Guid id,
        SubmitExpenseRequestCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (currentActor.IsBmsAdministrator)
        {
            throw new ActorForbiddenException();
        }

        ArgumentNullException.ThrowIfNull(command);
        ValidateExpectedVersion(command.ExpectedVersion);
        ValidateIdempotencyKey(idempotencyKey);
        try
        {
            return await transactionRunner.ExecuteAsync(
                transactionCancellationToken => SubmitInTransactionAsync(
                    id,
                    command,
                    idempotencyKey,
                    actorExternalId,
                    transactionCancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ActorNotAuthenticatedException)
        {
            throw;
        }
        catch (ActorForbiddenException)
        {
            throw;
        }
        catch (IdempotencyConflictException)
        {
            throw;
        }
        catch (InvalidIdempotencyKeyException)
        {
            throw;
        }
        catch (ExpenseRequestNotFoundException)
        {
            throw;
        }
        catch (ExpenseRequestVersionConflictException)
        {
            throw;
        }
        catch (ExpenseRequestSubmissionBlockedException)
        {
            throw;
        }
        catch (DomainRuleViolationException)
        {
            throw;
        }
        catch (ExpenseRequestWorkflowUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ExpenseRequestWorkflowUnavailableException(exception);
        }
    }

    /// <summary>
    /// Resolves a submit command that may have reached the server before the
    /// browser lost its response. The actor-specific scope prevents one
    /// employee from observing another employee's business command.
    /// </summary>
    public async Task<ExpenseRequestDto?> ResolveSubmitIdempotencyAsync(
        Guid id,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (currentActor.IsBmsAdministrator)
        {
            throw new ActorForbiddenException();
        }

        if (id == Guid.Empty)
        {
            throw new ExpenseRequestNotFoundException();
        }

        ValidateIdempotencyKey(idempotencyKey);
        try
        {
            return await transactionRunner.ExecuteAsync<ExpenseRequestDto?>(
                async transactionCancellationToken =>
            {
                var stored = await idempotencyStore.FindAsync(
                    BuildIdempotencyScope("submit", id, actorExternalId),
                    idempotencyKey,
                    transactionCancellationToken);
                if (stored is null)
                {
                    return null;
                }

                // A stored response is not a standing permission. Submit is
                // initiator-gated, so replay has to prove the actor still owns
                // the request, exactly as the command path proves it before it
                // writes. Ownership revoked after the response was stored must
                // stop the replay instead of serving the captured aggregate.
                var request = await repository.FindForWorkflowUpdateAsync(
                    id,
                    transactionCancellationToken);
                if (request is null)
                {
                    throw new ExpenseRequestNotFoundException();
                }

                var actionAuthority = await RequireSubmitAuthorityAsync(
                    request,
                    actorExternalId,
                    clock.UtcNow,
                    transactionCancellationToken);
                _ = await RequireSubmitCreationAuthorityAsync(
                    request,
                    actionAuthority,
                    transactionCancellationToken);

                await RevalidateSeniorSubmissionAsync(request, actionAuthority, stored.ResponseJson, transactionCancellationToken);

                return await ReadStoredResponseAsync(stored.ResponseJson, request, transactionCancellationToken);
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ActorNotAuthenticatedException)
        {
            throw;
        }
        catch (ActorForbiddenException)
        {
            throw;
        }
        catch (InvalidIdempotencyKeyException)
        {
            throw;
        }
        catch (ExpenseRequestNotFoundException)
        {
            throw;
        }
        catch (ExpenseRequestWorkflowUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ExpenseRequestWorkflowUnavailableException(exception);
        }
    }

    private async Task<SubmitExpenseRequestResult> SubmitInTransactionAsync(
        Guid id,
        SubmitExpenseRequestCommand command,
        string idempotencyKey,
        string actorExternalId,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new ExpenseRequestNotFoundException();
        }

        var scope = BuildIdempotencyScope("submit", id, actorExternalId);
        var requestHash = ComputeSubmitHash(id, command);
        var stored = await idempotencyStore.FindAsync(scope, idempotencyKey, cancellationToken);
        if (stored is not null &&
            !string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new IdempotencyConflictException();
        }

        var authorizationAtUtc = clock.UtcNow;
        var request = await repository.FindForWorkflowUpdateAsync(
            id,
            cancellationToken);
        if (request is null)
        {
            throw new ExpenseRequestNotFoundException();
        }

        var actionAuthority = await RequireSubmitAuthorityAsync(
            request,
            actorExternalId,
            authorizationAtUtc,
            cancellationToken);

        if (stored is not null)
        {
            _ = await RequireSubmitCreationAuthorityAsync(
                request,
                actionAuthority,
                cancellationToken);
            await RevalidateSeniorSubmissionAsync(request, actionAuthority, stored.ResponseJson, cancellationToken);
            return new SubmitExpenseRequestResult(
                await ReadStoredResponseAsync(stored.ResponseJson, request, cancellationToken),
                true,
                stored.ResponseStatusCode);
        }

        if (request.ConcurrencyStamp != command.ExpectedVersion)
        {
            throw new ExpenseRequestVersionConflictException();
        }

        EnsureSubmitShape(request);
        if (contractValidator is not null && !await contractValidator.MatchesAsync(
            request.ContractId, request.OrganizationId, request.CounterpartyId, request.CurrencyCode, cancellationToken))
            throw new ExpenseRequestSubmissionBlockedException();
        if (currencyReadiness is not null && !await currencyReadiness.IsCurrentAsync(request, cancellationToken))
            throw new ExpenseRequestSubmissionBlockedException();
        var organizationId = RequireOrganizationId(request);
        var costCenterId = await RequireSubmitCreationAuthorityAsync(
            request,
            actionAuthority,
            cancellationToken);

        var attachmentFacts = attachmentStore is null
            ? SubmissionAttachmentFacts.None
            : await attachmentStore.ReadSubmissionFactsAsync(
                request.Id,
                cancellationToken);
        var readiness = SubmissionReadinessEvaluator.Evaluate(request, attachmentFacts);
        if (!readiness.IsReady)
        {
            throw new ExpenseRequestSubmissionBlockedException();
        }

        var projections = ExpenseRequestBudgetProjectionCalculator.Calculate(request);
        if (!projections.IsComplete)
        {
            throw new ExpenseRequestSubmissionBlockedException();
        }

        var budgetPreflight = SubmissionBudgetPreflightEvaluator.Evaluate(
            await budgetSnapshotReader.ReadAsync(
                request.Id,
                projections.Projections,
                cancellationToken));
        if (!budgetPreflight.IsReady || budgetPreflight.PositiveDeficitKzt is null)
        {
            throw new ExpenseRequestSubmissionBlockedException();
        }

        var definitionVersion = initiatorRoutes is null ? 1 : await initiatorRoutes.SubmissionVersionAsync(request, cancellationToken,
            actionAuthority, budgetPreflight.PositiveDeficitKzt.Value);
        var workflow = await ReadVerifiedWorkflowAsync(cancellationToken, definitionVersion);
        var before = ExpenseRequestDto.FromDomain(request, projections.Projections);
        var beforeAccrualSnapshot = request.Status == ExpenseRequestStatus.Returned
            ? SerializeAccrualSnapshotAudit(request) : null;
        var prepared = await accrualSnapshotMaterializer.PrepareAsync(
            request,
            command.ExpectedVersion,
            cancellationToken);
        if (!prepared.IsReady || prepared.RequestVersion == Guid.Empty)
        {
            throw new ExpenseRequestSubmissionBlockedException();
        }

        var snapshotNumber = await approvalRouteSnapshotStore.NextSnapshotNumberAsync(
            request.Id,
            cancellationToken);
        var routeParticipants = initiatorRoutes is null
            ? await approvalRouteParticipantResolver.ResolveAsync(budgetPreflight.RoutePreview, cancellationToken)
            : await initiatorRoutes.ResolveAsync(request, definitionVersion, budgetPreflight.PositiveDeficitKzt.Value,
                actorExternalId, cancellationToken);
        var now = authorizationAtUtc;
        var snapshot = ApprovalRouteSnapshot.Create(
            request.Id,
            snapshotNumber,
            workflow.Version,
            organizationId,
            costCenterId,
            budgetPreflight.PositiveDeficitKzt.Value,
            now,
            routeParticipants);
        approvalRouteSnapshotStore.Add(snapshot);
        await approvalRouteSnapshotStore.CaptureCompanyBudgetBaselineAsync(request.Id, snapshotNumber, cancellationToken);

        var context = WorkflowEvaluationContext.Create(
            request.Status.ToCode(),
            workflow.Version,
            stageNumber: 0,
            functionalRoles: [],
            isBmsAdministrator: false,
            isInitiatorOfDocument: true,
            satisfiedConditions: [PayrollAdvanceWorkflow372.IsVersion(workflow.Version) ? "SUBMISSION_READY"
                : ExpenseRequestWorkflowDefinition251.IsVersion(workflow.Version)
                ? ExpenseRequestWorkflowDefinition251.SubmissionCondition(budgetPreflight.PositiveDeficitKzt.Value,
                    workflow.Version, request.IsNewCounterparty || request.IsNewContract)
                : workflow.Version is 3 or 4
                ? ExpenseRequestWorkflowDefinitionV3.SubmissionCondition(budgetPreflight.PositiveDeficitKzt.Value,
                    request.IsNewCounterparty || request.IsNewContract)
                : "SUBMISSION_READY"]);
        var transition = WorkflowResolver.PlanTransition(
            context,
            ExpenseRequestWorkflowCodes.SubmitCommandCode,
            reason: null,
            workflow.Permissions,
            workflow.Commands,
            workflow.Statuses);
        if (!ExpenseRequestStatusCode.TryParse(transition.NextStatus.Code, out var targetStatus)
            || (snapshot.Stages.Count > 0
                ? targetStatus != ExpenseRequestStatus.InApproval || transition.TargetStageNumber != 1
                : (workflow.Version is not (3 or 4 or 8 or 12) && !PayrollAdvanceWorkflow372.IsVersion(workflow.Version)) || transition.TargetStageNumber != 0
                    || targetStatus != (request.IsNewCounterparty || request.IsNewContract
                        ? ExpenseRequestStatus.AwaitingNsi : workflow.Version is 8 or 12 || PayrollAdvanceWorkflow372.IsVersion(workflow.Version)
                            ? ExpenseRequestStatus.Approved : ExpenseRequestStatus.ToPay)))
        {
            throw new ExpenseRequestWorkflowUnavailableException();
        }

        var expectedStatus = request.Status;
        request.InitializePaymentPurposeForSubmission(
            prepared.RequestVersion,
            now);
        request.ApplyWorkflowTransition(
            request.ConcurrencyStamp,
            now,
            expectedStatus,
            targetStatus,
            targetStatus == ExpenseRequestStatus.InApproval ? snapshotNumber : null,
            transition.TargetStageNumber);
        if (attachmentStore is not null)
        {
            await attachmentStore.SealForSubmissionAsync(request.Id, cancellationToken);
        }

        var response = ExpenseRequestDto.FromDomain(request, projections.Projections, attachmentFacts);
        var responseJson = JsonSerializer.Serialize(response, JsonOptions);
        var beforeJson = JsonSerializer.Serialize(before, JsonOptions);
        var correlationId = Guid.NewGuid().ToString("N");
        if (beforeAccrualSnapshot is not null && prepared.RequestVersion != command.ExpectedVersion)
        {
            // Keep private line settings out of the public response/outbox and
            // out of the ordinary request-history projection. The same commit
            // retains their exact old/new pairs in business audit.
            auditSink.Append(Guid.NewGuid(), now, actorExternalId, "ExpenseRequestAccrualSnapshot",
                request.Id.ToString("D"), "RefreshedOnResubmission", beforeAccrualSnapshot,
                SerializeAccrualSnapshotAudit(request), correlationId);
        }
        auditSink.Append(
            Guid.NewGuid(),
            now,
            actorExternalId,
            "ExpenseRequest",
            request.Id.ToString("D"),
            targetStatus == ExpenseRequestStatus.Approved ? "SubmittedApproved"
                : targetStatus == ExpenseRequestStatus.ToPay ? "SubmittedToPayment"
                : targetStatus == ExpenseRequestStatus.AwaitingNsi ? "SubmittedToNsi" : "SubmittedForApproval",
            beforeJson,
            responseJson,
            correlationId);
        ExpenseRequestSubstitutionAudit.Append(
            auditSink,
            request.Id,
            now,
            actionAuthority,
            "SubmittedBySubstitute",
            correlationId);
        outboxSink.Enqueue(
            Guid.NewGuid(),
            now,
            "expense-request.submitted.v1",
            responseJson,
            correlationId);
        idempotencyStore.Add(
            scope,
            idempotencyKey,
            requestHash,
            StatusOk,
            workflow.Version is 3 or 4 or 5 or 8 or 9 or 10 or 11 or 12 or 13 || PayrollAdvanceWorkflow372.IsVersion(workflow.Version)
                ? JsonSerializer.Serialize(new StoredSeniorSubmissionResponse(response, snapshotNumber), JsonOptions)
                : responseJson,
            now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new SubmitExpenseRequestResult(response, false, StatusOk);
    }

    private static void EnsureSubmitShape(ExpenseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Status.IsEditableDraft()
            || request.OrganizationId is not { } organizationId
            || organizationId == Guid.Empty
            || request.DefaultCostCenterId is not { } costCenterId
            || costCenterId == Guid.Empty
            || request.Lines.Count == 0
            || request.Lines.Any(line => line is null || line.CostCenterId != costCenterId))
        {
            throw new ExpenseRequestSubmissionBlockedException();
        }
    }

    private static Guid RequireOrganizationId(ExpenseRequest request)
    {
        if (request.OrganizationId is not { } organizationId || organizationId == Guid.Empty)
        {
            throw new ExpenseRequestSubmissionBlockedException();
        }

        return organizationId;
    }

    private static Guid RequireCostCenterId(ExpenseRequest request)
    {
        if (request.DefaultCostCenterId is not { } costCenterId || costCenterId == Guid.Empty)
        {
            throw new ExpenseRequestSubmissionBlockedException();
        }

        return costCenterId;
    }

    private async Task<ExpenseRequestWorkflowDefinition> ReadVerifiedWorkflowAsync(
        CancellationToken cancellationToken, int version = 1)
    {
        var source = await workflowDefinitionReader.ReadVersionAsync(version, cancellationToken);
        ExpenseRequestWorkflowDefinition? workflow = null;
        var valid = ExpenseRequestWorkflowDefinitions.TryCreate(version, source.Statuses, source.Commands, source.Permissions, out workflow);
        if (!valid || workflow is null)
        {
            throw new ExpenseRequestWorkflowUnavailableException();
        }

        return ExpenseRequestWorkflowDefinition251.WithTreasuryRules(workflow);
    }

    private sealed record StoredSeniorSubmissionResponse(ExpenseRequestDto Response, int SeniorSubmissionSnapshotNumber);

    private async Task RevalidateSeniorSubmissionAsync(ExpenseRequest request, ExpenseRequestActionAuthority authority,
        string storedJson, CancellationToken token)
    {
        using var stored = JsonDocument.Parse(storedJson);
        if (!stored.RootElement.TryGetProperty("seniorSubmissionSnapshotNumber", out var number)) return;
        var snapshot = await approvalRouteSnapshotStore.FindAsync(request.Id, number.GetInt32(), token);
        if (snapshot is null || (snapshot.WorkflowDefinitionVersion is not (3 or 4 or 5 or 8 or 9 or 10 or 11 or 12 or 13)
                && !PayrollAdvanceWorkflow372.IsVersion(snapshot.WorkflowDefinitionVersion))
            || snapshot.CostCenterId != request.DefaultCostCenterId || initiatorRoutes is null || !authority.IsAuthorized)
            throw new ActorForbiddenException();
        _ = await initiatorRoutes.ResolveAsync(request, snapshot.WorkflowDefinitionVersion, snapshot.PositiveDeficitKzt,
            authority.ActorExternalId, token);
    }

    private static string ComputeSubmitHash(Guid id, SubmitExpenseRequestCommand command)
    {
        var canonical = $"expense-request-submit-v1:{id:D}:{command.ExpectedVersion:D}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string BuildIdempotencyScope(
        string operation,
        Guid requestId,
        string actorExternalId)
    {
        var scope = $"expense-requests:{operation}:{requestId:D}:{actorExternalId}";
        if (scope.Length > MaximumIdempotencyScopeLength
            || !string.Equals(scope, scope.Trim(), StringComparison.Ordinal)
            || scope.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "The canonical idempotency scope exceeds the persisted contract.");
        }

        return scope;
    }

    /// <summary>
    /// Scopes a workflow-command idempotency record to one command. The
    /// stored response of REJECT must not be reachable through the APPROVE
    /// route: a compact discriminator belongs to the identity of the record,
    /// not only to its payload hash, because the replay endpoint never sees
    /// the payload.
    /// </summary>
    private static string BuildWorkflowCommandIdempotencyScope(
        Guid requestId,
        string commandCode,
        string actorExternalId)
    {
        var discriminator = WorkflowCommandScopeDiscriminator(commandCode);
        var scope = $"expense-requests:workflow:{discriminator}:{requestId:D}:{actorExternalId}";
        if (scope.Length > MaximumIdempotencyScopeLength
            || !string.Equals(scope, scope.Trim(), StringComparison.Ordinal)
            || scope.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "The canonical idempotency scope exceeds the persisted contract.");
        }

        return scope;
    }

    /// <summary>
    /// Keeps the command part of the persisted scope both unambiguous and
    /// compact. A full 256-character actor id plus a raw command code would
    /// exceed the nvarchar(323) database contract.
    /// </summary>
    private static char WorkflowCommandScopeDiscriminator(string commandCode) => commandCode switch
    {
        ExpenseRequestWorkflowCodes.ApproveCommandCode => 'A',
        ExpenseRequestWorkflowCodes.ReturnCommandCode => 'B',
        ExpenseRequestWorkflowCodes.RejectCommandCode => 'R',
        ExpenseRequestWorkflowCodes.CancelCommandCode => 'C',
        ExpenseRequestWorkflowCodes.ConfirmNsiCommandCode => 'N',
        ExpenseRequestWorkflowCodes.SendToPaymentCommandCode => 'P',
        _ => throw new InvalidOperationException("The workflow command scope is unsupported.")
    };

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

    private static void ValidateExpectedVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty)
        {
            throw new ExpenseRequestVersionConflictException();
        }
    }

    private static string SerializeAccrualSnapshotAudit(ExpenseRequest request) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            requestId = request.Id,
            lines = request.Lines.Select(line => new
            {
                lineId = line.Id,
                formsManagerialAccrual = line.AccrualSnapshotFormsManagerialAccrual,
                expenseItemId = line.AccrualSnapshotExpenseItemId
            }).ToArray()
        }, JsonOptions);

    private static ExpenseRequestDto DeserializeStoredResponse(string responseJson)
    {
        try
        {
            var response = JsonSerializer.Deserialize<ExpenseRequestDto>(responseJson, JsonOptions)
                ?? throw new InvalidOperationException("Stored response is invalid.");
            if (response.Id == Guid.Empty
                || !ExpenseRequestStatusCode.TryParse(response.Status, out _)
                || response.ConcurrencyStamp == Guid.Empty
                || response.Lines is null
                || response.BudgetReservation is null
                || response.SubmissionReadiness is null)
            {
                throw new InvalidOperationException("Stored response is invalid.");
            }

            return response;
        }
        catch (JsonException exception)
        {
            throw new ExpenseRequestWorkflowUnavailableException(exception);
        }
    }
}
