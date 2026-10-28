using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Application.Authorization;
using Bms.Domain.Budgeting;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.Budgeting;

public sealed class BudgetPositionService(
    IBudgetRepository budgetRepository,
    IExpenseRequestRepository expenseRequestRepository,
    ICurrentActor currentActor,
    IBusinessTransactionRunner transactionRunner,
    IExecutionReportReader? reportReader = null)
{
    public async Task<BudgetPositionPageDto> ListAsync(
        Guid? expenseRequestId,
        Guid? costCenterId,
        Guid? cashFlowItemId,
        DateOnly? budgetMonth,
        int skip,
        int take,
        CancellationToken cancellationToken,
        bool completeRequestCapture = false,
        Guid? expectedRequestVersion = null,
        Guid? expectedFactSnapshotId = null,
        bool currentStructure = false)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (currentStructure && (expenseRequestId.HasValue || reportReader is null))
            throw new ArgumentException("Current structure is a report-only view.");
        if (expectedFactSnapshotId.HasValue && reportReader is null) throw new ExecutionFactVersionConflictException();
        if (completeRequestCapture && !expenseRequestId.HasValue)
            throw new ArgumentException("A complete capture requires an expense request.");
        if (expectedRequestVersion.HasValue && (!completeRequestCapture || expectedRequestVersion == Guid.Empty))
            throw new ArgumentException("A version constraint requires a complete request capture.");
        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 100);
        if (completeRequestCapture)
        {
            // The explicitly requested complete capture is still bounded;
            // ordinary page parameters cannot truncate a request's control.
            skip = 0;
            take = BudgetExecutionExportPolicy.MaximumRows;
        }

        if (expenseRequestId.HasValue)
        {
            if (expectedFactSnapshotId.HasValue) throw new ArgumentException("Fact constraints do not belong to request admission.");
            var includeAll = currentActor.HasFullBusinessDataAccess;
            cancellationToken.ThrowIfCancellationRequested();
            // Access, the request's keys and the positions behind them must
            // describe one state, including when a draft moves to another key.
            // Retry the whole capture; never retain keys from an earlier attempt.
            return await transactionRunner.ExecuteAsync(
                async attemptCancellationToken =>
                {
                    var request = await expenseRequestRepository.FindVisibleAsync(
                        expenseRequestId.Value,
                        actorExternalId,
                        includeAll,
                        attemptCancellationToken);
                    attemptCancellationToken.ThrowIfCancellationRequested();
                    if (request is null)
                    {
                        throw new ExpenseRequestNotFoundException();
                    }

                    var readScope = await currentActor.ResolveBusinessDataReadScopeAsync(attemptCancellationToken);
                    if (!readScope.Contains(request)) throw new ExpenseRequestNotFoundException();
                    var capturedVersion = request.ConcurrencyStamp;
                    if (expectedRequestVersion.HasValue && expectedRequestVersion.Value != capturedVersion)
                        throw new ExpenseRequestVersionConflictException();
                    if (request.PaymentOperation?.IsPayrollAdvance == true)
                        return new BudgetPositionPageDto([], 0, take, 0, request.Id, BuildTotals(0,0,0,0m,0m,0m))
                            { ExpenseRequestVersion = capturedVersion };
                    var projections = await budgetRepository.ListRequestProjectionsAsync(
                        [request.Id],
                        attemptCancellationToken);
                    attemptCancellationToken.ThrowIfCancellationRequested();
                    var keys = projections
                        .Select(projection => projection.Key)
                        .Concat(SavedSettledKeys(request))
                        .Distinct()
                        .Where(key => readScope.Contains(key.CostCenterId))
                        .Where(key => !costCenterId.HasValue || key.CostCenterId == costCenterId.Value)
                        .Where(key => !cashFlowItemId.HasValue || key.CashFlowItemId == cashFlowItemId.Value)
                        .Where(key => !budgetMonth.HasValue
                            || key.BudgetMonth == FirstDayOfMonth(budgetMonth.Value))
                        .OrderBy(key => key.CostCenterId)
                        .ThenBy(key => key.CashFlowItemId)
                        .ThenBy(key => key.BudgetMonth)
                        .ToArray();
                    if (completeRequestCapture && keys.Length > BudgetExecutionExportPolicy.MaximumRows)
                        throw new BudgetExecutionExportTooLargeException(keys.Length, BudgetExecutionExportPolicy.MaximumRows);
                    var positions = await budgetRepository.ListPositionsAsync(keys, attemptCancellationToken);
                    attemptCancellationToken.ThrowIfCancellationRequested();
                    var controls343=await budgetRepository.ReadRequestControl343Async(request.Id,attemptCancellationToken);
                    var page = positions.Skip(skip).Take(take).Select(p=>Map(p) with
                        {RequestControl343=controls343?.GetValueOrDefault(p.Key)}).ToArray();
                    return new BudgetPositionPageDto(
                        page,
                        skip,
                        completeRequestCapture ? Math.Max(positions.Count, 1) : take,
                        positions.Count,
                        request.Id,
                        Summarise(positions.Select(position => (
                            position.LimitAmountKzt,
                            position.ReservedAmountKzt,
                            position.PaidAmountKzt)))) { ExpenseRequestVersion = capturedVersion };
                },
                cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        DateOnly? normalizedMonth = budgetMonth.HasValue
            ? FirstDayOfMonth(budgetMonth.Value)
            : null;
        // One captured slice: the page, the count and the sums describe the same
        // filtered area rather than three reads that could disagree.
        var slice = await transactionRunner.ExecuteAsync(async token =>
        {
            var scope = await currentActor.ResolveBusinessDataReadScopeAsync(token);
            if (!scope.HasAny || (costCenterId.HasValue && !scope.Contains(costCenterId.Value)))
                throw new ActorForbiddenException();
            return reportReader is not null
                ? currentStructure
                    ? await reportReader.ReadCurrentSliceAsync(costCenterId, cashFlowItemId, normalizedMonth, skip, take, scope, token, expectedFactSnapshotId)
                    : await reportReader.ReadSliceAsync(costCenterId, cashFlowItemId, normalizedMonth, skip, take, scope, token, expectedFactSnapshotId)
                : await budgetRepository.ReadExecutionSliceAsync(costCenterId, cashFlowItemId, normalizedMonth, skip, take, scope, token);
        }, cancellationToken);
        return new BudgetPositionPageDto(
            slice.Page.Select(Map).ToArray(),
            skip,
            take,
            slice.Total,
            null,
            BuildTotals(
                slice.Total,
                slice.ConfiguredLimitCount,
                slice.UnconfiguredLimitCount,
                slice.ConfiguredLimitAmountKzt,
                slice.ReservedAmountKzt,
                slice.PaidAmountKzt)) { FactSource = slice.FactSource };
    }

    private static BudgetPositionDto Map(BudgetPosition position) =>
        Map(
            position.CostCenterId,
            position.CashFlowItemId,
            position.BudgetMonth,
            position.LimitAmountKzt,
            position.ReservedAmountKzt,
            position.PaidAmountKzt);

    private static BudgetPositionDto Map(BudgetPositionSnapshot position) =>
        Map(
            position.CostCenterId,
            position.CashFlowItemId,
            position.BudgetMonth,
            position.LimitAmountKzt,
            position.ReservedAmountKzt,
            position.PaidAmountKzt) with { PlanCostCenters = position.PlanCostCenters };

    /// <summary>
    /// The single definition of a published budget position. The reservation
    /// drill-down maps its position through this same method, so the two
    /// screens cannot come to describe one position differently. A missing
    /// legacy value is already the effective zero by the time a real reader
    /// reaches this boundary; normalize defensively for older adapters too.
    /// </summary>
    internal static BudgetPositionDto Map(
        Guid costCenterId,
        Guid cashFlowItemId,
        DateOnly budgetMonth,
        decimal? limitAmountKzt,
        decimal reservedAmountKzt,
        decimal paidAmountKzt)
    {
        var effectiveLimit = limitAmountKzt ?? 0m;
        var available = BudgetPosition.CalculateAvailableAmountKzt(
            effectiveLimit,
            reservedAmountKzt,
            paidAmountKzt) ?? 0m;
        return new BudgetPositionDto(
            costCenterId,
            cashFlowItemId,
            budgetMonth,
            effectiveLimit,
            reservedAmountKzt,
            paidAmountKzt,
            available,
            true,
            available < 0m);
    }

    /// <summary>
    /// Sums an area that is already in memory, in the same shape the captured
    /// slice returns from the server.
    /// </summary>
    private static BudgetPositionTotalsDto Summarise(
        IEnumerable<(decimal? Limit, decimal Reserved, decimal Paid)> positions)
    {
        var total = 0;
        var configured = 0;
        var configuredLimit = 0m;
        var reserved = 0m;
        var paid = 0m;
        foreach (var position in positions)
        {
            total++;
            if (position.Limit.HasValue)
            {
                configured++;
                configuredLimit += position.Limit.Value;
            }

            reserved += position.Reserved;
            paid += position.Paid;
        }

        return BuildTotals(total, configured, total - configured, configuredLimit, reserved, paid);
    }

    /// <summary>
    /// Reserved and paid are complete for the area. Effective limits are also
    /// complete: a position outside every approved version contributes the
    /// explicit zero required by the budget contract. Legacy adapters may still
    /// report their source count separately, but this application boundary
    /// never publishes an undefined total or an unconfigured wire row.
    /// </summary>
    internal static BudgetPositionTotalsDto BuildTotals(
        int totalPositions,
        int configuredLimitCount,
        int unconfiguredLimitCount,
        decimal configuredLimitAmountKzt,
        decimal reservedAmountKzt,
        decimal paidAmountKzt)
    {
        // Any legacy adapter that still reports an unconfigured row has already
        // lost the approved-version context. Treat that row as the contract's
        // explicit zero and keep the aggregate usable rather than reviving an
        // "undefined budget" state.
        var effectiveConfiguredCount = totalPositions;
        return new BudgetPositionTotalsDto(
            totalPositions,
            effectiveConfiguredCount,
            0,
            configuredLimitAmountKzt,
            reservedAmountKzt,
            paidAmountKzt,
            BudgetPosition.CalculateAvailableAmountKzt(
                configuredLimitAmountKzt,
                reservedAmountKzt,
                paidAmountKzt) ?? 0m);
    }

    private static DateOnly FirstDayOfMonth(DateOnly value) =>
        new(value.Year, value.Month, 1);

    private static IEnumerable<BudgetKey> SavedSettledKeys(ExpenseRequest request)
    {
        // Settling a line or rejecting/cancelling a request removes its reserve,
        // not its budget identity. Read existing positions for the saved lines;
        // never recreate a projection or conceal a missing unsettled reserve.
        if (request.Status is not (ExpenseRequestStatus.PartiallyPaid or ExpenseRequestStatus.Paid
            or ExpenseRequestStatus.Closed or ExpenseRequestStatus.Rejected or ExpenseRequestStatus.Cancelled)
            || request.PlannedPaymentDate is not { } paymentDate) yield break;

        foreach (var line in request.Lines)
            if (line.CostCenterId is { } cfo && cfo != Guid.Empty
                && line.CashFlowItemId is { } article && article != Guid.Empty)
                yield return BudgetKey.Create(ExpenseBudgetDimensions372.CostCenter(request, line), article, paymentDate);
    }
}
