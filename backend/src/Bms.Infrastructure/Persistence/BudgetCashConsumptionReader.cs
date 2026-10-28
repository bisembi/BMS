using Bms.Application.Abstractions;
using Bms.Application.Budgeting;
using Bms.Domain.Budgeting;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>Coverage is proved against the exact immutable DDS capture, never
/// just a payment date, amount or the existence of an old import receipt.</summary>
public interface IAccountingPaymentCoverageReader
{
    Task<IReadOnlyDictionary<BudgetKey, decimal>> ReadRepresentedAsync(Guid factSnapshotId,
        IReadOnlyCollection<BudgetKey> keys, CancellationToken token);
}

public sealed class BudgetCashConsumptionReader(BmsDbContext db, IDirectDdsReplicaReader source,
    IAccountingPaymentCoverageReader coverage, AccountingReplicaLocation? location=null) : IBudgetCashConsumptionReader
{
    private DirectDdsSnapshot? captured;
    public ExecutionFactSourceDto? CapturedSource => captured is null ? null
        : new(captured.Id,captured.SourceGeneration,captured.GrossHash,captured.CapturedAtUtc,captured.CutoffExclusiveLocal,captured.Months.Max(m=>m.Year));

    public async Task<IReadOnlyDictionary<BudgetKey, BudgetCashConsumptionAmount>> ReadAsync(
        IReadOnlyCollection<BudgetKey> keys, CancellationToken token)
    {
        if (keys.Count == 0) return new Dictionary<BudgetKey, BudgetCashConsumptionAmount>();
        if (keys.Any(k => !AccountingPeriod467.IsMonth(k.BudgetMonth))) throw new CashFlowActualsPeriodUnavailableException();
        if(captured is null)
        {
            await AccountingPaymentCaptureLock.AcquireAsync(db,false,token,location);
            var candidate=await CurrentDdsActualsSource.ReadRequiredAsync(source, token);
            var now=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5));
            var expected=new DateTimeOffset(now.Date.AddHours(7),TimeSpan.FromHours(5));
            if(now<expected)expected=expected.AddDays(-1);
            if(candidate.CapturedAtUtc<expected)
                throw new CashFlowActualsNotAvailableException();
            // Never cache a rejected capture: a second read in this scope must
            // not bypass the freshness guard after read-only list degradation.
            captured=candidate;
        }
        var ids=keys.Select(k=>k.CashFlowItemId).Distinct().ToArray();
        var items=await db.CashFlowItems.AsNoTracking().Where(i=>ids.Contains(i.Id) && !i.IsGroup)
            .Select(i=>new{i.Id,i.CostCenterId}).ToArrayAsync(token);
        if(items.Length!=ids.Length) throw new CashFlowActualsMappingUnavailableException();
        var byId=items.ToDictionary(i=>i.Id,i=>i.CostCenterId);
        // Execution-only transfer exclusions do not govern admission. The
        // operational limit deducts all accepted 1C cash for the exact article.
        var cash=captured.Cells.Where(c=>ids.Contains(c.CashFlowItemId))
            .ToDictionary(c=>(c.CashFlowItemId,c.BudgetMonth),c=>c.PaymentsAmountKzt);
        var represented=await coverage.ReadRepresentedAsync(captured.Id,keys,token);
        return keys.Distinct().ToDictionary(k=>k,k=>new BudgetCashConsumptionAmount(
            byId[k.CashFlowItemId]==k.CostCenterId ? cash.GetValueOrDefault((k.CashFlowItemId,k.BudgetMonth)) : 0m,
            represented.GetValueOrDefault(k)));
    }
}
