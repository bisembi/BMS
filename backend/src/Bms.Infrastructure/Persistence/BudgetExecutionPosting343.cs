using System.Text.Json;
using Bms.Domain.Budgeting;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>Internal transaction commit projection. It never creates a payment,
/// approval or a balancing article. All posted values come from committed BMS
/// business records and a separately verified cash capture.</summary>
internal sealed class BudgetExecutionPosting343(BmsDbContext db)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);

    // Active-composition edits alter accepted limits only. Do not reconcile
    // cash, reserves, approval decisions or request routes in this operation.
    public async Task RecalculateActiveLimits454Async(CancellationToken token)
    {
        if(db.BudgetRegisterReadOnly343 || db.BudgetRegisterTouchedRequests343.Count!=0)
            throw new InvalidOperationException("Composition-only transaction required.");
        var storage=new BudgetExecutionStorage343(db);var state=await storage.ReadAsync(token);
        if(!state.Enabled || state.CashSnapshotId is null)throw new InvalidOperationException("Accepted register required.");
        var versions=await CompanyBudgetPlanReader.ReadActiveLimitsAsync(db,null,token);
        var active=await CompanyBudgetPlanReader.ActiveAsync(db,token);
        var keys=versions.SelectMany(v=>v.Lines.Select(l=>BudgetKey.Create(v.CostCenterId,l.CashFlowItemId,l.BudgetMonth)))
            .Concat(state.Values.Select(v=>v.Key)).Distinct().ToArray();
        if(keys.Length>40000)throw new InvalidOperationException("Budget register key bound exceeded.");
        var evidence=JsonSerializer.Serialize(new{versionId=active.VersionId,activeConcurrencyStamp=active.ConcurrencyStamp,
            operation="ActiveCompositionUpdated"},Json);
        var values=state.Values.Where(v=>v.Kind!="Limit").ToList();
        values.AddRange(keys.Select(key=>new RegisterSourceValue343("Limit",Guid.Empty,key.CostCenterId,key.CashFlowItemId,key.BudgetMonth,
            BudgetEffectiveLimitResolver.Resolve(null,key.CostCenterId,key.CashFlowItemId,key.BudgetMonth,versions),
            "Изменение состава действующей версии",active.ConcurrencyStamp.ToString("D"),evidence)));
        var result=await storage.PostAsync(values,[],Guid.NewGuid(),token);
        if(result.Frozen!=0)throw new InvalidOperationException("Composition edit cannot change approved decisions.");
        var after=await storage.ReadAsync(token);
        var untouched=state.Values.Where(v=>v.Kind!="Limit").OrderBy(v=>v.Kind).ThenBy(v=>v.SourceId).ThenBy(v=>v.CostCenterId)
            .ThenBy(v=>v.CashFlowItemId).ThenBy(v=>v.BudgetMonth).ToArray();
        var confirmed=after.Values.Where(v=>v.Kind!="Limit").OrderBy(v=>v.Kind).ThenBy(v=>v.SourceId).ThenBy(v=>v.CostCenterId)
            .ThenBy(v=>v.CashFlowItemId).ThenBy(v=>v.BudgetMonth).ToArray();
        if(!untouched.SequenceEqual(confirmed) || !state.Frozen.OrderBy(v=>v.RequestId).ThenBy(v=>v.FinancialFingerprint)
            .SequenceEqual(after.Frozen.OrderBy(v=>v.RequestId).ThenBy(v=>v.FinancialFingerprint))
            || !state.Cash.SequenceEqual(after.Cash) || !state.Transfers.SequenceEqual(after.Transfers)
            || state.CashSnapshotId!=after.CashSnapshotId || state.CashGrossSha256!=after.CashGrossSha256)
            throw new InvalidOperationException("Composition edit changed protected register values.");
    }

    public async Task<(int Movements,int Frozen)> SynchronizeAsync(bool bootstrap,CancellationToken token,int pass=0)
    {
        if(pass>2)throw new InvalidOperationException("FIFO register reconciliation did not converge.");
        var storage=new BudgetExecutionStorage343(db);var state=await storage.ReadAsync(token);
        if(state.CashSnapshotId is null)throw new InvalidOperationException("Verified cash opening required before budget register posting.");
        var requests=await db.ExpenseRequests.AsNoTracking().Include(r=>r.Lines).OrderBy(r=>r.Id).Take(20001).ToArrayAsync(token);
        if(requests.Length>20000||requests.Sum(r=>r.Lines.Count)>100000)throw new InvalidOperationException("Budget register source bound exceeded.");
        var byId=requests.ToDictionary(r=>r.Id);
        var episodes=await BudgetExecutionEpisodes343.ReadAsync(db,requests,token);
        var projections=await db.BudgetRequestProjections.AsNoTracking().OrderBy(p=>p.Id).Take(100001).ToArrayAsync(token);
        if(projections.Length>100000)throw new InvalidOperationException("Budget register reserve bound exceeded.");
        var positions=await db.BudgetPositions.AsNoTracking().Take(100001).ToArrayAsync(token);
        if(positions.Length>100000)throw new InvalidOperationException("Budget register position bound exceeded.");
        // Preserve and independently verify the legacy aggregate rather than
        // rewriting it: legacy draft projections are not register reservations.
        var sums=projections.GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.Sum(p=>p.ReservedAmountKzt));
        if(positions.Any(p=>p.ReservedAmountKzt!=sums.GetValueOrDefault(p.Key))
            ||sums.Any(p=>!positions.Any(row=>row.Key==p.Key)&&p.Value!=0m))throw new InvalidOperationException("Legacy reserve aggregate drift.");
        var versions=await CompanyBudgetPlanReader.ReadActiveLimitsAsync(db,null,token);
        var active=await CompanyBudgetPlanReader.ActiveAsync(db,token);
        var adjustmentIds=await db.BudgetAdjustments.AsNoTracking().Where(a=>a.State=="Approved")
            .OrderBy(a=>a.ApprovalOrder).Select(a=>a.Id).ToArrayAsync(token);
        var keys=versions.SelectMany(v=>v.Lines.Select(l=>BudgetKey.Create(v.CostCenterId,l.CashFlowItemId,l.BudgetMonth)))
            .Concat(state.Values.Select(v=>v.Key))
            .Concat(projections.Where(p=>byId.TryGetValue(p.ExpenseRequestId,out var request)&&Consumes(request.Status)).Select(p=>p.Key))
            .Concat(positions.Where(p=>p.PaidAmountKzt>0m).Select(p=>p.Key)).Concat(state.Cash.Select(c=>c.Key)).Concat(state.Transfers.Select(t=>t.Key))
            .Distinct().ToArray();
        if(keys.Length>40000)throw new InvalidOperationException("Budget register key bound exceeded.");
        var values=new List<RegisterSourceValue343>();
        var versionEvidence=JsonSerializer.Serialize(new{versionId=active.VersionId,approvedAdjustmentIds=adjustmentIds},Json);
        if(versionEvidence.Length>4000)throw new InvalidOperationException("Budget register adjustment evidence bound exceeded.");
        foreach(var key in keys)
        {
            var limit=BudgetEffectiveLimitResolver.Resolve(null,key.CostCenterId,key.CashFlowItemId,key.BudgetMonth,versions);
            var name=bootstrap?"Начальный остаток принятого бюджета с утверждёнными корректировками":"Принятие бюджета / утверждённая корректировка лимита";
            values.Add(new("Limit",Guid.Empty,key.CostCenterId,key.CashFlowItemId,key.BudgetMonth,limit,name,active.VersionId.ToString("D"),versionEvidence));
        }
        // Draft projections are a legacy document cache, not register sources.
        // Even a new, unbudgeted draft key must not create zero register cells.
        foreach(var group in projections.Where(p=>byId.TryGetValue(p.ExpenseRequestId,out var request)&&Consumes(request.Status)).GroupBy(p=>(p.ExpenseRequestId,p.Key)))
        {
            var request=byId[group.Key.ExpenseRequestId];var key=group.Key.Key;
            values.Add(new("Reserve",request.Id,key.CostCenterId,key.CashFlowItemId,key.BudgetMonth,group.Sum(p=>p.ReservedAmountKzt),
                bootstrap?"Начальный неоплаченный остаток поданной заявки":ReleaseName(request.Status),request.ConcurrencyStamp.ToString("D"),
                JsonSerializer.Serialize(new{requestId=request.Id,number=request.RequestNumber,status=request.Status.ToCode(),createdAtUtc=request.CreatedAtUtc,
                    queueAtUtc=request.Status is ExpenseRequestStatus.Draft or ExpenseRequestStatus.Returned or ExpenseRequestStatus.Cancelled or ExpenseRequestStatus.Rejected
                        ?(DateTimeOffset?)null:episodes[request.Id].QueueAtUtc,returnAuditId=episodes[request.Id].ReturnAuditId},Json)));
        }
        var presentReserves=values.Where(v=>v.Kind=="Reserve").Select(v=>(v.SourceId,v.Key)).ToHashSet();
        foreach(var previous in state.Values.Where(v=>v.Kind=="Reserve"&&!presentReserves.Contains((v.SourceId,v.Key))))
        {
            // Historical zero cache rows must not prevent deleting a draft.
            // A nonzero missing source is released only with its durable tombstone.
            if(previous.AmountKzt==0m)continue;
            if(!byId.TryGetValue(previous.SourceId,out var request))
            {
                request=await db.ExpenseRequests.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(r=>r.Id==previous.SourceId,token);
                var deletion=await db.BusinessAuditEntries.AsNoTracking().Where(a=>a.ObjectType=="ExpenseRequest"
                        &&a.ObjectId==previous.SourceId.ToString("D")&&a.Action=="ExpenseRequestDraftDeleted")
                    .OrderBy(a=>a.OccurredAtUtc).Select(a=>new{a.Id,a.OccurredAtUtc}).FirstOrDefaultAsync(token);
                if(request is null||request.Status!=ExpenseRequestStatus.Draft||deletion is null)
                    throw new InvalidOperationException("Register reservation source request unavailable without verified deletion evidence.");
                values.Add(previous with{AmountKzt=0m,MovementName="Удаление заявки — освобождение лимита",SourceVersion=deletion.Id.ToString("D"),
                    Evidence=JsonSerializer.Serialize(new{requestId=request.Id,number=request.RequestNumber,deletionAuditId=deletion.Id,
                        deletedAtUtc=deletion.OccurredAtUtc,previousSourceVersion=previous.SourceVersion},Json)});
                continue;
            }
            values.Add(previous with{AmountKzt=0m,MovementName=ReleaseName(request.Status),SourceVersion=request.ConcurrencyStamp.ToString("D"),
                Evidence=JsonSerializer.Serialize(new{requestId=request.Id,number=request.RequestNumber,status=request.Status.ToCode(),previousSourceVersion=previous.SourceVersion},Json)});
        }
        var paidByKey=positions.ToDictionary(p=>p.Key,p=>p.PaidAmountKzt);var cash=state.Cash.ToDictionary(c=>c.Key);
        var approved400=await ApprovedAccountingEvidence400.ReadAsync(db,token);
        var activeLinks=(await db.AccountingPaymentLinks.AsNoTracking().Where(l=>l.ReversedAtUtc==null).Select(l=>l.Id).ToArrayAsync(token))
            .Concat(approved400.SelectMany(g=>g.Allocations).Select(a=>a.EventId)).ToArray();
        var originals=await db.AccountingPaymentLinkLines.AsNoTracking().Where(l=>activeLinks.Contains(l.LinkId)).Take(100001).ToArrayAsync(token);
        if(originals.Length>100000)throw new InvalidOperationException("Original payment evidence bound exceeded.");
        var originalByKey=originals.GroupBy(l=>BudgetKey.Create(l.CostCenterId,l.CashFlowItemId,l.BudgetMonth))
            .ToDictionary(g=>g.Key,g=>g.Sum(l=>l.OriginalReservePaidKzt));
        foreach(var line in approved400.SelectMany(g=>g.Allocations).SelectMany(a=>a.Lines))
        {
            var key=BudgetKey.Create(line.CostCenterId,line.CashFlowItemId,line.BudgetMonth);
            originalByKey[key]=checked(originalByKey.GetValueOrDefault(key)+line.ReleasedOriginalKzt);
        }
        foreach(var key in keys)
        {
            var source=cash.GetValueOrDefault(key);
            // Remove the exact linked bank slice at its ACTUAL source month;
            // post its full actual KZT once at the ORIGINAL request month below.
            // Other confirmed BMS payments retain their existing treatment.
            var effective=checked((source?.SourceCashKzt??0m)-(source?.RepresentedBmsPaidKzt??0m)
                +paidByKey.GetValueOrDefault(key)-originalByKey.GetValueOrDefault(key));
            if(effective<0m)throw new InvalidOperationException("Source cash/original payment attribution drift.");
            values.Add(new("Paid",Guid.Empty,key.CostCenterId,key.CashFlowItemId,key.BudgetMonth,effective,
                bootstrap?"Начальный подтверждённый факт оплат без двойного учёта":"Факт оплат / сторно; замещение резерва без двойного учёта",
                (source?.SnapshotId??state.CashSnapshotId.Value).ToString("D"),JsonSerializer.Serialize(new{snapshotId=source?.SnapshotId??state.CashSnapshotId,
                    currentCashCaptureId=state.CashSnapshotId,grossSha256=source is null||source.SnapshotId==state.CashSnapshotId?state.CashGrossSha256:null,
                    sourceCashKzt=source?.SourceCashKzt??0m,bmsSettlementKzt=paidByKey.GetValueOrDefault(key),representedBmsSettlementKzt=source?.RepresentedBmsPaidKzt??0m},Json)));
        }
        foreach(var group in state.Transfers.Where(t=>activeLinks.Contains(t.LinkId)).GroupBy(t=>(t.RequestId,t.Key)))
        {
            var key=group.Key.Key;
            var proof=JsonSerializer.Serialize(group.OrderBy(t=>t.LinkId).Select(t=>new{t.LinkId,t.AmountKzt,t.OriginalReservePaidKzt,t.SnapshotId,
                evidenceSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(t.Evidence)))}),Json);
            var evidence=JsonSerializer.Serialize(new{requestId=group.Key.RequestId,rule="REQUEST_BUDGET_MONTH",
                linksCount=group.Count(),currentCashCaptureId=state.CashSnapshotId,
                transferProofSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(proof)))},Json);
            if(evidence.Length>4000)throw new InvalidOperationException("Request bank evidence bound exceeded.");
            values.Add(new("Paid",group.Key.RequestId,key.CostCenterId,key.CashFlowItemId,key.BudgetMonth,group.Sum(t=>t.AmountKzt),
                "Банковская оплата заявки — месяц заявки; замещение резерва",state.CashSnapshotId.Value.ToString("D"),evidence));
        }
        var previousValues=state.Values.ToDictionary(v=>(v.Kind,v.SourceId,v.Key));
        for(var i=0;i<values.Count;i++)
            values[i]=BudgetRegisterEvidence350.PreserveEquivalent(previousValues.GetValueOrDefault((values[i].Kind,values[i].SourceId,values[i].Key)),values[i]);
        var frame=await new BudgetExecutionFrameReader343(db).ReadAsync(null,null,false,token,state with{Values=values});
        var frozen=await CaptureApprovedDecisionsAsync(state,requests,bootstrap,frame,token);
        var posted=await storage.PostAsync(values,frozen,Guid.NewGuid(),token);
        var captured=await new BudgetExecutionFrameReader343(db).ReadAsync(null,null,false,token);
        if(await new BudgetExecutionRoutes343(db).ReconcileAsync(captured,bootstrap,token)>0)
        {
            var follow=await SynchronizeAsync(false,token,pass+1);
            return(posted.Movements+follow.Movements,posted.Frozen+follow.Frozen);
        }
        return posted;
    }

    internal static bool Consumes(ExpenseRequestStatus status) => status is not(ExpenseRequestStatus.Draft or ExpenseRequestStatus.Returned or ExpenseRequestStatus.Cancelled
        or ExpenseRequestStatus.Rejected or ExpenseRequestStatus.Paid or ExpenseRequestStatus.Closed);

    private static string ReleaseName(ExpenseRequestStatus status)=>status switch
    {
        ExpenseRequestStatus.Returned=>"Возврат заявки — освобождение лимита",
        ExpenseRequestStatus.Cancelled or ExpenseRequestStatus.Rejected=>"Отмена / отклонение заявки — освобождение лимита",
        ExpenseRequestStatus.Paid or ExpenseRequestStatus.Closed=>"Оплата заявки — замещение резерва подтверждённым фактом",
        ExpenseRequestStatus.Draft=>"Черновик — лимит не занят",
        _=>"Занятие / перераспределение неоплаченного остатка заявки"
    };

    private async Task<IReadOnlyList<RegisterFrozenValue343>> CaptureApprovedDecisionsAsync(RegisterSnapshot343 state,
        ExpenseRequest[] requests,bool bootstrap,RegisterFrame343 frame,CancellationToken token)
    {
        var current=requests.Where(r=>bootstrap||r.Status is ExpenseRequestStatus.Approved or ExpenseRequestStatus.ToPay
            or ExpenseRequestStatus.PartiallyPaid or ExpenseRequestStatus.Paid or ExpenseRequestStatus.Closed).ToDictionary(r=>r.Id);
        var known=state.Frozen.Select(f=>(f.RequestId,f.FinancialFingerprint)).ToHashSet();
        var fingerprints=current.ToDictionary(p=>p.Key,p=>frame.Fingerprints[p.Key]);
        var ids=current.Keys.Select(id=>id.ToString("D")).ToArray();
        var evidence=await db.BusinessAuditEntries.AsNoTracking().Where(a=>a.ObjectType=="ExpenseRequest"&&ids.Contains(a.ObjectId)
                &&(a.Action=="WorkflowApproved"||a.Action=="WorkflowNsiConfirmed"||a.Action=="SubmittedApproved"||a.Action=="SubmittedToPayment"||a.Action=="BudgetRouteRecalculated"))
            .OrderBy(a=>a.OccurredAtUtc).ThenBy(a=>a.Id).Take(100001).ToArrayAsync(token);
        if(evidence.Length>100000)throw new InvalidOperationException("Approved decision evidence bound exceeded.");
        var requestIds=current.Keys.ToArray();
        var snapshots=await db.ApprovalRouteSnapshots.AsNoTracking().Where(s=>requestIds.Contains(s.ExpenseRequestId))
            .OrderBy(s=>s.SnapshotNumber).Take(100001).ToArrayAsync(token);
        if(snapshots.Length>100000)throw new InvalidOperationException("Approval snapshot evidence bound exceeded.");
        var result=new List<RegisterFrozenValue343>();
        foreach(var audit in evidence)
        {
            if(!Guid.TryParse(audit.ObjectId,out var id)||!current.TryGetValue(id,out var request))continue;
            var approved=BudgetFinancialFingerprint343.FromApprovedAudit(audit.AfterJson);
            var episode=frame.Episodes[id];var scoped=episode.Fingerprint(approved?.Fingerprint);
            if(approved is null||scoped is null||(episode.ReturnedAtUtc is{} returned&&audit.OccurredAtUtc<=returned)
                ||scoped!=fingerprints[id]||known.Contains((id,scoped)))continue;
            var snapshot=snapshots.Where(s=>s.ExpenseRequestId==id&&s.CapturedAtUtc<=audit.OccurredAtUtc).MaxBy(s=>s.SnapshotNumber)
                ??throw new InvalidOperationException("Approved financial snapshot unavailable; do not invent a historical decision.");
            var decision=frame.Evaluation.Decisions.GetValueOrDefault(id);
            var deficit=bootstrap?snapshot.PositiveDeficitKzt:decision?.PositiveDeficitKzt
                ??throw new InvalidOperationException("Approval-time FIFO decision unavailable.");
            var keys=frame.Inputs.SingleOrDefault(r=>r.Id==id)?.RemainingReservations.Keys.ToArray()??[];
            var lines=bootstrap
                ?keys.Length==1?keys.Select(k=>new RegisterFrozenPosition343(k.CostCenterId,k.CashFlowItemId,k.BudgetMonth,deficit)).ToArray()
                    :deficit==0m?keys.Select(k=>new RegisterFrozenPosition343(k.CostCenterId,k.CashFlowItemId,k.BudgetMonth,0m)).ToArray():[]
                :(decision??throw new InvalidOperationException("Approval FIFO lines missing.")).Lines.Select(l=>new RegisterFrozenPosition343(l.Key.CostCenterId,l.Key.CashFlowItemId,l.Key.BudgetMonth,Math.Max(0m,-l.AvailableAfterKzt))).ToArray();
            result.Add(new(id,scoped,audit.OccurredAtUtc,deficit,snapshot.SnapshotNumber,
                JsonSerializer.Serialize(new{auditId=audit.Id,action=audit.Action,requestNumber=request.RequestNumber,snapshotNumber=snapshot.SnapshotNumber,approvedStatus=approved.Value.Status,
                    legacyDecision=bootstrap,registerRevision=state.Revision,returnAuditId=episode.ReturnAuditId,queueAtUtc=episode.QueueAtUtc},Json),JsonSerializer.Serialize(lines,Json)));
            known.Add((id,scoped));
        }
        if(current.Values.Any(r=>r.Status is ExpenseRequestStatus.Approved or ExpenseRequestStatus.ToPay or ExpenseRequestStatus.PartiallyPaid or ExpenseRequestStatus.Paid or ExpenseRequestStatus.Closed
                &&(fingerprints[r.Id] is not{} hash||!known.Contains((r.Id,hash)))))
            throw new InvalidOperationException("An approved request lacks verified immutable budget-decision evidence.");
        return result;
    }
}
