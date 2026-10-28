using Bms.Application.Abstractions;
using Bms.Application.Authorization;

namespace Bms.Application.Budgeting;

public sealed record ExecutionFactSourceDto(Guid SnapshotId, string SourceGeneration,
    string GrossHash, DateTimeOffset CapturedAtUtc, DateOnly CutoffExclusiveLocal, int SourceYear);
public sealed class ExecutionFactVersionConflictException : Exception;

public sealed record ExecutionFactCell(Guid CashFlowItemId, DateOnly BudgetMonth, decimal PaymentsAmountKzt);
public sealed record ExecutionFactArticle(Guid CashFlowItemId, Guid? CostCenterId,
    string? CostCenterCode, string? CostCenterName, string ArticleCode, string ArticleName);

/// <summary>Pure, bounded overlay. Paid is eligible gross cash credit, not net cash flow.
/// Historical BMS paid values are replaced only in these immutable report rows.</summary>
public static class ExecutionActualsProjection
{
    public static IReadOnlyList<BudgetExecutionExportRow> Overlay(
        IReadOnlyList<BudgetExecutionExportRow> budgetRows,
        IReadOnlyList<ExecutionFactCell> cells, IReadOnlyList<ExecutionFactArticle> mappings,
        BusinessDataReadScope scope, Guid? costCenterId, Guid? articleId, DateOnly? month,
        bool includesSourceCashConsumption = false)
    {
        var map = new Dictionary<Guid, ExecutionFactArticle>();
        foreach (var item in mappings)
            if (item.CashFlowItemId == Guid.Empty || item.CostCenterId == Guid.Empty
                || !map.TryAdd(item.CashFlowItemId, item)) throw new CashFlowActualsSourceInvalidException();
        var payments = new Dictionary<(Guid Article, DateOnly Month), decimal>();
        foreach (var cell in cells)
            if (!map.ContainsKey(cell.CashFlowItemId) || !AccountingPeriod467.IsMonth(cell.BudgetMonth)
                || !Money(cell.PaymentsAmountKzt) || !payments.TryAdd((cell.CashFlowItemId, cell.BudgetMonth), cell.PaymentsAmountKzt))
                throw new CashFlowActualsSourceInvalidException();
        bool Visible(Guid cfo, Guid article, DateOnly period) => VisibleCenter(scope, cfo)
            && (!costCenterId.HasValue || cfo == costCenterId) && (!articleId.HasValue || article == articleId)
            && (!month.HasValue || period == month);
        var rows = new Dictionary<(Guid Cfo, Guid Article, DateOnly Month), BudgetExecutionExportRow>();
        foreach (var row in budgetRows)
        {
            if (!AccountingPeriod467.IsMonth(row.BudgetMonth) || row.CostCenterId == Guid.Empty
                || row.CashFlowItemId == Guid.Empty || !Visible(row.CostCenterId, row.CashFlowItemId, row.BudgetMonth)
                || !Money(row.LimitAmountKzt ?? 0m) || !Money(row.ReservedAmountKzt))
                throw new CashFlowActualsSourceInvalidException();
            var paid = !ExecutionPaidExclusionPolicy.IsExcluded(row.CashFlowItemId)
                && map.TryGetValue(row.CashFlowItemId, out var mapping) && mapping.CostCenterId == row.CostCenterId
                ? payments.GetValueOrDefault((row.CashFlowItemId, row.BudgetMonth)) : 0m;
            if(includesSourceCashConsumption)
            {
                if(!Money(row.PaidAmountKzt) || row.PaidAmountKzt<paid) throw new CashFlowActualsSourceInvalidException();
                paid=row.PaidAmountKzt;
            }
            if (!rows.TryAdd((row.CostCenterId, row.CashFlowItemId, row.BudgetMonth), row with { PaidAmountKzt = paid }))
                throw new CashFlowActualsSourceInvalidException();
        }
        foreach (var cell in cells.Where(c => c.PaymentsAmountKzt != 0m || map[c.CashFlowItemId].CostCenterId is null))
        {
            var mapping = map[cell.CashFlowItemId];
            if (ExecutionPaidExclusionPolicy.IsExcluded(cell.CashFlowItemId)
                || !Visible(mapping.CostCenterId ?? Guid.Empty, cell.CashFlowItemId, cell.BudgetMonth)) continue;
            var key = (mapping.CostCenterId ?? Guid.Empty, cell.CashFlowItemId, cell.BudgetMonth);
            rows.TryAdd(key, new(cell.BudgetMonth, mapping.CostCenterCode, mapping.CostCenterName,
                mapping.ArticleCode, mapping.ArticleName, 0m, 0m, cell.PaymentsAmountKzt)
            { CostCenterId = key.Item1, CashFlowItemId = cell.CashFlowItemId,
                IsCostCenterUnassigned = mapping.CostCenterId is null });
        }
        if (rows.Count > BudgetExecutionExportPolicy.MaximumRows)
            throw new BudgetExecutionExportTooLargeException(rows.Count, BudgetExecutionExportPolicy.MaximumRows);
        var result = rows.Values.OrderByDescending(r => r.BudgetMonth).ThenBy(r => r.CostCenterId).ThenBy(r => r.CashFlowItemId).ToArray();
        if (!Money(result.Sum(r => r.LimitAmountKzt ?? 0m)) || !Money(result.Sum(r => r.ReservedAmountKzt))
            || !Money(result.Sum(r => r.PaidAmountKzt))) throw new CashFlowActualsSourceInvalidException();
        return result;
    }
    // Empty is a report-only unassigned bucket, never a BudgetKey or write authority.
    internal static bool VisibleCenter(BusinessDataReadScope scope, Guid cfo) =>
        cfo == Guid.Empty ? scope.CanReadAll : scope.Contains(cfo);
    private static bool Money(decimal value) => value >= 0m && value <= CashFlowActualsSource.MaximumSafeMoney && decimal.Round(value, 2) == value;
}
