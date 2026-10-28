using System.Collections.ObjectModel;
using Bms.Domain.ExpenseRequests;

namespace Bms.Domain.Budgeting;

public static class ExpenseRequestBudgetIndicatorEvaluator
{
    public static IReadOnlyDictionary<Guid, bool?> Evaluate(
        IReadOnlyCollection<ExpenseRequestBudgetIndicatorScope> requestScopes,
        IReadOnlyCollection<BudgetRequestProjection> projections,
        IReadOnlyCollection<BudgetPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(requestScopes);
        ArgumentNullException.ThrowIfNull(projections);
        ArgumentNullException.ThrowIfNull(positions);

        var scopes = requestScopes.ToArray();
        var requestIds = scopes.Select(scope => scope.ExpenseRequestId).ToArray();
        if (requestIds.Any(id => id == Guid.Empty) ||
            requestIds.Distinct().Count() != requestIds.Length)
        {
            throw new InvalidOperationException(
                "Expense-request budget indicators require distinct non-empty request ids.");
        }

        var expectedLineIdsByRequest = new Dictionary<Guid, HashSet<Guid>>(scopes.Length);
        foreach (var scope in scopes)
        {
            if (scope.ExpenseRequestLineIds is null)
            {
                throw new InvalidOperationException(
                    "Expense-request budget indicator line identities are required.");
            }

            var lineIds = scope.ExpenseRequestLineIds.ToArray();
            if (lineIds.Any(id => id == Guid.Empty) ||
                lineIds.Distinct().Count() != lineIds.Length)
            {
                throw new InvalidOperationException(
                    "Expense-request budget indicators require distinct non-empty line ids.");
            }

            expectedLineIdsByRequest.Add(scope.ExpenseRequestId, lineIds.ToHashSet());
        }

        var requestIdSet = requestIds.ToHashSet();
        if (projections.Any(projection => !requestIdSet.Contains(projection.ExpenseRequestId)))
        {
            throw new InvalidOperationException(
                "A budget projection does not belong to the requested indicator set.");
        }

        if (projections
            .GroupBy(projection => (projection.ExpenseRequestId, projection.ExpenseRequestLineId))
            .Any(group => group.Count() != 1))
        {
            throw new InvalidOperationException(
                "An expense-request line has duplicate current budget projections.");
        }

        if (positions.GroupBy(position => position.Key).Any(group => group.Count() != 1))
        {
            throw new InvalidOperationException(
                "A budget key has duplicate current positions.");
        }

        var positionsByKey = positions.ToDictionary(position => position.Key);
        var projectionsByRequest = projections
            .GroupBy(projection => projection.ExpenseRequestId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var indicators = new Dictionary<Guid, bool?>(requestIds.Length);
        var scopesById = scopes.ToDictionary(scope => scope.ExpenseRequestId);

        foreach (var requestId in requestIds)
        {
            var expectedLineIds = expectedLineIdsByRequest[requestId];
            // A released/missing reservation is not evidence that an article
            // has a budget. Read the actual line keys even on cancelled or
            // incomplete requests. A failed repository read still throws;
            // only a successfully read absent/unallocated limit means over.
            if (scopesById[requestId].BudgetKeys?.Any(key =>
                !positionsByKey.TryGetValue(key, out var position) ||
                position.LimitAmountKzt is null or <= 0m) == true)
            {
                indicators.Add(requestId, true);
                continue;
            }
            projectionsByRequest.TryGetValue(requestId, out var requestProjections);
            requestProjections ??= [];
            var hasUnexpectedProjection = requestProjections.Any(projection =>
                !expectedLineIds.Contains(projection.ExpenseRequestLineId));
            var currentProjections = requestProjections
                .Where(projection => expectedLineIds.Contains(projection.ExpenseRequestLineId))
                .ToArray();
            if (currentProjections.Length == 0)
            {
                indicators.Add(requestId, null);
                continue;
            }

            var hasUnknownPosition = hasUnexpectedProjection ||
                currentProjections.Length != expectedLineIds.Count;
            var isOverBudget = false;
            foreach (var key in currentProjections.Select(projection => projection.Key).Distinct())
            {
                if (!positionsByKey.TryGetValue(key, out var position) ||
                    position.LimitAmountKzt is null or <= 0m)
                {
                    isOverBudget = true;
                    break;
                }

                if (!position.AvailableAmountKzt.HasValue)
                {
                    hasUnknownPosition = true;
                    continue;
                }

                if (position.AvailableAmountKzt.Value < 0m)
                {
                    isOverBudget = true;
                    break;
                }
            }

            indicators.Add(
                requestId,
                isOverBudget ? true : hasUnknownPosition ? null : false);
        }

        return new ReadOnlyDictionary<Guid, bool?>(indicators);
    }
}

public sealed record ExpenseRequestBudgetIndicatorScope(
    Guid ExpenseRequestId,
    IReadOnlyCollection<Guid> ExpenseRequestLineIds,
    IReadOnlyCollection<BudgetKey>? BudgetKeys = null)
{
    public static ExpenseRequestBudgetIndicatorScope FromRequest(ExpenseRequest request) => new(
        request.Id,
        request.Lines.Select(line => line.Id).ToArray(),
        request.EffectiveBudgetMonth is { } date
            ? request.Lines.Where(line => line.CostCenterId.HasValue && line.CostCenterId != Guid.Empty
                    && line.CashFlowItemId.HasValue && line.CashFlowItemId != Guid.Empty)
                .Select(line => BudgetKey.Create(ExpenseBudgetDimensions372.CostCenter(request, line), line.CashFlowItemId.GetValueOrDefault(), date))
                .Distinct().ToArray()
            : []);
}
