using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Budgeting;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>Read-only execution projection of the posted register. The legacy
/// document cache also contains drafts and must not be used as an execution
/// balance. No posting, FIFO evaluation or admission freshness bypass here.</summary>
internal sealed class BudgetExecutionReportCapture386(BmsDbContext db)
{
    internal static void VerifyCapture(RegisterSnapshot343 state, DirectDdsSnapshot fact)
    {
        fact.Validate();
        if (!state.Enabled || state.CashSnapshotId is null)
            throw new CashFlowActualsNotAvailableException();
        if (state.CashSnapshotId != fact.Id || state.CashGrossSha256 != fact.GrossHash
            || state.CashCapturedAtUtc != fact.CapturedAtUtc || state.CashCutoffExclusiveLocal != fact.CutoffExclusiveLocal)
            throw new ExecutionFactVersionConflictException();
    }

    internal static IReadOnlyList<RegisterSourceValue343> Area(RegisterSnapshot343 state,
        Guid? cfo, Guid? article, DateOnly? month, BusinessDataReadScope scope)
    {
        if (!state.Enabled || state.CashSnapshotId is null) throw new CashFlowActualsNotAvailableException();
        var values = state.Values.Where(v => scope.Contains(v.CostCenterId)
            && (!cfo.HasValue || v.CostCenterId == cfo)
            && (!article.HasValue || v.CashFlowItemId == article)
            && (!month.HasValue || v.BudgetMonth == month)).ToArray();
        if (values.Any(v => v.Kind is not ("Limit" or "Reserve" or "Paid") || v.AmountKzt < 0m
            || decimal.Round(v.AmountKzt, 2) != v.AmountKzt || v.BudgetMonth.Day != 1
            || v.CostCenterId == Guid.Empty || v.CashFlowItemId == Guid.Empty)
            || values.Select(v => (v.Kind, v.SourceId, v.Key)).Distinct().Count() != values.Length)
            throw new BudgetProjectionDriftException("Invalid posted execution values.");
        return values;
    }

    internal static BudgetExecutionExportSlice Project(IReadOnlyList<RegisterSourceValue343> values,
        IReadOnlyDictionary<Guid, (string Code, string Name)> centers,
        IReadOnlyDictionary<Guid, (string Code, string Name)> articles)
    {
        var rows = values.GroupBy(v => v.Key).Select(g =>
        {
            if (!centers.TryGetValue(g.Key.CostCenterId, out var cfo)
                || !articles.TryGetValue(g.Key.CashFlowItemId, out var article))
                throw new CashFlowActualsMappingUnavailableException();
            return new BudgetExecutionExportRow(g.Key.BudgetMonth, cfo.Code, cfo.Name, article.Code, article.Name,
                g.Where(v => v.Kind == "Limit").Sum(v => v.AmountKzt),
                g.Where(v => v.Kind == "Reserve").Sum(v => v.AmountKzt),
                ExecutionPaidExclusionPolicy.IsExcluded(g.Key.CashFlowItemId) ? 0m : g.Where(v => v.Kind == "Paid").Sum(v => v.AmountKzt))
                { CostCenterId = g.Key.CostCenterId, CashFlowItemId = g.Key.CashFlowItemId };
        }).OrderByDescending(r => r.BudgetMonth).ThenBy(r => r.CostCenterId).ThenBy(r => r.CashFlowItemId).ToArray();
        if (rows.Length > BudgetExecutionExportPolicy.MaximumRows)
            throw new BudgetExecutionExportTooLargeException(rows.Length, BudgetExecutionExportPolicy.MaximumRows);
        return new(rows, rows.Length, rows.Length, 0, rows.Sum(r => r.LimitAmountKzt ?? 0m),
            rows.Sum(r => r.ReservedAmountKzt), rows.Sum(r => r.PaidAmountKzt));
    }

    public async Task<BudgetExecutionExportSlice> ReadAsync(Guid? cfo, Guid? article, DateOnly? month,
        BusinessDataReadScope scope, DirectDdsSnapshot? fact, CancellationToken token)
    {
        var state = await new BudgetExecutionStorage343(db).ReadAsync(token);
        if (fact is not null) VerifyCapture(state, fact);
        var area = Area(state, cfo, article, month, scope);
        var centerIds = area.Select(v => v.CostCenterId).Distinct().ToArray();
        var articleIds = area.Select(v => v.CashFlowItemId).Distinct().ToArray();
        var centers = await db.CostCenters.AsNoTracking().Where(c => centerIds.Contains(c.Id) && !c.IsGroup)
            .Select(c => new { c.Id, c.Code, c.Name }).ToArrayAsync(token);
        var articles = await db.CashFlowItems.AsNoTracking().Where(a => articleIds.Contains(a.Id) && !a.IsGroup)
            .Select(a => new { a.Id, a.Code, a.Name }).ToArrayAsync(token);
        return Project(area, centers.ToDictionary(c => c.Id, c => (c.Code, c.Name)),
            articles.ToDictionary(a => a.Id, a => (a.Code, a.Name)));
    }

    private sealed record Entry(BudgetKey Key, Guid RequestId, string Status, string? Purpose,
        DateOnly Date, int Lines, decimal Amount, bool Visible);

    private async Task<(IReadOnlyList<RegisterSourceValue343> Area, Entry[] Entries)> Reservations(
        Guid? cfo, Guid? article, DateOnly? month, BusinessDataReadScope scope, CancellationToken token)
    {
        var state = await new BudgetExecutionStorage343(db).ReadAsync(token);
        var area = Area(state, cfo, article, month, scope);
        var reserves = area.Where(v => v.Kind == "Reserve" && v.AmountKzt > 0m).ToArray();
        var ids = reserves.Select(v => v.SourceId).Distinct().ToArray();
        var requests = await db.ExpenseRequests.AsNoTracking().Where(r => ids.Contains(r.Id))
            .Include(r => r.Lines).Take(20001).ToArrayAsync(token);
        if (requests.Length > 20000 || requests.Length != ids.Length)
            throw new BudgetProjectionDriftException("A posted reservation has no available request.");
        var projections = await db.BudgetRequestProjections.AsNoTracking().Where(p => ids.Contains(p.ExpenseRequestId))
            .Take(100001).ToArrayAsync(token);
        if (projections.Length > 100000) throw new BudgetExecutionExportTooLargeException(projections.Length, 100000);
        var counts = projections.GroupBy(p => (p.ExpenseRequestId, p.Key))
            .ToDictionary(g => g.Key, g => (Lines: g.Count(), Amount: g.Sum(p => p.ReservedAmountKzt)));
        var byId = requests.ToDictionary(r => r.Id);
        var entries = reserves.Select(v =>
        {
            var request = byId[v.SourceId];
            if (!BudgetExecutionPosting343.Consumes(request.Status) || request.PlannedPaymentDate is null
                || !counts.TryGetValue((request.Id, v.Key), out var projection)
                || projection.Amount != v.AmountKzt || projection.Lines < 1)
                throw new BudgetProjectionDriftException("Posted reservation and document evidence disagree.");
            var visible = scope.Contains(request.DefaultCostCenterId, request.Lines.Select(l => l.CostCenterId));
            return new Entry(v.Key, request.Id, request.Status.ToCode(), request.Purpose,
                request.PlannedPaymentDate.Value, projection.Lines, v.AmountKzt, visible);
        }).OrderByDescending(e => e.Key.BudgetMonth).ThenBy(e => e.Key.CostCenterId).ThenBy(e => e.Key.CashFlowItemId)
            .ThenBy(e => e.Date).ThenBy(e => e.Purpose).ThenBy(e => e.RequestId).ToArray();
        return (area, entries);
    }

    public async Task<BudgetReservationSlice?> ReadReservationAsync(BudgetKey key, int skip, int take,
        BusinessDataReadScope scope, CancellationToken token)
    {
        var capture = await Reservations(key.CostCenterId, key.CashFlowItemId, key.BudgetMonth, scope, token);
        if (capture.Area.Count == 0) return null;
        var visible = capture.Entries.Where(e => e.Visible).ToArray();
        var total = capture.Entries.Sum(e => e.Amount);
        if (total != capture.Area.Where(v => v.Kind == "Reserve").Sum(v => v.AmountKzt))
            throw new BudgetProjectionDriftException("Register reservation totals disagree.");
        return new(capture.Area.Where(v => v.Kind == "Limit").Sum(v => v.AmountKzt), total,
            capture.Area.Where(v => v.Kind == "Paid").Sum(v => v.AmountKzt),
            visible.Skip(skip).Take(take).Select(e => new BudgetReservationParticipant(e.RequestId,
                e.Status, e.Purpose, e.Date, e.Lines, e.Amount)).ToArray(),
            visible.Length, visible.Sum(e => e.Lines), total) { VisibleReservedAmountKzt = visible.Sum(e => e.Amount) };
    }

    public async Task<BudgetReservationExportCapture> ReadReservationsForExportAsync(Guid? cfo, Guid? article,
        DateOnly? month, BusinessDataReadScope scope, CancellationToken token)
    {
        var capture = await Reservations(cfo, article, month, scope, token);
        var positions = await ReadAsync(cfo, article, month, scope, null, token);
        var byKey = positions.Rows.ToDictionary(r => r.Key);
        var rows = capture.Entries.Where(e => e.Visible).Select(e =>
        {
            var p = byKey[e.Key];
            return new BudgetReservationExportRow(e.Key.BudgetMonth, p.CostCenterCode, p.CostCenterName,
                p.CashFlowItemCode, p.CashFlowItemName, e.Status, e.Purpose, e.Date, e.Lines, e.Amount)
                { CostCenterId = e.Key.CostCenterId, CashFlowItemId = e.Key.CashFlowItemId };
        }).ToArray();
        var entriesByKey = capture.Entries.GroupBy(e => e.Key).ToDictionary(g => g.Key);
        var totals = positions.Rows.Select(p =>
        {
            var entries = entriesByKey.GetValueOrDefault(p.Key)?.ToArray() ?? [];
            return new BudgetReservationExportTotal(p.Key, entries.Length, entries.Sum(e => e.Lines), entries.Sum(e => e.Amount));
        }).ToArray();
        return new(rows, totals);
    }
}
