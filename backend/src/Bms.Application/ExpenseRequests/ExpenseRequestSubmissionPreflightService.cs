using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;

namespace Bms.Application.ExpenseRequests;

public sealed class ExpenseRequestSubmissionPreflightService(
    IExpenseRequestRepository expenseRequestRepository,
    ISubmissionBudgetSnapshotReader budgetSnapshotReader,
    IAccrualSnapshotSourceResolver accrualSnapshotSourceResolver,
    IExpenseRequestWorkflowDefinitionReader workflowDefinitionReader,
    ApprovalRouteParticipantResolver approvalRouteParticipantResolver,
    BudgetOwnerCreationAuthority budgetOwnerCreationAuthority,
    IBusinessTransactionRunner transactionRunner,
    IClock clock,
    ICurrentActor currentActor,
    ExpenseRequestSubstitutionAuthority? substitutionAuthority = null,
    IExpenseRequestAttachmentReader? attachmentReader = null,
    ExpenseInitiatorRouteResolver? initiatorRoutes = null,
    CurrencySubmissionReadiness? currencyReadiness = null,
    IExpenseRequestContractValidator? contractValidator = null)
{
    public async Task<ExpenseRequestSubmissionPreflightDto?> EvaluateAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        try
        {
            return await transactionRunner.ExecuteAsync(async transactionCancellationToken =>
            {
                var request = await expenseRequestRepository.FindVisibleAsync(
                    requestId,
                    actorExternalId,
                    currentActor.HasFullBusinessDataAccess,
                    transactionCancellationToken);
                if (request is null)
                {
                    return null;
                }

                var attachmentFacts = attachmentReader is null
                    ? SubmissionAttachmentFacts.None
                    : await attachmentReader.ReadSubmissionFactsAsync(
                        request.Id,
                        transactionCancellationToken);
                var readiness = SubmissionReadinessEvaluator.Evaluate(request, attachmentFacts);
                if (contractValidator is not null && !await contractValidator.MatchesAsync(
                    request.ContractId, request.OrganizationId, request.CounterpartyId, request.CurrencyCode, transactionCancellationToken))
                    readiness = new SubmissionReadiness(false, readiness.MissingRequirements.Concat(["ContractIdOrIsNewContract"]).Distinct().ToArray());
                if (currencyReadiness is not null && !await currencyReadiness.IsCurrentAsync(request, transactionCancellationToken))
                    readiness = new SubmissionReadiness(false, readiness.MissingRequirements.Concat(["CurrencyRate"]).Distinct().ToArray());
                var projection = ExpenseRequestBudgetProjectionCalculator.Calculate(request);
                var budgetPreflight = projection.IsComplete
                    ? SubmissionBudgetPreflightEvaluator.Evaluate(
                        await budgetSnapshotReader.ReadAsync(
                            request.Id,
                            projection.Projections,
                            transactionCancellationToken))
                    : new SubmissionBudgetPreflight(
                        false,
                        null,
                        [SubmissionBudgetPreflightBlockers.ProjectionIncomplete],
                        []);
                var accrualResolution = await accrualSnapshotSourceResolver.ResolveAsync(
                    request.Lines
                        .OrderBy(line => line.SortOrder)
                        .Select((line, index) => new AccrualSnapshotSourceLine(
                            index + 1,
                            line.Id,
                            line.CashFlowItemId,
                            line.AccrualPeriod))
                        .ToArray(),
                    transactionCancellationToken);

                var creatorReady = false;
                var financialCreator = false;
                ExpenseRequestActionAuthority? actionAuthority = null;
                if (!currentActor.IsBmsAdministrator &&
                    request.DefaultCostCenterId is { } costCenterId &&
                    costCenterId != Guid.Empty)
                {
                    actionAuthority = string.Equals(
                            request.InitiatorExternalId,
                            actorExternalId,
                            StringComparison.Ordinal)
                        ? ExpenseRequestActionAuthority.Direct(actorExternalId)
                        : substitutionAuthority is null
                            ? ExpenseRequestActionAuthority.Denied(
                                request.InitiatorExternalId,
                                actorExternalId)
                            : await substitutionAuthority.ResolveAsync(
                                request.InitiatorExternalId,
                                actorExternalId,
                                clock.UtcNow,
                                transactionCancellationToken);
                    if (actionAuthority.IsAuthorized)
                    {
                        try
                        {
                            var principalAuthority = actionAuthority.InitiatorExternalId == currentActor.ExternalId
                                ? budgetOwnerCreationAuthority
                                : await budgetOwnerCreationAuthority.ForAuthorizedSubstitutionPrincipalAsync(currentActor, transactionCancellationToken);
                            creatorReady = await currentActor.CanCreateExpenseRequestAsync(transactionCancellationToken)
                                && await principalAuthority.CanCreateForCostCenterAsync(actionAuthority.InitiatorExternalId,
                                costCenterId, transactionCancellationToken);
                            financialCreator = creatorReady && await principalAuthority.HasFinancialDraftAuthorityAsync(
                                actionAuthority.InitiatorExternalId, transactionCancellationToken);
                        }
                        catch (ActorForbiddenException) { }
                    }
                }

                var version = 1;
                if (creatorReady && initiatorRoutes is not null)
                    version = await initiatorRoutes.SubmissionVersionAsync(request, transactionCancellationToken, actionAuthority,
                        budgetPreflight.PositiveDeficitKzt ?? 0m);
                var workflowSource = await workflowDefinitionReader.ReadVersionAsync(version, transactionCancellationToken);
                ExpenseRequestWorkflowDefinition? workflow = null;
                var workflowReady = ExpenseRequestWorkflowDefinitions.TryCreate(version, workflowSource.Statuses,
                    workflowSource.Commands, workflowSource.Permissions, out workflow);

                var roleAssignmentsReady = false;
                workflowReady &= !financialCreator || initiatorRoutes is not null;
                if (workflowReady && budgetPreflight.IsReady && creatorReady)
                {
                    try
                    {
                        if (initiatorRoutes is null)
                            _ = await approvalRouteParticipantResolver.ResolveAsync(budgetPreflight.RoutePreview, transactionCancellationToken);
                        else
                            _ = await initiatorRoutes.ResolveAsync(request, version, budgetPreflight.PositiveDeficitKzt!.Value,
                                actorExternalId, transactionCancellationToken);
                        roleAssignmentsReady = true;
                    }
                    catch (ApprovalRouteParticipantResolutionUnavailableException)
                    {
                        // This expected projection deficiency is a visible,
                        // safe no-submit state, not a provider outage.
                    }
                    catch (Bms.Domain.Common.DomainRuleViolationException) { }
                }
                var accrualSourceReady = accrualResolution.Issues.Count == 0;

                var blockers = new List<SubmissionPreflightBlockerDto>();
                if (currentActor.IsBmsAdministrator)
                {
                    blockers.Add(new SubmissionPreflightBlockerDto(
                        SubmissionPreflightBlockerCategories.Command,
                        SubmissionPreflightBlockerCodes.AdministratorReadOnly));
                }
                else if (!creatorReady)
                {
                    blockers.Add(new SubmissionPreflightBlockerDto(
                        SubmissionPreflightBlockerCategories.Command,
                        SubmissionPreflightBlockerCodes.BudgetOwnerRequired));
                }

                blockers.AddRange(budgetPreflight.Blockers.Select(code =>
                    new SubmissionPreflightBlockerDto(
                        SubmissionPreflightBlockerCategories.Budget,
                        code)));
                blockers.AddRange(accrualResolution.Issues.Select(issue =>
                    new SubmissionPreflightBlockerDto(
                        issue.Code == SubmissionPreflightBlockerCodes.AccrualSnapshotSourceUnavailable
                            ? SubmissionPreflightBlockerCategories.Accrual
                            : SubmissionPreflightBlockerCategories.Data,
                        issue.Code,
                        issue.LineNumber)));
                if (!workflowReady)
                {
                    blockers.Add(new SubmissionPreflightBlockerDto(
                        SubmissionPreflightBlockerCategories.Workflow,
                        SubmissionPreflightBlockerCodes.WorkflowDefinitionUnavailable));
                    blockers.Add(new SubmissionPreflightBlockerDto(
                        SubmissionPreflightBlockerCategories.Workflow,
                        SubmissionPreflightBlockerCodes.WorkflowSubmitCommandUnavailable));
                }

                if (!roleAssignmentsReady)
                {
                    blockers.Add(new SubmissionPreflightBlockerDto(
                        SubmissionPreflightBlockerCategories.RoleAssignments,
                        SubmissionPreflightBlockerCodes.ApprovalRoleAssignmentsUnavailable));
                }

                var routePreview = budgetPreflight.IsReady
                    ? new SubmissionRoutePreviewDto(
                        true,
                        request.PaymentOperation?.IsPayrollAdvance == true ? 0m : budgetPreflight.PositiveDeficitKzt!.Value,
                        ApprovalRoutePlanner.Plan(budgetPreflight.PositiveDeficitKzt!.Value, version)
                            .Select(stage => stage.RoleCode)
                            .ToArray(),
                        roleAssignmentsReady)
                    : null;
                var canSubmit = !currentActor.IsBmsAdministrator &&
                    creatorReady &&
                    request.Status.IsEditableDraft() &&
                    readiness.IsReady &&
                    budgetPreflight.IsReady &&
                    accrualSourceReady &&
                    workflowReady &&
                    roleAssignmentsReady;

                return new ExpenseRequestSubmissionPreflightDto(
                    request.Id,
                    request.ConcurrencyStamp,
                    clock.UtcNow,
                    new SubmissionReadinessDto(
                        readiness.IsReady,
                        readiness.MissingRequirements),
                    new SubmissionBudgetReadinessDto(
                        budgetPreflight.IsReady,
                        request.PaymentOperation?.IsPayrollAdvance == true && budgetPreflight.IsReady ? 0m : budgetPreflight.PositiveDeficitKzt,
                        budgetPreflight.Blockers),
                    new SubmissionPreflightCapabilitiesDto(
                        // Production DI supplies the SQL-backed reader. The
                        // null fallback exists only for older isolated unit
                        // constructors and remains a conservative zero-file fact.
                        AttachmentFactsReady: true,
                        AccrualSnapshotSourceReady: accrualSourceReady,
                        WorkflowSeedReady: workflowReady,
                        RoleAssignmentsReady: roleAssignmentsReady),
                    routePreview,
                    canSubmit,
                    blockers, request.PaymentOperation?.IsPayrollAdvance == true ? ExpensePaymentOperation.PayrollAdvanceKind : null);
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
        catch (SubmissionPreflightUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new SubmissionPreflightUnavailableException(
                "Submission preflight could not obtain a consistent read-only result.",
                exception);
        }
    }

}
