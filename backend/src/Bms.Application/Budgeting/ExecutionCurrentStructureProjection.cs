using Bms.Application.Abstractions;
using Bms.Application.Authorization;

namespace Bms.Application.Budgeting;

/// <summary>Display-only regrouping by immutable article UUID and month. No budget or reservation writer uses this.</summary>
public static class ExecutionCurrentStructureProjection
{
    public static BudgetExecutionExportSlice Project(IReadOnlyList<BudgetExecutionExportRow> rows,
        IReadOnlyList<BudgetExecutionExportRow> planRows, IReadOnlyList<ExecutionFactArticle> mappings,
        BusinessDataReadScope scope, Guid? cfo)
    {
        if (rows.Count > BudgetExecutionExportPolicy.MaximumRows || planRows.Count > BudgetExecutionExportPolicy.MaximumRows)
            throw new BudgetExecutionExportTooLargeException(Math.Max(rows.Count, planRows.Count), BudgetExecutionExportPolicy.MaximumRows);
        var map = mappings.ToDictionary(m => m.CashFlowItemId);
        var planKeys = planRows.Select(r => r.Key).ToHashSet();
        // Both the original monetary key and its current display destination must be authorized.
        var visible = rows.Where(r => ExecutionActualsProjection.VisibleCenter(scope, r.CostCenterId))
            .Select(r => (Row: r, Cfo: map.TryGetValue(r.CashFlowItemId, out var m) ? m.CostCenterId ?? Guid.Empty : r.CostCenterId))
            .Where(r => ExecutionActualsProjection.VisibleCenter(scope, r.Cfo) && (!cfo.HasValue || r.Cfo == cfo)).ToArray();
        var result = visible.GroupBy(r => (r.Cfo, r.Row.CashFlowItemId, r.Row.BudgetMonth))
            .Select(group =>
            {
                var first = group.First().Row;
                map.TryGetValue(first.CashFlowItemId, out var current);
                var limit = group.Sum(r => r.Row.LimitAmountKzt ?? 0m);
                var reserved = group.Sum(r => r.Row.ReservedAmountKzt);
                var paid = group.Sum(r => r.Row.PaidAmountKzt);
                if (!Money(limit) || !Money(reserved) || !Money(paid)) throw new CashFlowActualsSourceInvalidException();
                return first with
                {
                    CostCenterId = group.Key.Cfo,
                    IsCostCenterUnassigned = group.Key.Cfo == Guid.Empty,
                    CostCenterCode = current is null ? first.CostCenterCode : current.CostCenterCode,
                    CostCenterName = current is null ? first.CostCenterName : current.CostCenterName,
                    CashFlowItemCode = current?.ArticleCode ?? first.CashFlowItemCode,
                    CashFlowItemName = current?.ArticleName ?? first.CashFlowItemName,
                    LimitAmountKzt = limit, ReservedAmountKzt = reserved, PaidAmountKzt = paid,
                    PlanCostCenters = group.Where(r => r.Row.CostCenterId != Guid.Empty
                        && (planKeys.Contains(r.Row.Key) || r.Row.ReservedAmountKzt > 0m))
                        .Select(r => new ExecutionPlanCostCenterDto(r.Row.CostCenterId, r.Row.CostCenterCode,
                            r.Row.CostCenterName, r.Row.ReservedAmountKzt)).OrderBy(r => r.Code).ThenBy(r => r.CostCenterId).ToArray()
                };
            }).OrderByDescending(r => r.BudgetMonth).ThenBy(r => r.CostCenterId).ThenBy(r => r.CashFlowItemId).ToArray();
        if (!Money(result.Sum(r => r.LimitAmountKzt ?? 0m))
            || !Money(result.Sum(r => r.ReservedAmountKzt)) || !Money(result.Sum(r => r.PaidAmountKzt)))
            throw new CashFlowActualsSourceInvalidException();
        return new(result, result.Length, result.Length, 0, result.Sum(r => r.LimitAmountKzt ?? 0m),
            result.Sum(r => r.ReservedAmountKzt), result.Sum(r => r.PaidAmountKzt))
        { ReconciliationRows = visible.Select(r => r.Row).ToArray() };
    }
    private static bool Money(decimal value) => value >= 0m && value <= CashFlowActualsSource.MaximumSafeMoney && decimal.Round(value, 2) == value;
}
