using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Application.Budgeting;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// Reading one expense request and the filtered page.
public sealed partial class ExpenseRequestService
{

    public async Task<ExpenseRequestDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        var includeAll = currentActor.HasFullBusinessDataAccess;
        return await transactionRunner.ExecuteAsync<ExpenseRequestDto?>(
            async attemptCancellationToken =>
            {
                var request = await repository.FindVisibleAsync(
                    id,
                    actorExternalId,
                    includeAll,
                    attemptCancellationToken);
                if (request is null)
                {
                    return null;
                }

                var projections = await budgetRepository.ListRequestProjectionsAsync(
                    [request.Id],
                    attemptCancellationToken);
                return await ToCurrentDtoAsync(request, projections, attemptCancellationToken);
            },
            cancellationToken);
    }

    public async Task<ExpenseRequestDetailDto?> GetDetailAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        var includeAll = currentActor.HasFullBusinessDataAccess;
        return await transactionRunner.ExecuteAsync<ExpenseRequestDetailDto?>(
            async attemptCancellationToken =>
            {
                var request = await repository.FindVisibleAsync(
                    id,
                    actorExternalId,
                    includeAll,
                    attemptCancellationToken);
                if (request is null)
                {
                    return null;
                }

                var projections = await budgetRepository.ListRequestProjectionsAsync(
                    [request.Id],
                    attemptCancellationToken);
                var canEdit = false;
                // Broad visibility is not a read-only business profile. A
                // financial director can still edit their own draft; the
                // technical administrator remains read-only and a readable
                // foreign card still needs explicit substitution authority.
                if (!currentActor.IsBmsAdministrator
                    && currentActor.ActiveRoleCode != Bms.Domain.Workflow.ExpenseRequestWorkflowCodes.AuditorRoleCode
                    && request.Status.IsEditableDraft())
                {
                    var authority = string.Equals(
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
                                attemptCancellationToken);
                    canEdit = authority.IsAuthorized
                        && await CanBudgetOwnerEditExistingAsync(
                            authority.InitiatorExternalId,
                            request.DefaultCostCenterId,
                            request.Lines.Select(line => line.CostCenterId),
                            attemptCancellationToken);
                }

                return new ExpenseRequestDetailDto(
                    await ToCurrentDtoAsync(request, projections, attemptCancellationToken),
                    new ExpenseRequestEditAuthorityDto(canEdit));
            },
            cancellationToken);
    }

    /// <summary>Captures optional display columns with the same visible page,
    /// count and budget. Ledger failures never return plausible unpaid values.</summary>
    public async Task<ExpenseRequestPageDto> ListAsync(
        int skip,
        int take,
        ExpenseRequestFilter filter,
        bool includeBusinessColumns,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        filter.Validate();
        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 200);

        var includeAll = currentActor.HasFullBusinessDataAccess;
        Exception? optionalReadFailure = null;
        try
        {
            return await transactionRunner.ExecuteAsync(
                async attemptCancellationToken =>
                {
                    // A retry is a fresh business attempt. Do not let a failure
                    // attributed to the optional reader in an earlier attempt
                    // relabel a later core-list or budget failure.
                    optionalReadFailure = null;
                    // A technical profile has no actionable inbox. Do not
                    // confuse this explicit profile with the legacy full-read
                    // hint also used by a financial director.
                    if (filter.OnlyMyTasks && currentActor.IsBmsAdministrator)
                    {
                        return new ExpenseRequestPageDto([], skip, take, 0)
                        { BusinessColumnsIncluded = includeBusinessColumns ? true : null };
                    }
                    var budgetIndicatorsUnavailable = false;
                    IReadOnlyList<ExpenseRequest> items;
                    try
                    {
                        items = await repository.ListVisibleAsync(
                        actorExternalId,
                        includeAll,
                        filter,
                        skip,
                        take,
                        attemptCancellationToken);
                    }
                    catch (CashFlowActualsNotAvailableException) when (filter.SortBy == "budget")
                    {
                        // Read-only degradation: no stale financial classification,
                        // and no page-local sorting masquerading as global sorting.
                        budgetIndicatorsUnavailable = true;
                        items = await repository.ListVisibleAsync(actorExternalId, includeAll,
                            filter with { SortBy = null, SortDirection = null }, skip, take,
                            attemptCancellationToken);
                    }
                    var total = await repository.CountVisibleAsync(
                        actorExternalId,
                        includeAll,
                        filter,
                        attemptCancellationToken);

                    var itemIds = items.Select(item => item.Id).ToArray();
                    var openableIds = await repository.ListOpenableIdsAsync(
                        actorExternalId,
                        includeAll,
                        itemIds,
                        attemptCancellationToken);
                    var openableIdSet = openableIds.ToHashSet();
                    if (openableIdSet.Count != openableIds.Count
                        || openableIdSet.Any(id => !itemIds.Contains(id)))
                    {
                        throw new InvalidOperationException(
                            "Expense-request list interaction scope is inconsistent.");
                    }

                    // The active register is authoritative. Avoid building a
                    // second full register frame through unused legacy positions.
                    var registerDecisions343 = await budgetRepository.ReadFifoDecisions343Async(itemIds, attemptCancellationToken);
                    var currentIndicatorItems = items.Where(item => registerDecisions343 is null
                        && item.Status is not (ExpenseRequestStatus.Paid or ExpenseRequestStatus.Closed)).ToArray();
                    IReadOnlyList<BudgetRequestProjection> projections = registerDecisions343 is null
                        ? await budgetRepository.ListRequestProjectionsAsync(
                            currentIndicatorItems.Select(item => item.Id).ToArray(),
                            attemptCancellationToken)
                        : [];
                    var indicatorScopes = currentIndicatorItems.Select(ExpenseRequestBudgetIndicatorScope.FromRequest).ToArray();
                    var budgetKeys = projections.Select(projection => projection.Key)
                        .Concat(indicatorScopes.SelectMany(scope => scope.BudgetKeys ?? []))
                        .Distinct().ToArray();
                    IReadOnlyList<BudgetPosition> positions = [];
                    if (!budgetIndicatorsUnavailable && budgetKeys.Length > 0)
                    {
                        try
                        {
                            positions = await budgetRepository.ListPositionsAsync(
                                budgetKeys, attemptCancellationToken);
                        }
                        catch (CashFlowActualsNotAvailableException)
                        {
                            // Only this typed source-unavailability failure is optional
                            // on the list. Writes and financial checks remain fail-closed.
                            budgetIndicatorsUnavailable = true;
                        }
                    }
                    var indicators = budgetIndicatorsUnavailable
                        ? items.ToDictionary(item => item.Id, _ => (bool?)null)
                        : ExpenseRequestBudgetIndicatorEvaluator.Evaluate(
                            indicatorScopes, projections, positions);
                    var cancelledIds = items.Where(item => item.Status is ExpenseRequestStatus.Cancelled or ExpenseRequestStatus.Paid or ExpenseRequestStatus.Closed)
                        .Select(item => item.Id).ToArray();
                    IReadOnlyDictionary<Guid, bool> cancelledIndicators = registerDecisions343 is null
                        ? await budgetRepository.ReadCancelledSubmissionBudgetIndicatorsAsync(
                            cancelledIds, attemptCancellationToken)
                        : new Dictionary<Guid, bool>();
                    if (cancelledIndicators.Keys.Any(id => !cancelledIds.Contains(id)))
                        throw new InvalidOperationException("Cancellation budget scope is inconsistent.");

                    IReadOnlyDictionary<Guid, ExpenseRequestListBusinessColumnsDto> businessColumns =
                        new Dictionary<Guid, ExpenseRequestListBusinessColumnsDto>();
                    if (includeBusinessColumns && openableIdSet.Count > 0)
                    {
                        try
                        {
                            businessColumns = await listBusinessColumnsReader.ReadAsync(
                                items.Where(item => openableIdSet.Contains(item.Id)).ToArray(), attemptCancellationToken);
                        }
                        catch (Exception failure) when (ConvertsToBusinessColumnsUnavailable(failure))
                        {
                            // Preserve the provider exception while the execution
                            // strategy is deciding whether to retry it. The outer
                            // boundary converts only the final exception raised by
                            // this reader, after retries have been exhausted.
                            optionalReadFailure = failure;
                            throw;
                        }
                        if (businessColumns.Count != openableIdSet.Count ||
                            businessColumns.Any(pair => !openableIdSet.Contains(pair.Key) || pair.Value is null))
                            throw new ExpenseRequestListBusinessColumnsUnavailableException();
                    }

                    var activeRoutes = items.Where(item => item.Status == ExpenseRequestStatus.InApproval)
                        .ToDictionary(item => item.Id, item => item.ActiveApprovalRouteSnapshotNumber
                            ?? throw new DomainRuleViolationException("Active route is missing."));
                    var snapshots = await approvalRouteSnapshotStore.FindActiveAsync(activeRoutes, attemptCancellationToken);
                    if (snapshots.Count != activeRoutes.Count || snapshots.Any(pair => pair.Value is null || !activeRoutes.TryGetValue(pair.Key, out var number)
                        || pair.Value.ExpenseRequestId != pair.Key || pair.Value.SnapshotNumber != number))
                        throw new DomainRuleViolationException("Active route is inconsistent.");
                    var requestDates = await repository.ReadRequestDatesAsync(items.Select(item => item.Id).ToArray(), attemptCancellationToken);
                    if (requestDates.Keys.Any(id => !items.Any(item => item.Id == id)))
                        throw new InvalidOperationException("Request date scope is inconsistent.");
                    return new ExpenseRequestPageDto(
                        items.Select(item => ExpenseRequestListItemDto.FromDomain(
                            item,
                            registerDecisions343 is not null
                                ? registerDecisions343.TryGetValue(item.Id,out var decision343)?decision343.IsOverBudget:(bool?)null
                                : (!budgetIndicatorsUnavailable || item.Status is ExpenseRequestStatus.Paid or ExpenseRequestStatus.Closed)
                                && cancelledIndicators.TryGetValue(item.Id, out var historicalOverBudget)
                                ? historicalOverBudget : item.Status is ExpenseRequestStatus.Paid or ExpenseRequestStatus.Closed
                                    ? null : indicators[item.Id],
                            openableIdSet.Contains(item.Id), snapshots.GetValueOrDefault(item.Id)) with
                        { BusinessColumns = businessColumns.GetValueOrDefault(item.Id),
                          RequestDateUtc = requestDates.TryGetValue(item.Id, out var requestDate) ? requestDate : item.CreatedAtUtc }).ToArray(),
                        skip,
                        take,
                        total)
                    {
                        BusinessColumnsIncluded = includeBusinessColumns ? true : null,
                        BudgetIndicatorsUnavailable = budgetIndicatorsUnavailable ? true : null
                    };
                },
                cancellationToken);
        }
        catch (Exception failure) when (ReferenceEquals(failure, optionalReadFailure))
        {
            throw new ExpenseRequestListBusinessColumnsUnavailableException(failure);
        }
    }

    /// <summary>
    /// Only failures raised by the optional business-columns reader may be
    /// converted to its named, redacted unavailability error after the
    /// transaction strategy has exhausted any provider retry. Core list,
    /// budget and interaction-scope failures keep their own identity.
    /// </summary>
    private static bool ConvertsToBusinessColumnsUnavailable(Exception exception) =>
        exception is not OperationCanceledException
            and not ActorNotAuthenticatedException
            and not ActorForbiddenException
            and not ExpenseRequestListBusinessColumnsUnavailableException
        && !ProcessFatalExceptionClassifier.IsFatal(exception);

    /// <summary>Reads the original list contract through the same clamped capture,
    /// without performing optional ledger queries.</summary>
    public Task<ExpenseRequestPageDto> ListAsync(
        int skip, int take, ExpenseRequestFilter filter, CancellationToken cancellationToken) =>
        ListAsync(skip, take, filter, false, cancellationToken);
}
