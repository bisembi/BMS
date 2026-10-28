using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Budgeting;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class AccountingPaymentCoverageReader(BmsDbContext db,IAccountingPaymentSourceReader source)
    : IAccountingPaymentCoverageReader
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyDictionary<BudgetKey,decimal>> ReadRepresentedAsync(Guid factSnapshotId,
        IReadOnlyCollection<BudgetKey> keys,CancellationToken token)
    {
        var requested=keys.ToHashSet();
        var result=requested.ToDictionary(k=>k,_=>0m);
        var itemIds=requested.Select(k=>k.CashFlowItemId).Distinct().ToArray();
        var centerIds=requested.Select(k=>k.CostCenterId).Distinct().ToArray();
        var months=requested.Select(k=>k.BudgetMonth).Distinct().ToArray();
        var lines=await (from line in db.AccountingPaymentLinkLines.AsNoTracking()
            join link in db.AccountingPaymentLinks.AsNoTracking() on line.LinkId equals link.Id
            where link.ReversedAtUtc==null && itemIds.Contains(line.CashFlowItemId)
                && centerIds.Contains(line.CostCenterId) && months.Contains(line.BudgetMonth)
            select new {Line=line,Link=link}).ToArrayAsync(token);
        var approved400=await ApprovedAccountingEvidence400.ReadAsync(db,token);
        if(lines.Length==0 && approved400.Count==0) return result;
        var links=lines.Select(x=>x.Link).DistinctBy(x=>x.Id).ToArray();
        if(links.Length>100_000) throw new ExpenseRequestPaymentSummaryUnavailableException();
        var actual=await source.ReadRepresentedLegsAsync(factSnapshotId,links.Select(l=>l.DocumentKey)
            .Concat(approved400.Select(g=>g.DocumentKey)).Distinct(StringComparer.Ordinal).ToArray(),token);
        var actualByKey=actual.GroupBy(l=>l.DocumentKey).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
        var represented=new HashSet<Guid>();
        foreach(var link in links)
        {
            if(link.SourceLegsJson.Length>1_000_000) throw new ExpenseRequestPaymentSummaryUnavailableException();
            var expected=JsonSerializer.Deserialize<AccountingSourceCashLeg[]>(link.SourceLegsJson,Json)
                ?? throw new ExpenseRequestPaymentSummaryUnavailableException();
            if(expected.Length is <1 or >2000 || expected.Any(l=>l.DocumentKey!=link.DocumentKey||l.AmountKzt<=0m)
                || expected.Select(l=>l.LineNo).Distinct().Count()!=expected.Length)
                throw new ExpenseRequestPaymentSummaryUnavailableException();
            if(!actualByKey.TryGetValue(link.DocumentKey,out var found))
                throw new ExpenseRequestPaymentSummaryUnavailableException();
            var byLine=found.ToDictionary(l=>l.LineNo);
            if(!expected.All(l=>byLine.TryGetValue(l.LineNo,out var row)&&row==l))
                throw new ExpenseRequestPaymentSummaryUnavailableException();
            represented.Add(link.Id);
        }
        foreach(var item in lines.Where(x=>represented.Contains(x.Link.Id)))
        {
            var key=BudgetKey.Create(item.Line.CostCenterId,item.Line.CashFlowItemId,item.Line.BudgetMonth);
            if(requested.Contains(key)) result[key]=checked(result[key]+item.Line.OriginalReservePaidKzt);
        }
        foreach(var group in approved400)
        {
            if(links.Any(l=>l.DocumentKey==group.DocumentKey)
                || !actualByKey.TryGetValue(group.DocumentKey,out var found)
                || !AccountingPaymentReconciliation.SameLegs(group.Legs,found))
                throw new ExpenseRequestPaymentSummaryUnavailableException();
            foreach(var line in group.Allocations.SelectMany(a=>a.Lines))
            {
                var key=BudgetKey.Create(line.CostCenterId,line.CashFlowItemId,line.BudgetMonth);
                if(requested.Contains(key))result[key]=checked(result[key]+line.ReleasedOriginalKzt);
            }
        }
        return result;
    }
}
