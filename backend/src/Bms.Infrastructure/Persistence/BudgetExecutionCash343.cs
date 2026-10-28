using Bms.Domain.Budgeting;
using Bms.Application.Abstractions;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

internal sealed class BudgetExecutionCash343(BmsDbContext db,IAccountingPaymentCoverageReader coverage,AccountingReplicaLocation? location=null)
{
    public async Task RefreshAsync(DirectDdsSnapshot snapshot,CancellationToken token)
    {
        snapshot.Validate();
        var items=await db.CashFlowItems.AsNoTracking().Where(i=>!i.IsGroup).Select(i=>new{i.Id,i.CostCenterId}).ToArrayAsync(token);
        var owner=items.ToDictionary(i=>i.Id,i=>i.CostCenterId);
        var source=snapshot.Cells.Where(c=>owner.TryGetValue(c.CashFlowItemId,out var cfo)&&cfo.HasValue)
            .ToDictionary(c=>BudgetKey.Create(owner[c.CashFlowItemId]??throw new InvalidOperationException("Cash owner missing."),c.CashFlowItemId,c.BudgetMonth),c=>c.PaymentsAmountKzt);
        _=coverage; // Constructor compatibility with the guarded existing operator.
        var links=await db.AccountingPaymentLinks.AsNoTracking().Where(l=>l.ReversedAtUtc==null).Take(100001).ToArrayAsync(token);
        if(links.Length>100000)throw new InvalidOperationException("Budget payment link bound exceeded.");
        var ids=links.Select(l=>l.Id).ToArray();
        var lines=await (from line in db.AccountingPaymentLinkLines.AsNoTracking()
            join requestLine in db.ExpenseRequestLines.AsNoTracking() on line.ExpenseRequestLineId equals requestLine.Id
            where ids.Contains(line.LinkId)
            select new{Line=line,requestLine.SortOrder}).Take(100001).ToArrayAsync(token);
        if(lines.Length>100000)throw new InvalidOperationException("Budget payment allocation bound exceeded.");
        var linesByLink=lines.ToLookup(l=>l.Line.LinkId);
        var approved400=await ApprovedAccountingEvidence400.ReadAsync(db,token);
        var proof=await new BudgetExecutionBankSource343(db,location).ReadAsync(snapshot.Id,links.Select(l=>l.DocumentKey)
            .Concat(approved400.Select(g=>g.DocumentKey)).Distinct(StringComparer.Ordinal).ToArray(),token);
        var proofByDocument=proof.GroupBy(p=>p.Leg.DocumentKey).ToDictionary(g=>g.Key,g=>g.ToDictionary(p=>p.Leg.LineNo),StringComparer.Ordinal);
        var represented=new Dictionary<BudgetKey,decimal>();var transfers=new List<RegisterCashTransfer343>();
        var seen=new HashSet<(string,int)>();
        foreach(var link in links)
        {
            token.ThrowIfCancellationRequested();
            if(link.SourceLegsJson.Length>1000000)throw new InvalidOperationException("Bank leg evidence bound exceeded.");
            var expected=JsonSerializer.Deserialize<AccountingSourceCashLeg[]>(link.SourceLegsJson,new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ??throw new InvalidOperationException("Bank leg evidence missing.");
            if(expected.Length is <1 or >2000||expected.Select(l=>l.LineNo).Distinct().Count()!=expected.Length
                ||expected.Any(l=>l.DocumentKey!=link.DocumentKey||l.AmountKzt<=0m)||expected.Sum(l=>l.AmountKzt)!=link.AmountKzt
                ||!proofByDocument.TryGetValue(link.DocumentKey,out var found))
                throw new InvalidOperationException("Complete source bank payment proof required.");
            foreach(var leg in expected)
            {
                if(!seen.Add((leg.DocumentKey,leg.LineNo))||!found.TryGetValue(leg.LineNo,out var row)||row.Leg!=leg
                    ||!owner.TryGetValue(row.CashFlowItemId,out var cfo)||cfo is null)
                    throw new InvalidOperationException("Exact unique source bank leg/article ownership required.");
                var date=DateOnly.ParseExact(leg.EntryDateLocal[..10],"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
                if(date!=link.PaymentDay)throw new InvalidOperationException("Bank payment date proof differs.");
                var key=BudgetKey.Create(cfo.Value,row.CashFlowItemId,new(date.Year,date.Month,1));
                represented[key]=checked(represented.GetValueOrDefault(key)+leg.AmountKzt);
            }
            var shares=linesByLink[link.Id].Select(l=>new BudgetPaymentShare343(l.Line.ExpenseRequestLineId,l.SortOrder,
                BudgetKey.Create(l.Line.CostCenterId,l.Line.CashFlowItemId,l.Line.BudgetMonth),l.Line.OriginalReservePaidKzt)).ToArray();
            var allocation=BudgetPaymentAttribution343.Allocate(link.AmountKzt,shares);
            var original=shares.GroupBy(s=>s.Key).ToDictionary(g=>g.Key,g=>g.Sum(s=>s.ReleasedOriginalKzt));
            foreach(var pair in allocation)
            {
                var evidence=JsonSerializer.Serialize(new{linkId=link.Id,documentKey=link.DocumentKey,financialHash=link.FinancialHash,
                    actualPaymentDay=link.PaymentDay,actualBankKzt=link.AmountKzt,originalReleasedKzt=shares.Sum(s=>s.ReleasedOriginalKzt),
                    attributedBankKzt=pair.Value,originalKeyReleasedKzt=original[pair.Key],budgetMonth=pair.Key.BudgetMonth,
                    sourceLegsSha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(link.SourceLegsJson))),
                    rule="REQUEST_MONTH_PROPORTIONAL_SETTLED_LINES_TIYN_LAST_REMAINDER"},new JsonSerializerOptions(JsonSerializerDefaults.Web));
                transfers.Add(new(link.Id,link.ExpenseRequestId,pair.Key.CostCenterId,pair.Key.CashFlowItemId,pair.Key.BudgetMonth,
                    pair.Value,original[pair.Key],snapshot.Id,evidence));
            }
        }
        foreach(var group in approved400)
        {
            if(!proofByDocument.TryGetValue(group.DocumentKey,out var found))
                throw new InvalidOperationException("Approved cash pool is not represented in current DDS.");
            // A shared primary document contributes its real cash legs ONCE,
            // independently of how many requests share the approved pool.
            foreach(var leg in group.Legs)
            {
                if(!seen.Add((leg.DocumentKey,leg.LineNo)) || !found.TryGetValue(leg.LineNo,out var row) || row.Leg!=leg
                    || !owner.TryGetValue(row.CashFlowItemId,out var cfo) || cfo is null)
                    throw new InvalidOperationException("Exact unique approved cash pool proof required.");
                var date=DateOnly.ParseExact(leg.EntryDateLocal[..10],"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
                if(date!=group.PaymentDay)throw new InvalidOperationException("Approved bank date differs.");
                var key=BudgetKey.Create(cfo.Value,row.CashFlowItemId,new(date.Year,date.Month,1));
                represented[key]=checked(represented.GetValueOrDefault(key)+leg.AmountKzt);
            }
            foreach(var target in group.Allocations)
            {
                var shares=ApprovedAccountingEvidence400.Shares(target);
                var allocation=BudgetPaymentAttribution343.Allocate(target.CashAmount,shares);
                var original=shares.GroupBy(s=>s.Key).ToDictionary(g=>g.Key,g=>g.Sum(s=>s.ReleasedOriginalKzt));
                foreach(var pair in allocation)
                {
                    var evidence=JsonSerializer.Serialize(new{approvalId=group.ApprovalId,documentKey=group.DocumentKey,
                        financialHash=group.FinancialHash,actualPaymentDay=group.PaymentDay,parentBankCashKzt=group.SourceCashAmount,
                        actualBankKzt=target.CashAmount,approvedRoundingKzt=target.RoundingAmount,attributedBankKzt=pair.Value,
                        originalKeyReleasedKzt=original[pair.Key],rule="EXPLICIT_APPROVAL_SHARED_PRIMARY_POOL_BANK_CASH_ONLY"},
                        ApprovedAccountingEvidence400.Json);
                    transfers.Add(new(target.EventId,target.ExpenseRequestId,pair.Key.CostCenterId,pair.Key.CashFlowItemId,
                        pair.Key.BudgetMonth,pair.Value,original[pair.Key],snapshot.Id,evidence));
                }
            }
        }
        var approvedHistory400=await ApprovedAccountingEvidence400.ReadAsync(db,token,includeReversed:true);
        var activeApprovals400=approved400.Select(g=>g.ApprovalId).ToHashSet();
        var sourceYear400=snapshot.CutoffExclusiveLocal.AddDays(-1).Year;
        foreach(var group in approvedHistory400.Where(g=>!activeApprovals400.Contains(g.ApprovalId)&&g.PaymentDay.Year==sourceYear400))
            foreach(var target in group.Allocations)
                foreach(var share in target.Lines.GroupBy(l=>BudgetKey.Create(l.CostCenterId,l.CashFlowItemId,l.BudgetMonth)))
                    transfers.Add(new(target.EventId,target.ExpenseRequestId,share.Key.CostCenterId,share.Key.CashFlowItemId,
                        share.Key.BudgetMonth,0m,share.Sum(l=>l.ReleasedOriginalKzt),snapshot.Id,
                        JsonSerializer.Serialize(new{approvalId=group.ApprovalId,rule="APPROVED_ALLOCATION_REVERSED_NO_BANK_CASH"},ApprovedAccountingEvidence400.Json)));
        var controlled400=approvedHistory400.Where(g=>g.PaymentDay.Year==sourceYear400).SelectMany(g=>g.Allocations)
            .Select(a=>a.EventId).ToArray();
        var positions=await db.BudgetPositions.AsNoTracking().ToArrayAsync(token);
        var keys=source.Keys.Concat(represented.Keys).Concat(positions.Select(p=>p.Key).Where(k=>snapshot.Months.Contains(k.BudgetMonth))).Distinct().ToArray();
        if(represented.Any(p=>p.Value>source.GetValueOrDefault(p.Key)))
            throw new InvalidOperationException("Proven linked bank cash exceeds the accepted source article/month total.");
        var cells=keys.Select(k=>new RegisterCashValue343(k.CostCenterId,k.CashFlowItemId,k.BudgetMonth,
            source.GetValueOrDefault(k),represented.GetValueOrDefault(k),snapshot.Id)).ToArray();
        await new BudgetExecutionStorage343(db).AcceptCashAsync(snapshot,cells,token,transfers,controlled400);
        db.BudgetRegisterFinancialWrites343=true;
    }
}
