using System.Collections.ObjectModel;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;

namespace Bms.Application.Budgeting;

internal enum BudgetLimitImportOperation
{
    Insert = 1,
    Update = 2,
    Clear = 3,
    Unchanged = 4
}

/// <summary>
/// One exact patch row supplied by a future controlled budget-limit source.
/// Absence from a batch never means delete; a null limit is an explicit clear.
/// </summary>
internal sealed record BudgetLimitImportItem
{
    private BudgetLimitImportItem(BudgetKey key, decimal? limitAmountKzt)
    {
        Key = key;
        LimitAmountKzt = limitAmountKzt;
    }

    internal BudgetKey Key { get; }

    internal decimal? LimitAmountKzt { get; }

    internal static BudgetLimitImportItem Create(
        Guid costCenterId,
        Guid cashFlowItemId,
        DateOnly budgetMonth,
        decimal? limitAmountKzt)
    {
        if (budgetMonth.Day != 1)
        {
            throw new DomainRuleViolationException(
                "A budget-limit import month must be the first day of its month.");
        }

        MoneyAmountPolicy.EnsureNonNegative(limitAmountKzt, "Budget import limit");
        return new BudgetLimitImportItem(
            BudgetKey.Create(costCenterId, cashFlowItemId, budgetMonth),
            limitAmountKzt);
    }
}

internal sealed class BudgetLimitImportBatch
{
    private BudgetLimitImportBatch(IReadOnlyList<BudgetLimitImportItem> items)
    {
        Items = items;
    }

    internal IReadOnlyList<BudgetLimitImportItem> Items { get; }

    internal static BudgetLimitImportBatch Create(IEnumerable<BudgetLimitImportItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var snapshot = items.ToArray();
        if (snapshot.Any(item => item is null))
        {
            throw new ArgumentException("Budget-limit import rows cannot be null.", nameof(items));
        }

        if (snapshot.Select(item => item.Key).Distinct().Count() != snapshot.Length)
        {
            throw new DomainRuleViolationException(
                "A budget-limit import batch cannot contain duplicate budget keys.");
        }

        return new BudgetLimitImportBatch(
            new ReadOnlyCollection<BudgetLimitImportItem>(Order(snapshot)));
    }

    private static BudgetLimitImportItem[] Order(IEnumerable<BudgetLimitImportItem> items) => items
        .OrderBy(item => item.Key.CostCenterId.ToString("N"), StringComparer.Ordinal)
        .ThenBy(item => item.Key.CashFlowItemId.ToString("N"), StringComparer.Ordinal)
        .ThenBy(item => item.Key.BudgetMonth)
        .ToArray();
}

internal sealed record BudgetLimitPositionSnapshot
{
    private BudgetLimitPositionSnapshot(
        BudgetKey key,
        decimal? limitAmountKzt,
        decimal reservedAmountKzt,
        decimal paidAmountKzt,
        Guid concurrencyStamp)
    {
        Key = key;
        LimitAmountKzt = limitAmountKzt;
        ReservedAmountKzt = reservedAmountKzt;
        PaidAmountKzt = paidAmountKzt;
        ConcurrencyStamp = concurrencyStamp;
    }

    internal BudgetKey Key { get; }

    internal decimal? LimitAmountKzt { get; }

    internal decimal ReservedAmountKzt { get; }

    internal decimal PaidAmountKzt { get; }

    internal Guid ConcurrencyStamp { get; }

    internal static BudgetLimitPositionSnapshot Create(
        BudgetKey key,
        decimal? limitAmountKzt,
        decimal reservedAmountKzt,
        decimal paidAmountKzt,
        Guid concurrencyStamp)
    {
        _ = BudgetKey.Create(key.CostCenterId, key.CashFlowItemId, key.BudgetMonth);
        if (key.BudgetMonth.Day != 1)
        {
            throw new DomainRuleViolationException(
                "A stored budget position month must be the first day of its month.");
        }

        MoneyAmountPolicy.EnsureNonNegative(limitAmountKzt, "Stored budget limit");
        MoneyAmountPolicy.EnsureNonNegative(reservedAmountKzt, "Stored budget reserve");
        MoneyAmountPolicy.EnsureNonNegative(paidAmountKzt, "Stored budget paid amount");
        if (concurrencyStamp == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "A stored budget position must have a concurrency stamp.");
        }

        return new BudgetLimitPositionSnapshot(
            key,
            limitAmountKzt,
            reservedAmountKzt,
            paidAmountKzt,
            concurrencyStamp);
    }
}

internal sealed class BudgetLimitImportAssignment
{
    internal BudgetLimitImportAssignment(
        BudgetLimitImportOperation operation,
        BudgetLimitImportItem item,
        BudgetLimitPositionSnapshot? current)
    {
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        Operation = operation;
        Item = item ?? throw new ArgumentNullException(nameof(item));
        Current = current;

        var validShape = operation switch
        {
            BudgetLimitImportOperation.Insert =>
                current is null && item.LimitAmountKzt.HasValue,
            BudgetLimitImportOperation.Update =>
                current is not null && item.LimitAmountKzt.HasValue,
            BudgetLimitImportOperation.Clear =>
                current is not null && !item.LimitAmountKzt.HasValue,
            BudgetLimitImportOperation.Unchanged =>
                current?.LimitAmountKzt == item.LimitAmountKzt,
            _ => false
        };
        if (!validShape)
        {
            throw new ArgumentException(
                "The budget-limit assignment operation does not match its current and desired state.",
                nameof(operation));
        }
    }

    internal BudgetLimitImportOperation Operation { get; }

    internal BudgetLimitImportItem Item { get; }

    internal BudgetLimitPositionSnapshot? Current { get; }
}

internal sealed class BudgetLimitImportPlan
{
    internal BudgetLimitImportPlan(IEnumerable<BudgetLimitImportAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        var snapshot = assignments.ToArray();
        if (snapshot.Any(assignment => assignment is null))
        {
            throw new ArgumentException("Budget-limit assignments cannot be null.", nameof(assignments));
        }

        Assignments = new ReadOnlyCollection<BudgetLimitImportAssignment>(snapshot);
    }

    internal IReadOnlyList<BudgetLimitImportAssignment> Assignments { get; }

    internal int InsertCount => Count(BudgetLimitImportOperation.Insert);

    internal int UpdateCount => Count(BudgetLimitImportOperation.Update);

    internal int ClearCount => Count(BudgetLimitImportOperation.Clear);

    internal int UnchangedCount => Count(BudgetLimitImportOperation.Unchanged);

    internal int ChangeCount => InsertCount + UpdateCount + ClearCount;

    private int Count(BudgetLimitImportOperation operation) =>
        Assignments.Count(assignment => assignment.Operation == operation);
}

internal static class BudgetLimitImportPlanBuilder
{
    internal static BudgetLimitImportPlan Build(
        BudgetLimitImportBatch batch,
        IEnumerable<BudgetLimitPositionSnapshot> currentPositions)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(currentPositions);

        var current = currentPositions.ToArray();
        if (current.Any(position => position is null))
        {
            throw new ArgumentException(
                "Current budget-position snapshots cannot be null.",
                nameof(currentPositions));
        }

        if (current.Select(position => position.Key).Distinct().Count() != current.Length)
        {
            throw new DomainRuleViolationException(
                "Current budget-position snapshots cannot contain duplicate keys.");
        }

        var currentByKey = current.ToDictionary(position => position.Key);
        var assignments = batch.Items.Select(item =>
        {
            currentByKey.TryGetValue(item.Key, out var existing);
            return new BudgetLimitImportAssignment(
                ResolveOperation(item, existing),
                item,
                existing);
        });

        return new BudgetLimitImportPlan(assignments);
    }

    private static BudgetLimitImportOperation ResolveOperation(
        BudgetLimitImportItem item,
        BudgetLimitPositionSnapshot? current)
    {
        if (current is null)
        {
            return item.LimitAmountKzt.HasValue
                ? BudgetLimitImportOperation.Insert
                : BudgetLimitImportOperation.Unchanged;
        }

        if (current.LimitAmountKzt == item.LimitAmountKzt)
        {
            return BudgetLimitImportOperation.Unchanged;
        }

        return item.LimitAmountKzt.HasValue
            ? BudgetLimitImportOperation.Update
            : BudgetLimitImportOperation.Clear;
    }
}
