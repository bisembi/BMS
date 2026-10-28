using Bms.Domain.Budgeting;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

internal sealed record RegisterFrame343(RegisterSnapshot343 State,IReadOnlyList<ExpenseRequest> Requests,
    IReadOnlyDictionary<Guid,string?> Fingerprints,IReadOnlyList<BudgetExecutionOpening343> Openings,
    IReadOnlyList<BudgetExecutionRequest343> Inputs,BudgetExecutionEvaluation343 Evaluation,
    IReadOnlyDictionary<Guid,BudgetExecutionEpisode343> Episodes);

internal sealed class BudgetExecutionFrameReader343(BmsDbContext db)
{
    public async Task<RegisterFrame343> ReadAsync(Guid? candidateId,
        IReadOnlyCollection<BudgetRequestProjection>? candidateProjections,bool candidateSubmitted,CancellationToken token,RegisterSnapshot343? snapshotOverride=null,
        IReadOnlyCollection<BudgetKey>? requestedKeys=null)
    {
        var storage=new BudgetExecutionStorage343(db);var state=snapshotOverride??await storage.ReadAsync(token);
        if(state.CashSnapshotId is null)throw new InvalidOperationException("Budget register has no accepted cash opening.");
        var requests=await db.ExpenseRequests.AsNoTracking().Include(r=>r.Lines).OrderBy(r=>r.Id).Take(20001).ToArrayAsync(token);
        if(requests.Length>20000||requests.Sum(r=>r.Lines.Count)>100000)throw new InvalidOperationException("Budget register request bound exceeded.");
        var episodes=await BudgetExecutionEpisodes343.ReadAsync(db,requests,token);
        var fingerprints=requests.ToDictionary(r=>r.Id,r=>episodes[r.Id].Fingerprint(BudgetFinancialFingerprint343.FromRequest(r)));
        // Only independently captured bank cash, already reattributed to the
        // original request month, may occupy the request's FIFO position.
        var paidQueue=state.Values.Where(v=>v.Kind=="Paid"&&v.SourceId!=Guid.Empty&&v.AmountKzt>0m)
            .GroupBy(v=>v.SourceId).ToDictionary(g=>g.Key,g=>g.ToDictionary(v=>v.Key,v=>v.AmountKzt));
        var frozen=state.Frozen.ToDictionary(f=>(f.RequestId,f.FinancialFingerprint));
        var reservations=state.Values.Where(v=>v.Kind=="Reserve"&&v.AmountKzt>0m).GroupBy(v=>v.SourceId)
            .ToDictionary(g=>g.Key,g=>g.ToDictionary(v=>v.Key,v=>v.AmountKzt));
        var inputs=new List<BudgetExecutionRequest343>();
        foreach(var r in requests)
        {
            var active=r.Status is not(ExpenseRequestStatus.Cancelled or ExpenseRequestStatus.Rejected or ExpenseRequestStatus.Paid or ExpenseRequestStatus.Closed);
            var submitted=r.Status is not(ExpenseRequestStatus.Draft or ExpenseRequestStatus.Returned);
            var projection=reservations.GetValueOrDefault(r.Id)??new Dictionary<BudgetKey,decimal>();
            if(r.Status is (ExpenseRequestStatus.Draft or ExpenseRequestStatus.Returned)&&fingerprints[r.Id] is not null)
                projection=ExpenseRequestBudgetProjectionCalculator.Calculate(r).Projections.GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.Sum(p=>p.ReservedAmountKzt));
            if(candidateId==r.Id&&candidateProjections is not null)
            {
                if(candidateProjections.Any(p=>p.ExpenseRequestId!=r.Id))throw new InvalidOperationException("Candidate reservation identity mismatch.");
                projection=candidateProjections.GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.Sum(p=>p.ReservedAmountKzt));
                submitted=candidateSubmitted;
            }
            var approved=r.Status is not(ExpenseRequestStatus.Draft or ExpenseRequestStatus.Returned)&&fingerprints[r.Id] is{ } fingerprint&&frozen.TryGetValue((r.Id,fingerprint),out var f)
                ?new BudgetExecutionFrozen343(f.PositiveDeficitKzt,f.FinancialFingerprint,f.ApprovedAtUtc):null;
            var queuedPayments=paidQueue.GetValueOrDefault(r.Id)??new Dictionary<BudgetKey,decimal>();
            if(projection.Count==0&&approved is null&&queuedPayments.Count==0)continue;
            inputs.Add(new(r.Id,episodes[r.Id].QueueAtUtc,submitted,active,projection,approved){PaidQueueAmounts=queuedPayments});
        }
        if(candidateId is{} candidate&&candidateProjections is not null&&!byRequestId(requests,candidate))
        {
            if(candidate==Guid.Empty||candidateProjections.Any(p=>p.ExpenseRequestId!=candidate))throw new InvalidOperationException("Invalid transient budget candidate.");
            inputs.Add(new(candidate,DateTimeOffset.UtcNow,candidateSubmitted,true,candidateProjections.GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.Sum(p=>p.ReservedAmountKzt)),null));
        }
        // A read may ask for a future month with no posted cell, or both sides
        // of an unsaved month change. Capture those exact keys as well; never
        // substitute today's month or post a movement for the preview.
        var keys=state.Values.Select(v=>v.Key).Concat(inputs.SelectMany(r=>r.RemainingReservations.Keys.Concat(r.PaidQueueAmounts.Keys)))
            .Concat(requestedKeys??[]).Distinct().ToArray();
        var limits=state.Values.Where(v=>v.Kind=="Limit").ToDictionary(v=>v.Key,v=>v.AmountKzt);
        var paid=state.Values.Where(v=>v.Kind=="Paid").GroupBy(v=>v.Key).ToDictionary(g=>g.Key,g=>g.Sum(v=>v.AmountKzt));
        // A draft can introduce a previously unused zero-budget key. Resolve its
        // explicit published limit without posting a preview or altering state.
        var missing=keys.Where(k=>!limits.ContainsKey(k)).ToArray();
        if(missing.Length>0)
        {
            var versions=await CompanyBudgetPlanReader.ReadActiveLimitsAsync(db,missing.Select(k=>k.CostCenterId).Distinct().ToArray(),token);
            foreach(var key in missing)limits[key]=BudgetEffectiveLimitResolver.Resolve(null,key.CostCenterId,key.CashFlowItemId,key.BudgetMonth,versions);
        }
        var openings=keys.Select(k=>new BudgetExecutionOpening343(k,limits[k],paid.GetValueOrDefault(k))).ToArray();
        return new(state,requests,fingerprints,openings,inputs,BudgetExecutionFifo343.Evaluate(openings,inputs,token),episodes);
    }
    private static bool byRequestId(IEnumerable<ExpenseRequest> requests,Guid id)=>requests.Any(r=>r.Id==id);
}
