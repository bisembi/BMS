using Bms.Application.Abstractions;
using Bms.Application.ExpenseRequests;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class SqlServerAccrualSnapshotSourceResolver : IAccrualSnapshotSourceResolver
{
    private readonly Func<Guid[], CancellationToken, Task<AccrualCashFlowItemSettings[]>>
        _readCashFlowItemsAsync;
    private readonly Func<Guid[], CancellationToken, Task<Guid[]>> _readExpenseItemIdsAsync;

    public SqlServerAccrualSnapshotSourceResolver(BmsDbContext dbContext)
        : this(
            (referencedIds, cancellationToken) => dbContext.CashFlowItems
                .AsNoTracking()
                .Where(item => referencedIds.Contains(item.Id))
                .Select(item => new AccrualCashFlowItemSettings(
                    item.Id,
                    item.IsActive,
                    item.IsGroup,
                    item.FormsManagerialAccrual,
                    item.AccrualExpenseItemId))
                .ToArrayAsync(cancellationToken),
            (mappingIds, cancellationToken) => dbContext.ExpenseItems
                .AsNoTracking()
                .Where(item => mappingIds.Contains(item.Id))
                .Select(item => item.Id)
                .ToArrayAsync(cancellationToken))
    {
        ArgumentNullException.ThrowIfNull(dbContext);
    }

    internal SqlServerAccrualSnapshotSourceResolver(
        Func<Guid[], CancellationToken, Task<AccrualCashFlowItemSettings[]>>
            readCashFlowItemsAsync,
        Func<Guid[], CancellationToken, Task<Guid[]>> readExpenseItemIdsAsync)
    {
        _readCashFlowItemsAsync = readCashFlowItemsAsync ??
            throw new ArgumentNullException(nameof(readCashFlowItemsAsync));
        _readExpenseItemIdsAsync = readExpenseItemIdsAsync ??
            throw new ArgumentNullException(nameof(readExpenseItemIdsAsync));
    }

    public async Task<AccrualSnapshotSourceResolution> ResolveAsync(
        IReadOnlyList<AccrualSnapshotSourceLine> lines,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ValidateLineIds(lines);
        var materialLines = lines
            .Where(line => line.CashFlowItemId is { } id && id != Guid.Empty)
            .ToArray();
        var referencedIds = materialLines
            .Select(line => line.CashFlowItemId!.Value)
            .Distinct()
            .ToArray();

        var items = referencedIds.Length == 0
            ? []
            : await _readCashFlowItemsAsync(referencedIds, cancellationToken);
        var relevantExpenseItemIds = items
            .Where(item => item.IsActive &&
                !item.IsGroup &&
                item.FormsManagerialAccrual &&
                item.AccrualExpenseItemId is { } mappingId &&
                mappingId != Guid.Empty)
            .Select(item => item.AccrualExpenseItemId!.Value)
            .Distinct()
            .ToArray();
        var existingExpenseItemIds = relevantExpenseItemIds.Length == 0
            ? []
            : (await _readExpenseItemIdsAsync(
                relevantExpenseItemIds,
                cancellationToken)).ToHashSet();
        return AccrualSnapshotSourceEvaluator.Evaluate(
            lines,
            items,
            existingExpenseItemIds);
    }

    private static void ValidateLineIds(IReadOnlyList<AccrualSnapshotSourceLine> lines)
    {
        if (lines.Any(line => line.LineId == Guid.Empty) ||
            lines.Select(line => line.LineId).Distinct().Count() != lines.Count)
        {
            throw new InvalidOperationException(
                "Accrual snapshot source lines must have non-empty unique persisted ids.");
        }
    }
}
