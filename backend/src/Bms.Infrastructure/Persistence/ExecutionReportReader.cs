using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Budgeting;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class ExecutionReportReader(BmsDbContext db, IBudgetRepository budgets,
    IDirectDdsReplicaReader factReader) : IExecutionReportReader
{
    public async Task<BudgetPositionSlice> ReadCurrentSliceAsync(Guid? cfo, Guid? article, DateOnly? month,
        int skip, int take, BusinessDataReadScope scope, CancellationToken token, Guid? expectedFactSnapshotId = null)
        => Slice(await ReadCoreAsync(cfo, article, month, scope, token, expectedFactSnapshotId, true), skip, take);

    public Task<BudgetExecutionExportSlice> ReadCurrentExportAsync(Guid? cfo, Guid? article, DateOnly? month,
        BusinessDataReadScope scope, CancellationToken token, Guid? expectedFactSnapshotId = null)
        => ReadCoreAsync(cfo, article, month, scope, token, expectedFactSnapshotId, true);

    public async Task<BudgetPositionSlice> ReadSliceAsync(Guid? cfo, Guid? article, DateOnly? month,
        int skip, int take, BusinessDataReadScope scope, CancellationToken token, Guid? expectedFactSnapshotId = null)
    {
        var full = await ReadExportAsync(cfo, article, month, scope, token, expectedFactSnapshotId);
        return Slice(full, skip, take);
    }

    private static BudgetPositionSlice Slice(BudgetExecutionExportSlice full, int skip, int take) =>
        new(full.Rows.Skip(skip).Take(take).Select(r => new BudgetPositionSnapshot(r.CostCenterId,
            r.CashFlowItemId, r.BudgetMonth, r.LimitAmountKzt, r.ReservedAmountKzt, r.PaidAmountKzt)
            { PlanCostCenters = r.PlanCostCenters }).ToArray(),
            full.Total, full.ConfiguredLimitCount, full.UnconfiguredLimitCount, full.ConfiguredLimitAmountKzt,
            full.ReservedAmountKzt, full.PaidAmountKzt) { FactSource = full.FactSource };

    public async Task<BudgetExecutionExportSlice> ReadExportAsync(Guid? cfo, Guid? article, DateOnly? month,
        BusinessDataReadScope scope, CancellationToken token, Guid? expectedFactSnapshotId = null)
        => await ReadCoreAsync(cfo, article, month, scope, token, expectedFactSnapshotId, false);

    private async Task<BudgetExecutionExportSlice> ReadCoreAsync(Guid? cfo, Guid? article, DateOnly? month,
        BusinessDataReadScope scope, CancellationToken token, Guid? expectedFactSnapshotId, bool currentStructure)
    {
        if (month is {} selectedMonth && !AccountingPeriod467.IsMonth(selectedMonth)) throw new CashFlowActualsNotAvailableException();
        if (expectedFactSnapshotId == Guid.Empty) throw new ExecutionFactVersionConflictException();
        var fact = await factReader.ReadAsync(token) ?? throw new CashFlowActualsNotAvailableException();
        fact.Validate();
        if (expectedFactSnapshotId.HasValue && expectedFactSnapshotId != fact.Id)
            throw new ExecutionFactVersionConflictException();
        // The register already contains accepted cash, its original-request-month
        // attribution and only submitted reservations. Never fall back to the
        // legacy draft cache or apply the request-admission freshness gate to a
        // dated, read-only execution report.
        var fromRegister = db.BudgetRegisterEnabled343;
        var original = fromRegister
            ? await new BudgetExecutionReportCapture386(db).ReadAsync(currentStructure ? null : cfo, article, month, scope, fact, token)
            : await budgets.ReadExecutionExportAsync(currentStructure ? null : cfo, article, month, scope, token);
        if (original.Total != original.Rows.Count) throw new BudgetExecutionExportIncompleteException();
        if(original.IncludesSourceCashConsumption && original.FactSource?.SnapshotId!=fact.Id)
            throw new ExecutionFactVersionConflictException();
        var factIds = fact.Articles.Select(a => a.CashFlowItemId).ToHashSet();
        var ids = factIds.Concat(currentStructure ? original.Rows.Select(r => r.CashFlowItemId) : []).Distinct().ToArray();
        // UUID/FK join only. Never infer CFO from a name or from budget membership.
        var mappings = await (from item in db.CashFlowItems.AsNoTracking()
            join assigned in db.CostCenters.AsNoTracking() on item.CostCenterId equals (Guid?)assigned.Id into centers
            from center in centers.DefaultIfEmpty()
            where ids.Contains(item.Id) && !item.IsGroup
                && (item.CostCenterId == null || (center != null && !center.IsGroup))
            select new ExecutionFactArticle(item.Id, center == null ? null : (Guid?)center.Id,
                center == null ? null : center.Code, center == null ? null : center.Name, item.Code, item.Name))
            .ToArrayAsync(token);
        if (!factIds.IsSubsetOf(mappings.Select(m => m.CashFlowItemId)))
            throw new CashFlowActualsSourceInvalidException();
        // Assigned facts are already posted once by the register. Only the
        // report-only unassigned bucket comes directly from the same capture.
        var unassigned = mappings.Where(m => m.CostCenterId is null).Select(m => m.CashFlowItemId).ToHashSet();
        var rows = ExecutionActualsProjection.Overlay(original.Rows, fact.Cells
            .Where(c => !fromRegister || unassigned.Contains(c.CashFlowItemId)).Select(c =>
            new ExecutionFactCell(c.CashFlowItemId, c.BudgetMonth, c.PaymentsAmountKzt)).ToArray(), mappings, scope,
            currentStructure ? null : cfo, article, month, fromRegister || original.IncludesSourceCashConsumption);
        token.ThrowIfCancellationRequested();
        var source = new ExecutionFactSourceDto(fact.Id, fact.SourceGeneration, fact.GrossHash, fact.CapturedAtUtc, fact.CutoffExclusiveLocal, month?.Year ?? fact.Months.Max(m => m.Year));
        if (currentStructure)
            return ExecutionCurrentStructureProjection.Project(rows, original.Rows, mappings, scope, cfo) with { FactSource = source };
        return new(rows, rows.Count, rows.Count, 0, rows.Sum(r => r.LimitAmountKzt ?? 0m),
            rows.Sum(r => r.ReservedAmountKzt), rows.Sum(r => r.PaidAmountKzt))
        { FactSource = source };
    }
}
