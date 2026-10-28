using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Application.ReferenceData;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;

namespace Bms.Application.ExpenseRequests;

public sealed partial class ExpenseRequestWorkflowService
{
    private static readonly IReadOnlySet<string> ExecutableCommandCodes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            ExpenseRequestWorkflowCodes.ApproveCommandCode,
            ExpenseRequestWorkflowCodes.ReturnCommandCode,
            ExpenseRequestWorkflowCodes.RejectCommandCode,
            ExpenseRequestWorkflowCodes.CancelCommandCode,
            ExpenseRequestWorkflowCodes.ConfirmNsiCommandCode,
            ExpenseRequestWorkflowCodes.SendToPaymentCommandCode
        };

    private static readonly string[] CommandAvailabilityOrder =
    [
        ExpenseRequestWorkflowCodes.ApproveCommandCode,
        ExpenseRequestWorkflowCodes.ReturnCommandCode,
        ExpenseRequestWorkflowCodes.RejectCommandCode,
        ExpenseRequestWorkflowCodes.CancelCommandCode,
        ExpenseRequestWorkflowCodes.ConfirmNsiCommandCode,
        ExpenseRequestWorkflowCodes.SendToPaymentCommandCode
    ];

    /// <summary>
    /// Returns only the safe public projection of commands visible to the
    /// current actor. Execution rechecks every condition in its own locked
    /// transaction; this method is discovery, never authorization authority.
    /// </summary>
    public async Task<ExpenseRequestWorkflowCommandAvailabilityResult> GetAvailableCommandsAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (id == Guid.Empty)
        {
            throw new ExpenseRequestNotFoundException();
        }

        try
        {
            var request = await repository.FindVisibleAsync(
                id,
                actorExternalId,
                currentActor.HasFullBusinessDataAccess,
                cancellationToken);
            if (request is null)
            {
                throw new ExpenseRequestNotFoundException();
            }

            var requestVersion = request.ConcurrencyStamp.ToString("D");
            if (currentActor.IsBmsAdministrator
                || currentActor.ActiveRoleCode == ExpenseRequestWorkflowCodes.AuditorRoleCode)
            {
                return new(requestVersion, []);
            }

            var workflow = await ReadVerifiedWorkflowAsync(cancellationToken,
                initiatorRoutes is null ? 1 : await initiatorRoutes.CurrentVersionAsync(request, cancellationToken));
            var context = await BuildDiscoveryContextAsync(
                request,
                workflow,
                actorExternalId,
                cancellationToken);
            var available = WorkflowResolver.AvailableCommands(
                context,
                workflow.Permissions,
                workflow.Commands,
                workflow.Statuses)
                .Where(command => ExecutableCommandCodes.Contains(command.Code))
                .ToDictionary(command => command.Code, StringComparer.Ordinal);

            var commands = CommandAvailabilityOrder
                .Where(available.ContainsKey)
                .Select(commandCode =>
                {
                    var command = available[commandCode];
                    var label = commandCode == ExpenseRequestWorkflowCodes.ApproveCommandCode
                        ? context.SatisfiedConditions.Contains("ROUTE_HAS_NEXT_STAGE")
                            ? "Согласовать"
                            : "Утвердить"
                        : commandCode switch
                        {
                            ExpenseRequestWorkflowCodes.ReturnCommandCode => request.Status is ExpenseRequestStatus.Approved or ExpenseRequestStatus.ToPay
                                ? "На доработку" : "Вернуть",
                            ExpenseRequestWorkflowCodes.RejectCommandCode => "Отклонить",
                            ExpenseRequestWorkflowCodes.CancelCommandCode => "Отменить",
                            ExpenseRequestWorkflowCodes.ConfirmNsiCommandCode => "Подтвердить НСИ",
                            ExpenseRequestWorkflowCodes.SendToPaymentCommandCode => "В оплату",
                            _ => string.Empty
                        };
                    return new ExpenseRequestWorkflowCommandAvailabilityDto(
                        command.Code,
                        label,
                        command.RequiresReason || command.Code == ExpenseRequestWorkflowCodes.CancelCommandCode,
                        command.IsEnabled,
                        command.Code == ExpenseRequestWorkflowCodes.ConfirmNsiCommandCode);
                })
                .ToArray();

            return new(requestVersion, commands);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ExpenseRequestNotFoundException)
        {
            throw;
        }
        catch (ActorNotAuthenticatedException)
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
    /// Executes one of the six post-submit commands that have an approved v1
    /// transition. The request row is locked before any actor, route or
    /// condition is evaluated; audit, outbox and idempotency are committed by
    /// the same Serializable transaction as the aggregate transition.
    /// </summary>
    public async Task<ExpenseRequestWorkflowCommandResult> ExecuteCommandAsync(
        Guid id,
        string commandCode,
        ExpenseRequestWorkflowCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (currentActor.IsBmsAdministrator)
        {
            throw new ActorForbiddenException();
        }

        ArgumentNullException.ThrowIfNull(command);
        ValidateWorkflowCommandCode(commandCode);
        ValidateExpectedVersion(command.ExpectedVersion);
        ValidateIdempotencyKey(idempotencyKey);
        var reason = NormalizeWorkflowReason(command.Reason);
        ValidateCommandPayload(commandCode, command);

        try
        {
            return await transactionRunner.ExecuteAsync(
                transactionCancellationToken => ExecuteCommandInTransactionAsync(
                    id,
                    commandCode,
                    command with { Reason = reason },
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
        catch (ExpenseRequestWorkflowUnavailableException)
        {
            throw;
        }
        catch (DomainRuleViolationException)
        {
            throw;
        }
        catch (ExpenseRequestBudgetChangedException) { throw; }
        catch (Exception exception)
        {
            throw new ExpenseRequestWorkflowUnavailableException(exception);
        }
    }

    /// <summary>
    /// Resolves a command replay without mutating the aggregate. The scope is
    /// specific to both the actor and the command code, and the stored response
    /// is released only after the current authority for that command is proven
    /// again inside the same transaction.
    /// </summary>
    public async Task<ExpenseRequestDto?> ResolveWorkflowCommandIdempotencyAsync(
        Guid id,
        string commandCode,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (currentActor.IsBmsAdministrator)
        {
            throw new ActorForbiddenException();
        }

        ValidateWorkflowCommandCode(commandCode);
        ValidateIdempotencyKey(idempotencyKey);
        if (id == Guid.Empty)
        {
            throw new ExpenseRequestNotFoundException();
        }

        try
        {
            return await transactionRunner.ExecuteAsync<ExpenseRequestDto?>(
                async transactionCancellationToken =>
            {
                var stored = await idempotencyStore.FindAsync(
                    BuildWorkflowCommandIdempotencyScope(id, commandCode, actorExternalId),
                    idempotencyKey,
                    transactionCancellationToken);
                if (stored is null)
                {
                    return null;
                }

                var request = await repository.FindForWorkflowUpdateAsync(
                    id,
                    transactionCancellationToken);
                if (request is null)
                {
                    throw new ExpenseRequestNotFoundException();
                }

                await RevalidateWorkflowCommandAuthorityAsync(
                    request,
                    commandCode,
                    actorExternalId,
                    transactionCancellationToken, stored.ResponseJson);
                return await ReadStoredResponseAsync(stored.ResponseJson, request, transactionCancellationToken,
                    commandCode == ExpenseRequestWorkflowCodes.ConfirmNsiCommandCode);
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

    private async Task<ExpenseRequestWorkflowCommandResult> ExecuteCommandInTransactionAsync(
        Guid id,
        string commandCode,
        ExpenseRequestWorkflowCommand command,
        string idempotencyKey,
        string actorExternalId,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new ExpenseRequestNotFoundException();
        }

        var scope = BuildWorkflowCommandIdempotencyScope(id, commandCode, actorExternalId);
        var requestHash = ComputeWorkflowCommandHash(id, commandCode, command);
        var stored = await idempotencyStore.FindAsync(scope, idempotencyKey, cancellationToken);
        if (stored is not null &&
            !string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new IdempotencyConflictException();
        }

        var request = await repository.FindForWorkflowUpdateAsync(id, cancellationToken);
        if (request is null)
        {
            throw new ExpenseRequestNotFoundException();
        }

        if (stored is not null)
        {
            await RevalidateWorkflowCommandAuthorityAsync(
                request,
                commandCode,
                actorExternalId,
                cancellationToken, stored.ResponseJson);
            return new ExpenseRequestWorkflowCommandResult(
                await ReadStoredResponseAsync(stored.ResponseJson, request, cancellationToken,
                    commandCode == ExpenseRequestWorkflowCodes.ConfirmNsiCommandCode),
                true,
                stored.ResponseStatusCode);
        }

        await RequireDocumentReadScopeAsync(request, cancellationToken);
        if (request.ConcurrencyStamp != command.ExpectedVersion)
        {
            throw new ExpenseRequestVersionConflictException();
        }

        var workflow = await ReadVerifiedWorkflowAsync(cancellationToken,
            initiatorRoutes is null ? 1 : await initiatorRoutes.CurrentVersionAsync(request, cancellationToken));
        var treasuryDecision = request.Status is ExpenseRequestStatus.Approved or ExpenseRequestStatus.ToPay
            && commandCode is ExpenseRequestWorkflowCodes.SendToPaymentCommandCode or ExpenseRequestWorkflowCodes.CancelCommandCode
                or ExpenseRequestWorkflowCodes.ReturnCommandCode;
        if (treasuryDecision && commandCode is ExpenseRequestWorkflowCodes.CancelCommandCode or ExpenseRequestWorkflowCodes.ReturnCommandCode
            && string.IsNullOrWhiteSpace(command.Reason))
            throw new DomainRuleViolationException("A treasury cancellation or return requires a reason.");
        var projections = ExpenseRequestBudgetProjectionCalculator.Calculate(request);
        var context = await BuildWorkflowContextAsync(
            request,
            commandCode,
            command,
            workflow,
            actorExternalId,
            cancellationToken);
        var transition = WorkflowResolver.PlanTransition(
            context,
            commandCode,
            command.Reason,
            workflow.Permissions,
            workflow.Commands,
            workflow.Statuses);

        if (sourceCashPolicy?.RequireSourceCapture == true
            && commandCode is ExpenseRequestWorkflowCodes.ApproveCommandCode or ExpenseRequestWorkflowCodes.SendToPaymentCommandCode)
        {
            // A bank capture can change the route without changing the budget.
            var current = SubmissionBudgetPreflightEvaluator.Evaluate(
                await budgetSnapshotReader.ReadAsync(request.Id, projections.Projections, cancellationToken));
            if (!current.IsReady || current.PositiveDeficitKzt is not { } deficit)
                throw new ExpenseRequestWorkflowUnavailableException();
            var reviewed = request.ActiveApprovalRouteSnapshotNumber is { } active
                ? await approvalRouteSnapshotStore.FindAsync(request.Id, active, cancellationToken)
                : await approvalRouteSnapshotStore.FindLatestAsync(request.Id, cancellationToken);
            if (reviewed is null || reviewed.WorkflowDefinitionVersion != workflow.Version
                || ApprovalRoutePlanner.Plan(deficit, workflow.Version).Any(required =>
                    !reviewed.Stages.Any(stage => stage.StageNumber == required.StageNumber && stage.RoleCode == required.RoleCode)))
                throw new ExpenseRequestBudgetChangedException();
        }

        if (commandCode == ExpenseRequestWorkflowCodes.ApproveCommandCode && !PayrollAdvanceWorkflow372.IsVersion(workflow.Version)
            && await approvalRouteSnapshotStore.CompanyBudgetBaselineChangedAsync(
                request.Id, request.ActiveApprovalRouteSnapshotNumber
                    ?? throw new ExpenseRequestWorkflowUnavailableException(), cancellationToken))
        {
            var currentBudget = SubmissionBudgetPreflightEvaluator.Evaluate(
                await budgetSnapshotReader.ReadAsync(request.Id, projections.Projections, cancellationToken));
            if (!currentBudget.IsReady)
                throw new ExpenseRequestWorkflowUnavailableException();
            // A published budget revision must not turn an already agreed
            // over-budget route into an impossible in-budget-only approval.
            // The immutable submission snapshot is the reviewed ceiling, not
            // zero. Keep every existing participant/role/stage check above;
            // a larger deficit still requires return and a fresh submission.
            var reviewedRoute = await RequireActiveSnapshotAsync(request, workflow, cancellationToken);
            if (currentBudget.PositiveDeficitKzt is null
                || currentBudget.PositiveDeficitKzt > reviewedRoute.PositiveDeficitKzt)
                throw new ExpenseRequestBudgetChangedException();
        }

        // Rejection/cancellation release only this request's persisted reservation. Capture
        // the old composition inside the existing replacement lock protocol:
        // a preceding Serializable list read could retain range locks before
        // acquiring the budget lock. Status/evidence remain in this transaction.
        var releasesReservation = commandCode is ExpenseRequestWorkflowCodes.RejectCommandCode
            or ExpenseRequestWorkflowCodes.CancelCommandCode;
        var previousProjections = releasesReservation
            ? await budgetRepository.ReplaceForRequestAsync(request.Id, [], cancellationToken)
            : projections.Projections;
        var before = ExpenseRequestDto.FromDomain(request, previousProjections);

        var beforeSnapshot = request.ActiveApprovalRouteSnapshotNumber;
        var beforeStage = request.ActiveApprovalRouteStageNumber;
        StoredWorkflowCommandV2Response? replayEvidence = null;
        if (request.Status == ExpenseRequestStatus.InApproval && workflow.Version is >= 2 and <= 21
            && commandCode is ExpenseRequestWorkflowCodes.ApproveCommandCode
                or ExpenseRequestWorkflowCodes.ReturnCommandCode or ExpenseRequestWorkflowCodes.RejectCommandCode)
        {
            var snapshot = await RequireActiveSnapshotAsync(request, workflow, cancellationToken);
            var employee = await RequireActiveEmployeeAsync(actorExternalId, cancellationToken);
            var stage = snapshot.RequireEligibleParticipant(beforeStage, employee);
            replayEvidence = new(null!, beforeSnapshot!.Value, beforeStage, stage.RoleCode, employee);
        }
        var now = clock.UtcNow;
        if (string.Equals(commandCode, ExpenseRequestWorkflowCodes.ConfirmNsiCommandCode, StringComparison.Ordinal))
        {
            await ValidateAndApplyNsiLinksAsync(
                request,
                command,
                cancellationToken,
                now);
        }

        if (!ExpenseRequestStatusCode.TryParse(transition.NextStatus.Code, out var targetStatus))
        {
            throw new ExpenseRequestWorkflowUnavailableException();
        }

        var targetSnapshotNumber = transition.NextStatus.Code == ExpenseRequestWorkflowCodes.InApprovalStatusCode
            ? request.ActiveApprovalRouteSnapshotNumber
            : null;
        var targetStageNumber = transition.NextStatus.Code == ExpenseRequestWorkflowCodes.InApprovalStatusCode
            ? transition.TargetStageNumber
            : 0;
        var paymentHandoffReady = commandCode != ExpenseRequestWorkflowCodes.SendToPaymentCommandCode
            || treasuryPaymentDates is null
            || await treasuryPaymentDates.PrepareHandoffAsync(request.Id, command.ExpectedVersion, actorExternalId, now, cancellationToken);
        if (paymentHandoffReady) request.ApplyWorkflowTransition(
            request.ConcurrencyStamp,
            now,
            request.Status,
            targetStatus,
            targetSnapshotNumber,
            targetStageNumber);

        if (commandCode == ExpenseRequestWorkflowCodes.SendToPaymentCommandCode && treasuryPaymentDates is not null && paymentHandoffReady)
            await treasuryPaymentDates.RecordHandoffAsync(request.Id, command.ExpectedVersion, request.ConcurrencyStamp,
                actorExternalId, now, cancellationToken);

        if (commandCode == ExpenseRequestWorkflowCodes.SendToPaymentCommandCode && treasuryPaymentDates is not null)
            previousProjections = await budgetRepository.ListRequestProjectionsAsync([request.Id], cancellationToken);

        var facts = attachmentStore is null ? SubmissionAttachmentFacts.None
            : await attachmentStore.ReadSubmissionFactsAsync(request.Id, cancellationToken);
        var response = ExpenseRequestDto.FromDomain(request, releasesReservation ? [] : previousProjections, facts);
        var responseJson = JsonSerializer.Serialize(response, JsonOptions);
        var beforeJson = JsonSerializer.Serialize(before, JsonOptions);
        var auditJson = JsonSerializer.Serialize(
            new WorkflowCommandAuditPayload(commandCode, command.Reason, response),
            JsonOptions);
        var correlationId = Guid.NewGuid().ToString("N");
        auditSink.Append(
            Guid.NewGuid(),
            now,
            actorExternalId,
            "ExpenseRequest",
            request.Id.ToString("D"),
            paymentHandoffReady ? AuditAction(commandCode) : "TreasuryRepeatedApprovalRequired",
            beforeJson,
            auditJson,
            correlationId);
        outboxSink.Enqueue(
            Guid.NewGuid(),
            now,
            "expense-request.workflow-command.v1",
            auditJson,
            correlationId);
        idempotencyStore.Add(
            scope,
            idempotencyKey,
            requestHash,
            StatusOk,
            treasuryDecision ? JsonSerializer.Serialize(new StoredTreasuryCommandResponse(response, commandCode), JsonOptions)
                : replayEvidence is null ? responseJson : JsonSerializer.Serialize(replayEvidence with { Response = response }, JsonOptions),
            now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ExpenseRequestWorkflowCommandResult(response, false, StatusOk);
    }

    private async Task<WorkflowEvaluationContext> BuildWorkflowContextAsync(
        ExpenseRequest request,
        string commandCode,
        ExpenseRequestWorkflowCommand command,
        ExpenseRequestWorkflowDefinition workflow,
        string actorExternalId,
        CancellationToken cancellationToken)
    {
        var isInitiator = string.Equals(request.InitiatorExternalId, actorExternalId, StringComparison.Ordinal);
        var canExecuteInitiatorCommands = await IsEffectiveInitiatorAsync(request, actorExternalId, cancellationToken);
        var treasuryState = request.Status is ExpenseRequestStatus.Approved or ExpenseRequestStatus.ToPay;
        if (treasuryState)
        {
            if (commandCode is not (ExpenseRequestWorkflowCodes.SendToPaymentCommandCode
                or ExpenseRequestWorkflowCodes.CancelCommandCode or ExpenseRequestWorkflowCodes.ReturnCommandCode))
                throw new DomainRuleViolationException("Only treasury handoff decisions are allowed after approval.");
            await RequireRoleAsync(actorExternalId, ExpenseRequestWorkflowCodes.TreasurerRoleCode, cancellationToken);
            return WorkflowEvaluationContext.Create(request.Status.ToCode(), workflow.Version, 0,
                [ExpenseRequestWorkflowCodes.TreasurerRoleCode], false, isInitiator, [], canExecuteInitiatorCommands: false);
        }
        if (currentActor.UsesActiveRoleSelection && commandCode == ExpenseRequestWorkflowCodes.CancelCommandCode && !canExecuteInitiatorCommands)
            throw new ActorForbiddenException();
        var stageNumber = request.Status == ExpenseRequestStatus.InApproval
            ? request.ActiveApprovalRouteStageNumber
            : 0;
        var roles = new List<string>();
        var conditions = new HashSet<string>(StringComparer.Ordinal);

        if (commandCode is ExpenseRequestWorkflowCodes.ApproveCommandCode
            or ExpenseRequestWorkflowCodes.ReturnCommandCode
            or ExpenseRequestWorkflowCodes.RejectCommandCode)
        {
            // A finished/other normal state is a domain refusal, not evidence
            // of an unavailable route. Replay, read scope and version have
            // already been checked; a broken active route still fails below.
            if (request.Status != ExpenseRequestStatus.InApproval)
            {
                throw new DomainRuleViolationException("Approval decisions require an IN_APPROVAL request.");
            }

            var snapshot = await RequireActiveSnapshotAsync(request, workflow, cancellationToken);
            var employeeId = await RequireActiveEmployeeAsync(actorExternalId, cancellationToken);
            ApprovalRouteSnapshotStage stage;
            try
            {
                stage = snapshot.RequireEligibleParticipant(stageNumber, employeeId);
            }
            catch (DomainRuleViolationException)
            {
                throw new ActorForbiddenException();
            }

            roles.Add(stage.RoleCode);
            if (currentActor.UsesActiveRoleSelection)
                await RequireRoleAsync(actorExternalId, stage.RoleCode, cancellationToken);
            if (snapshot.WorkflowDefinitionVersion is >= 2 and <= 21)
            {
                if (cfoWorkflowAuthority is null) throw new ExpenseRequestWorkflowUnavailableException();
                await cfoWorkflowAuthority.RequireStageAsync(employeeId, snapshot.CostCenterId, stage.RoleCode, cancellationToken);
            }
            if (commandCode == ExpenseRequestWorkflowCodes.ApproveCommandCode)
            {
                if (stageNumber < snapshot.Stages.Count)
                {
                    conditions.Add("ROUTE_HAS_NEXT_STAGE");
                }
                else if (request.IsNewCounterparty || request.IsNewContract)
                {
                    conditions.Add("ROUTE_LAST_REQUIRES_NSI");
                }
                else
                {
                    conditions.Add("ROUTE_LAST_NO_NSI");
                }
            }
        }
        else if (commandCode == ExpenseRequestWorkflowCodes.ConfirmNsiCommandCode)
        {
            await RequireRoleAsync(
                actorExternalId,
                ExpenseRequestWorkflowCodes.NsiSpecialistRoleCode,
                cancellationToken);
            if (await IsNsiLinkReadyAsync(request, command, cancellationToken))
            {
                conditions.Add("NSI_CANDIDATE_LINKED");
            }

            roles.Add(ExpenseRequestWorkflowCodes.NsiSpecialistRoleCode);
        }
        else if (commandCode == ExpenseRequestWorkflowCodes.SendToPaymentCommandCode)
        {
            await RequireRoleAsync(
                actorExternalId,
                ExpenseRequestWorkflowCodes.TreasurerRoleCode,
                cancellationToken);
            roles.Add(ExpenseRequestWorkflowCodes.TreasurerRoleCode);
        }

        return WorkflowEvaluationContext.Create(
            request.Status.ToCode(),
            workflow.Version,
            stageNumber,
            roles,
            isBmsAdministrator: false,
            isInitiator,
            conditions,
            canExecuteInitiatorCommands);
    }

    private async Task<WorkflowEvaluationContext> BuildDiscoveryContextAsync(
        ExpenseRequest request,
        ExpenseRequestWorkflowDefinition workflow,
        string actorExternalId,
        CancellationToken cancellationToken)
    {
        var isInitiator = string.Equals(request.InitiatorExternalId, actorExternalId, StringComparison.Ordinal);
        var canExecuteInitiatorCommands = await IsEffectiveInitiatorAsync(request, actorExternalId, cancellationToken);
        var stageNumber = request.Status == ExpenseRequestStatus.InApproval
            ? request.ActiveApprovalRouteStageNumber
            : 0;
        var roles = new List<string>();
        var conditions = new HashSet<string>(StringComparer.Ordinal);

        var actor = await employeeActorResolver.ResolveAsync(
            actorExternalId,
            cancellationToken);
        if (request.Status is ExpenseRequestStatus.Approved or ExpenseRequestStatus.ToPay)
        {
            if (actor.EmployeeId is Guid employee)
            {
                var treasuryRoles = await employeeFunctionalRoleResolver.ResolveAsync(employee, cancellationToken);
                if (treasuryRoles.HasExactRole(ExpenseRequestWorkflowCodes.TreasurerRoleCode))
                {
                    await RequireRoleAsync(actorExternalId, ExpenseRequestWorkflowCodes.TreasurerRoleCode, cancellationToken);
                    roles.Add(ExpenseRequestWorkflowCodes.TreasurerRoleCode);
                }
            }
            return WorkflowEvaluationContext.Create(request.Status.ToCode(), workflow.Version, 0, roles, false,
                isInitiator, conditions, canExecuteInitiatorCommands: false);
        }
        if (request.Status == ExpenseRequestStatus.InApproval)
        {
            // Discovery must use the same immutable participants as execution,
            // not grant decisions to a later holder of the stage's current role.
            // A missing/broken route remains unavailable even for an unmapped actor.
            var snapshot = await RequireActiveSnapshotAsync(request, workflow, cancellationToken);
            var stage = snapshot.Stages.SingleOrDefault(candidate => candidate.StageNumber == stageNumber)
                ?? throw new ExpenseRequestWorkflowUnavailableException();
            if (actor.EmployeeId is { } employeeId && stage.EligibleEmployeeIds.Contains(employeeId))
            {
                stage = snapshot.RequireEligibleParticipant(stageNumber, employeeId);
                if (currentActor.UsesActiveRoleSelection)
                {
                    try { await RequireRoleAsync(actorExternalId, stage.RoleCode, cancellationToken); }
                    catch (ActorForbiddenException)
                    {
                        return WorkflowEvaluationContext.Create(request.Status.ToCode(), workflow.Version,
                            stageNumber, [], false, isInitiator, conditions, canExecuteInitiatorCommands);
                    }
                }
                if (snapshot.WorkflowDefinitionVersion is >= 2 and <= 21)
                {
                    if (cfoWorkflowAuthority is null) throw new ExpenseRequestWorkflowUnavailableException();
                    try { await cfoWorkflowAuthority.RequireStageAsync(employeeId, snapshot.CostCenterId, stage.RoleCode, cancellationToken); }
                    catch (ActorForbiddenException)
                    {
                        return WorkflowEvaluationContext.Create(request.Status.ToCode(), workflow.Version,
                            stageNumber, [], false, isInitiator, conditions, canExecuteInitiatorCommands);
                    }
                }
                roles.Add(stage.RoleCode);
                if (stageNumber < snapshot.Stages.Count)
                    conditions.Add("ROUTE_HAS_NEXT_STAGE");
                else if (request.IsNewCounterparty || request.IsNewContract)
                    conditions.Add("ROUTE_LAST_REQUIRES_NSI");
                else
                    conditions.Add("ROUTE_LAST_NO_NSI");
            }
        }
        else if (actor.EmployeeId is { } employeeId)
        {
            var resolvedRoles = await employeeFunctionalRoleResolver.ResolveAsync(employeeId, cancellationToken);
            roles.AddRange(resolvedRoles.ActiveRoleCodes);
        }

        return WorkflowEvaluationContext.Create(
            request.Status.ToCode(),
            workflow.Version,
            stageNumber,
            roles,
            isBmsAdministrator: false,
            isInitiator,
            conditions,
            canExecuteInitiatorCommands);
    }

    private async Task<ApprovalRouteSnapshot> RequireActiveSnapshotAsync(
        ExpenseRequest request,
        ExpenseRequestWorkflowDefinition workflow,
        CancellationToken cancellationToken)
    {
        if (request.Status != ExpenseRequestStatus.InApproval
            || request.ActiveApprovalRouteSnapshotNumber is not { } snapshotNumber
            || snapshotNumber <= 0
            || request.ActiveApprovalRouteStageNumber <= 0)
        {
            throw new ExpenseRequestWorkflowUnavailableException();
        }

        var snapshot = await approvalRouteSnapshotStore.FindAsync(
            request.Id,
            snapshotNumber,
            cancellationToken);
        if (snapshot is null
            || snapshot.WorkflowDefinitionVersion != workflow.Version
            || request.OrganizationId is not { } organizationId
            || request.DefaultCostCenterId is not { } costCenterId
            || snapshot.OrganizationId != organizationId
            || snapshot.CostCenterId != costCenterId
            || snapshot.Stages.Count == 0
            || request.ActiveApprovalRouteStageNumber > snapshot.Stages.Count)
        {
            throw new ExpenseRequestWorkflowUnavailableException();
        }

        return snapshot;
    }

    /// <summary>
    /// Proves that the actor may still issue the command before a stored
    /// response is released. The transition itself cannot be replanned, because
    /// a replay observes the aggregate after the command already moved it; what
    /// is re-proven is the standing authority the command path required: an
    /// active employee, current route participation, the current functional
    /// role, or current initiator identity. Revocation after the response was
    /// stored therefore stops the replay. The method only reads.
    /// </summary>
    private async Task RevalidateWorkflowCommandAuthorityAsync(
        ExpenseRequest request,
        string commandCode,
        string actorExternalId,
        CancellationToken cancellationToken, string storedJson)
    {
        await RequireWorkflowReplayReadScopeAsync(request, commandCode, cancellationToken);
        using var treasuryDocument = JsonDocument.Parse(storedJson);
        if (treasuryDocument.RootElement.TryGetProperty("treasuryCommandCode", out var treasuryCommand))
        {
            var treasury = JsonSerializer.Deserialize<StoredTreasuryCommandResponse>(storedJson, JsonOptions);
            if (treasuryCommand.GetString() != commandCode || treasury?.Response.Id != request.Id
                || commandCode is not (ExpenseRequestWorkflowCodes.SendToPaymentCommandCode
                    or ExpenseRequestWorkflowCodes.CancelCommandCode or ExpenseRequestWorkflowCodes.ReturnCommandCode))
                throw new ActorForbiddenException();
            await RequireRoleAsync(actorExternalId, ExpenseRequestWorkflowCodes.TreasurerRoleCode, cancellationToken);
            return;
        }
        if (commandCode is ExpenseRequestWorkflowCodes.ApproveCommandCode
            or ExpenseRequestWorkflowCodes.ReturnCommandCode
            or ExpenseRequestWorkflowCodes.RejectCommandCode)
        {
            var employeeId = await RequireActiveEmployeeAsync(actorExternalId, cancellationToken);
            using var storedDocument = JsonDocument.Parse(storedJson);
            if (storedDocument.RootElement.TryGetProperty("response", out _))
            {
                var evidence = JsonSerializer.Deserialize<StoredWorkflowCommandV2Response>(storedJson, JsonOptions)
                    ?? throw new ExpenseRequestWorkflowUnavailableException();
                var original = await approvalRouteSnapshotStore.FindAsync(request.Id, evidence.SnapshotNumber, cancellationToken);
                if (original is null || original.WorkflowDefinitionVersion is not (>= 2 and <= 21) || original.CostCenterId != request.DefaultCostCenterId
                    || evidence.EmployeeId != employeeId || evidence.Response.Id != request.Id || cfoWorkflowAuthority is null)
                    throw new ActorForbiddenException();
                var stage = original.RequireEligibleParticipant(evidence.StageNumber, employeeId);
                if (stage.RoleCode != evidence.RoleCode) throw new ExpenseRequestWorkflowUnavailableException();
                if (currentActor.UsesActiveRoleSelection)
                    await RequireRoleAsync(actorExternalId, stage.RoleCode, cancellationToken);
                await cfoWorkflowAuthority.RequireStageAsync(employeeId, original.CostCenterId, stage.RoleCode, cancellationToken);
                return;
            }
            var latest = await approvalRouteSnapshotStore.FindLatestAsync(request.Id, cancellationToken);
            if (latest?.WorkflowDefinitionVersion is >= 2 and <= 21)
                throw new ExpenseRequestWorkflowUnavailableException();
            if (currentActor.UsesActiveRoleSelection)
            {
                if (latest is null) throw new ActorForbiddenException();
                var selected = currentActor.ActiveRoleCode;
                var hasSelectedParticipation = latest.Stages.Any(stage =>
                    stage.RoleCode == selected && stage.EligibleEmployeeIds.Contains(employeeId));
                if (!hasSelectedParticipation || selected is null) throw new ActorForbiddenException();
                await RequireRoleAsync(actorExternalId, selected, cancellationToken);
            }
            if (request.ActiveApprovalRouteSnapshotNumber is not { } snapshotNumber
                || snapshotNumber <= 0)
            {
                // The route this command belonged to is no longer active, so
                // stage participation cannot be re-proven. The active-employee
                // check above stays the effective boundary.
                return;
            }

            var snapshot = await approvalRouteSnapshotStore.FindAsync(
                request.Id,
                snapshotNumber,
                cancellationToken);
            if (snapshot is null)
            {
                throw new ExpenseRequestWorkflowUnavailableException();
            }

            var participates = snapshot.Stages.Any(
                stage => stage.EligibleEmployeeIds.Contains(employeeId));
            if (!participates)
            {
                throw new ActorForbiddenException();
            }

            return;
        }

        if (commandCode == ExpenseRequestWorkflowCodes.ConfirmNsiCommandCode)
        {
            await RequireRoleAsync(
                actorExternalId,
                ExpenseRequestWorkflowCodes.NsiSpecialistRoleCode,
                cancellationToken);
            return;
        }

        if (commandCode == ExpenseRequestWorkflowCodes.SendToPaymentCommandCode)
        {
            await RequireRoleAsync(
                actorExternalId,
                ExpenseRequestWorkflowCodes.TreasurerRoleCode,
                cancellationToken);
            return;
        }

        if (commandCode == ExpenseRequestWorkflowCodes.CancelCommandCode
            && !await IsEffectiveInitiatorAsync(request, actorExternalId, cancellationToken))
        {
            throw new ActorForbiddenException();
        }
    }

    private sealed record StoredWorkflowCommandV2Response(ExpenseRequestDto Response, int SnapshotNumber,
        int StageNumber, string RoleCode, Guid EmployeeId);
    private sealed record StoredTreasuryCommandResponse(ExpenseRequestDto Response, string TreasuryCommandCode);

    private async Task<bool> IsEffectiveInitiatorAsync(ExpenseRequest request, string actorExternalId,
        CancellationToken cancellationToken) =>
        string.Equals(request.InitiatorExternalId, actorExternalId, StringComparison.Ordinal)
        && (!currentActor.UsesActiveRoleSelection
            || await currentActor.CanCreateExpenseRequestAsync(cancellationToken));

    private async Task<Guid> RequireActiveEmployeeAsync(
        string actorExternalId,
        CancellationToken cancellationToken)
    {
        var actor = await employeeActorResolver.ResolveAsync(actorExternalId, cancellationToken);
        if (!actor.HasActiveEmployee || actor.EmployeeId is not { } employeeId)
        {
            throw new ActorForbiddenException();
        }

        return employeeId;
    }

    private async Task RequireRoleAsync(
        string actorExternalId,
        string roleCode,
        CancellationToken cancellationToken)
    {
        var employeeId = await RequireActiveEmployeeAsync(actorExternalId, cancellationToken);
        var roles = await employeeFunctionalRoleResolver.ResolveAsync(employeeId, cancellationToken);
        if (!roles.HasExactRole(roleCode))
        {
            throw new ActorForbiddenException();
        }
    }

    private async Task<bool> IsNsiLinkReadyAsync(
        ExpenseRequest request,
        ExpenseRequestWorkflowCommand command,
        CancellationToken cancellationToken)
    {
        if (!request.IsNewCounterparty && !request.IsNewContract)
        {
            return false;
        }

        if ((!request.IsNewCounterparty && command.CounterpartyId is not null)
            || (!request.IsNewContract && command.ContractId is not null))
        {
            return false;
        }

        var projection = await nsiReferenceProjectionReader.ReadAsync(cancellationToken);
        var linkedCounterpartyId = request.IsNewCounterparty
            ? command.CounterpartyId
            : request.CounterpartyId;
        if ((request.IsNewCounterparty
                && linkedCounterpartyId is not { } counterpartyId)
            || (linkedCounterpartyId is { } nonEmptyCounterpartyId
                && nonEmptyCounterpartyId == Guid.Empty))
        {
            return false;
        }

        if (linkedCounterpartyId is { } selectedCounterpartyId
            && !projection.Counterparties.Any(item => item.Id == selectedCounterpartyId && item.IsActive))
        {
            return false;
        }

        if (request.IsNewContract)
        {
            if (command.ContractId is not { } contractId || contractId == Guid.Empty)
            {
                return false;
            }

            var contract = projection.Contracts.SingleOrDefault(item => item.Id == contractId);
            return contract is not null
                && contract.IsActive
                && linkedCounterpartyId is { } selectedCounterparty
                && contract.CounterpartyId == selectedCounterparty
                && request.OrganizationId is { } organizationId
                && contract.OrganizationId == organizationId
                && (contractValidator is null || await contractValidator.MatchesAsync(
                    contract.Id, request.OrganizationId, linkedCounterpartyId, request.CurrencyCode, cancellationToken));
        }

        return !request.IsNewCounterparty || linkedCounterpartyId is not null;
    }

    private async Task ValidateAndApplyNsiLinksAsync(
        ExpenseRequest request,
        ExpenseRequestWorkflowCommand command,
        CancellationToken cancellationToken,
        DateTimeOffset now)
    {
        if (!await IsNsiLinkReadyAsync(request, command, cancellationToken))
        {
            throw new DomainRuleViolationException("The selected NSI candidate is not an active imported object.");
        }

        request.ApplyNsiReferenceLinks(
            request.ConcurrencyStamp,
            now,
            command.CounterpartyId,
            command.ContractId);
    }

    private static void ValidateWorkflowCommandCode(string commandCode)
    {
        if (string.IsNullOrWhiteSpace(commandCode)
            || !ExecutableCommandCodes.Contains(commandCode))
        {
            throw new DomainRuleViolationException("The requested workflow command is not available.");
        }
    }

    private static void ValidateCommandPayload(
        string commandCode,
        ExpenseRequestWorkflowCommand command)
    {
        if (commandCode != ExpenseRequestWorkflowCodes.ConfirmNsiCommandCode
            && (command.CounterpartyId is not null || command.ContractId is not null))
        {
            throw new DomainRuleViolationException("NSI links are accepted only by CONFIRM_NSI.");
        }

        if (command.CounterpartyId == Guid.Empty || command.ContractId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Workflow reference identifiers must be non-empty.");
        }
    }

    private static string? NormalizeWorkflowReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var normalized = reason.Trim();
        if (normalized.Length > 2_000 || normalized.Any(char.IsControl))
        {
            throw new DomainRuleViolationException("Workflow reason is invalid.");
        }

        return normalized;
    }

    private static string ComputeWorkflowCommandHash(
        Guid id,
        string commandCode,
        ExpenseRequestWorkflowCommand command)
    {
        var canonical = string.Join(
            ":",
            "expense-request-workflow-v1",
            id.ToString("D"),
            commandCode,
            command.ExpectedVersion.ToString("D"),
            command.Reason ?? string.Empty,
            command.CounterpartyId?.ToString("D") ?? string.Empty,
            command.ContractId?.ToString("D") ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string AuditAction(string commandCode) => commandCode switch
    {
        ExpenseRequestWorkflowCodes.ApproveCommandCode => "WorkflowApproved",
        ExpenseRequestWorkflowCodes.ReturnCommandCode => "WorkflowReturned",
        ExpenseRequestWorkflowCodes.RejectCommandCode => "WorkflowRejected",
        ExpenseRequestWorkflowCodes.CancelCommandCode => "WorkflowCancelled",
        ExpenseRequestWorkflowCodes.ConfirmNsiCommandCode => "WorkflowNsiConfirmed",
        ExpenseRequestWorkflowCodes.SendToPaymentCommandCode => "WorkflowSentToPayment",
        _ => "WorkflowCommand"
    };

    private sealed record WorkflowCommandAuditPayload(
        string CommandCode,
        string? Reason,
        ExpenseRequestDto ExpenseRequest);
}
