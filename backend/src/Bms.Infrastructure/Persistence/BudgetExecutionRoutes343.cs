using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.ExpenseRequests;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>Reconcile only unexecuted current/future-month finance routes.
/// Old snapshots/decisions remain append-only. The caller's own changed
/// requests are excluded so a committed command/idempotency response cannot
/// be silently replaced after it has been built.</summary>
internal sealed class BudgetExecutionRoutes343(BmsDbContext db)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public async Task<int> ReconcileAsync(RegisterFrame343 frame,bool migration,CancellationToken token)
    {
        var local=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5));var month=new DateOnly(local.Year,local.Month,1);
        var count=0;var snapshots=new ApprovalRouteSnapshotStore(db);
        var resolver=new ApprovalRouteParticipantResolver(new ApprovalRouteParticipantReader(db));
        var candidates = frame.Requests.Where(r=>r.Status is ExpenseRequestStatus.InApproval or ExpenseRequestStatus.AwaitingNsi)
                    .Where(r=>r.EffectiveBudgetMonth is{} m&&(migration?m==month:m>=month))
                    .Where(r=>!db.BudgetRegisterTouchedRequests343.Contains(r.Id))
                    .Where(r=>frame.Evaluation.Decisions.ContainsKey(r.Id)).OrderBy(r=>r.Id).ToArray();
        var captured = await ReadSnapshotPairs421Async(snapshots,
            candidates.ToDictionary(r=>r.Id,r=>r.ActiveApprovalRouteSnapshotNumber),token);
        foreach(var saved in candidates)
        {
            if(!frame.Evaluation.Decisions.TryGetValue(saved.Id,out var decision))continue;
            var old = captured.GetValueOrDefault(saved.Id);
            // Only the existing closed 251 definitions are automatically
            // reconciled. Unknown/legacy definitions are not invented or
            // silently converted to a different author's route policy.
            if(old is null||old.WorkflowDefinitionVersion is <6 or >13)continue;
            var desired=ApprovalRoutePlanner.Plan(decision.PositiveDeficitKzt,old.WorkflowDefinitionVersion);
            if(desired.Select(s=>s.RoleCode).SequenceEqual(old.Stages.Select(s=>s.RoleCode),StringComparer.Ordinal))continue;
            var prefix=Math.Min(desired.Count,old.Stages.Count);
            if(Enumerable.Range(0,prefix).Any(i=>desired[i].RoleCode!=old.Stages[i].RoleCode))
                throw new InvalidOperationException("FIFO route change is not a preserved approval prefix.");
            var request=await db.ExpenseRequests.Include(r=>r.Lines).SingleAsync(r=>r.Id==saved.Id,token);
            if(request.ConcurrencyStamp!=saved.ConcurrencyStamp||request.Status!=saved.Status)
                throw new InvalidOperationException("FIFO route source changed during the captured transaction.");
            var authors=await db.EmployeeActorMappings.AsNoTracking().Where(m=>m.ActorExternalId==request.InitiatorExternalId&&m.IsActive)
                .Select(m=>m.EmployeeId).Take(2).ToArrayAsync(token);
            if(authors.Length!=1)throw new ApprovalRouteParticipantResolutionUnavailableException();
            var finance=desired.Where(s=>s.RoleCode!="BUDGET_OWNER").Select((s,i)=>new ApprovalRouteStage(i+1,s.RoleCode)).ToArray();
            var byRole=new Dictionary<string,IReadOnlyList<Guid>>(StringComparer.Ordinal);
            if(desired.Count>old.Stages.Count)
            {
                var resolved=finance.Length==1&&finance[0].RoleCode=="GENERAL_DIRECTOR"
                    ?await resolver.ResolveGeneralDirectorAsync(token):await resolver.ResolveConfiguredAsync(finance,token);
                for(var i=0;i<finance.Length;i++)byRole[finance[i].RoleCode]=resolved[i].EligibleEmployeeIds.Where(id=>id!=authors[0]).ToArray();
            }
            var participants=desired.Select(s=>new ApprovalRouteSnapshotStageParticipants(s.StageNumber,
                s.StageNumber<=old.Stages.Count?old.Stages[s.StageNumber-1].EligibleEmployeeIds
                    :byRole.GetValueOrDefault(s.RoleCode)??[])).ToArray();
            if(participants.Any(p=>p.EligibleEmployeeIds.Count()==0))throw new ApprovalRouteParticipantResolutionUnavailableException();
            var now=DateTimeOffset.UtcNow;var sequence=await snapshots.NextSnapshotNumberAsync(request.Id,token);
            var next=ApprovalRouteSnapshot.Create(request.Id,sequence,old.WorkflowDefinitionVersion,old.OrganizationId,old.CostCenterId,
                decision.PositiveDeficitKzt,now,participants);
            // Every preserved prefix stage strictly before the current active
            // stage was already approved. No new participant is approved here.
            var completed=request.Status==ExpenseRequestStatus.AwaitingNsi?old.Stages.Count:request.ActiveApprovalRouteStageNumber-1;
            var complete=completed>=desired.Count;
            var target=complete?(request.IsNewCounterparty||request.IsNewContract?ExpenseRequestStatus.AwaitingNsi:ExpenseRequestStatus.Approved)
                :ExpenseRequestStatus.InApproval;
            var projections=await db.BudgetRequestProjections.AsNoTracking().Where(p=>p.ExpenseRequestId==request.Id).ToArrayAsync(token);
            var before=ExpenseRequestDto.FromDomain(request,projections);
            snapshots.Add(next);await snapshots.CaptureCompanyBudgetBaselineAsync(request.Id,sequence,token);
            request.ApplyWorkflowTransition(request.ConcurrencyStamp,now,request.Status,target,complete?null:sequence,complete?0:completed+1);
            var after=ExpenseRequestDto.FromDomain(request,projections);
            db.BusinessAuditEntries.Add(new(){Id=Guid.NewGuid(),OccurredAtUtc=now,ActorExternalId="BMS:BudgetExecution343",
                ObjectType="ExpenseRequest",ObjectId=request.Id.ToString("D"),Action="BudgetRouteRecalculated",
                BeforeJson=JsonSerializer.Serialize(before,Json),AfterJson=JsonSerializer.Serialize(new{commandCode="RECALCULATE_BUDGET_ROUTE",
                    reason=$"FIFO343: маршрут {old.SnapshotNumber} → {sequence}; прежние решения сохранены, новые этапы не согласованы автоматически. "
                        +(decision.Frozen?"Восстановлен неизменённый ранее утверждённый бюджетный признак.":"Расчёт по очереди создания в едином регистре."),expenseRequest=after},Json),CorrelationId=Guid.NewGuid().ToString("N")});
            count++;
        }
        if(count>0)await db.SaveChangesAsync(token);
        return count;
    }

    internal static async Task<IReadOnlyDictionary<Guid, ApprovalRouteSnapshot>> ReadSnapshotPairs421Async(
        IApprovalRouteSnapshotStore snapshots, IReadOnlyDictionary<Guid, int?> numbers, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (numbers.Count > 20000 || numbers.Any(pair => pair.Key == Guid.Empty || pair.Value is <= 0))
            throw new InvalidOperationException("Invalid bounded route capture.");
        var result = new Dictionary<Guid, ApprovalRouteSnapshot>();
        foreach (var batch in numbers.Chunk(100))
        {
            token.ThrowIfCancellationRequested();
            var active = batch.Where(pair => pair.Value.HasValue).ToDictionary(pair => pair.Key, pair => pair.Value.GetValueOrDefault());
            var latest = batch.Where(pair => !pair.Value.HasValue).Select(pair => pair.Key).ToArray();
            var exactRows = await snapshots.FindActiveAsync(active, token);
            var latestRows = await snapshots.FindLatestMany421Async(latest, token);
            foreach (var pair in exactRows)
            {
                if (!active.TryGetValue(pair.Key, out var number) || pair.Value.ExpenseRequestId != pair.Key
                    || pair.Value.SnapshotNumber != number || !result.TryAdd(pair.Key, pair.Value))
                    throw new InvalidOperationException("Active route capture is inconsistent.");
            }
            foreach (var pair in latestRows)
            {
                if (!latest.Contains(pair.Key) || pair.Value.ExpenseRequestId != pair.Key || !result.TryAdd(pair.Key, pair.Value))
                    throw new InvalidOperationException("Latest route capture is inconsistent.");
            }
        }
        return result;
    }
}
