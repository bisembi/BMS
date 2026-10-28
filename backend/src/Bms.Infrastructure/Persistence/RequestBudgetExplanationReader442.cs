using Bms.Application.Abstractions;
using Bms.Application.Budgeting;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class RequestBudgetExplanationReader442(BmsDbContext db,ICurrentActor actor,
    IExpenseRequestRepository requests,IBddsDrillStore facts):IRequestBudgetExplanationReader442
{
    public async Task<RequestBudgetExplanation442> ReadAsync(Guid requestId,Guid version,Guid articleId,
        int factSkip,int reserveSkip,long? revision,CancellationToken token)
    {
        var who=ActorIdentity.RequireBusinessDataAccess(actor);
        var request=await requests.FindVisibleAsync(requestId,who,actor.HasFullBusinessDataAccess,token)
            ??throw new ExpenseRequestNotFoundException();
        var scope=await actor.ResolveBusinessDataReadScopeAsync(token);
        if(!scope.Contains(request))throw new ExpenseRequestNotFoundException();
        if(request.ConcurrencyStamp!=version)throw new ExpenseRequestVersionConflictException();
        if(request.PaymentOperation?.IsPayrollAdvance==true||request.DefaultCostCenterId is not{} cfo
            ||request.PlannedPaymentDate is not{} payment||!scope.Contains(cfo)
            ||!request.Lines.Any(l=>l.CashFlowItemId==articleId))throw new ActorForbiddenException();
        if(!db.BudgetRegisterEnabled343)throw new InvalidOperationException("FIFO source unavailable.");
        var articleOwner=await db.CashFlowItems.AsNoTracking().Where(i=>i.Id==articleId&&!i.IsGroup)
            .Select(i=>i.CostCenterId).SingleOrDefaultAsync(token);
        // Raw source drill is article-owned, not an arbitrary cross-CFO report.
        if(articleOwner is not{} owner||!scope.Contains(owner))throw new ActorForbiddenException();
        var key=BudgetKey.Create(cfo,articleId,new(payment.Year,payment.Month,1));
        var frame=await new BudgetExecutionFrameReader343(db).ReadAsync(null,null,false,token,requestedKeys:[key]);
        if(revision.HasValue&&revision!=frame.State.Revision)throw new ExpenseRequestVersionConflictException();
        if(!frame.Evaluation.Decisions.TryGetValue(requestId,out var decision))throw new InvalidOperationException("Request decision missing.");
        var line=decision.Lines.SingleOrDefault(l=>l.Key==key)??throw new InvalidOperationException("Request budget key unavailable.");
        var own=frame.Inputs.Single(i=>i.Id==requestId);
        var submitted=frame.Inputs.Where(i=>(i.Active&&i.Submitted)||i.PaidQueueAmounts.Count>0)
            .OrderBy(i=>i.CreatedAtUtc).ThenBy(i=>i.Id.ToString("N"),StringComparer.Ordinal).ToArray();
        var index=Array.FindIndex(submitted,i=>i.Id==requestId);
        var prefix=index>=0?submitted.Take(index+1).ToArray():submitted.Append(own).ToArray();
        var reserve=prefix.Where(i=>i.Active&&(i.Submitted||i.Id==requestId))
            .Select(i=>(i.Id,Amount:i.RemainingReservations.GetValueOrDefault(key))).Where(i=>i.Amount>0m).ToArray();
        var ownPaid=own.PaidQueueAmounts.GetValueOrDefault(key);
        var ownReserve=own.Active?own.RemainingReservations.GetValueOrDefault(key):0m;
        var opening=frame.Openings.Single(i=>i.Key==key);
        RequestBudgetExplanationMath442.Validate(opening.LimitAmountKzt,line,ownPaid,ownReserve,reserve.Sum(i=>i.Amount));
        var laterPaid=checked(opening.PaidAmountKzt-line.PaidBeforeKzt-ownPaid);
        if(laterPaid<0m)throw new InvalidOperationException("Paid queue source drift.");
        await CompanyBudgetPlanReader.AcquireLockAsync(db,false,token);
        var plan=await CompanyBudgetPlanReader.ReadAsync(db,null,[cfo],token);
        var winner=plan.Instances.Where(i=>i.ValidFrom<=key.BudgetMonth&&i.ValidTo>=key.BudgetMonth)
            .OrderByDescending(i=>i.ApprovalOrdinal).ThenByDescending(i=>i.ApprovedAtUtc).ThenByDescending(i=>i.Id).FirstOrDefault();
        var limit=BudgetEffectiveLimitResolver.Resolve(null,cfo,articleId,key.BudgetMonth,plan.Instances.Select(BudgetRepository.ToEffectiveVersion).ToArray());
        if(limit!=opening.LimitAmountKzt)throw new InvalidOperationException("Published instance and register limit drift.");
        var byId=frame.Requests.ToDictionary(i=>i.Id);
        var visible=reserve.Where(i=>scope.Contains(byId[i.Id])).ToArray();
        var ids=visible.Select(i=>byId[i.Id].CounterpartyId).Where(i=>i.HasValue).Select(i=>i!.Value).Distinct().ToArray();
        var counterparties=await db.Counterparties.AsNoTracking().Where(i=>ids.Contains(i.Id)).ToDictionaryAsync(i=>i.Id,i=>i.Name,token);
        var rows=visible.Skip(reserveSkip).Take(50).Select(i=>{
            var r=byId[i.Id];return new RequestBudgetReserve442(r.Id,r.RequestNumber,
                r.PlannedPaymentDate??throw new InvalidOperationException("Reservation payment date missing."),
                r.CounterpartyId is{} cp?counterparties.GetValueOrDefault(cp):r.CandidateCounterpartyName,i.Amount,r.Id==requestId,r.Comment);
        }).ToArray();
        if(frame.State.CashSnapshotId is not{} snapshot||!AccountingPeriod467.IsMonth(key.BudgetMonth))throw new BddsDrillUnavailableException();
        var drill=await facts.ReadAsync(new(key.BudgetMonth.Year,key.BudgetMonth,key.BudgetMonth,snapshot,cfo,null,[articleId],factSkip,50,"entryDateLocal","asc"),token);
        if(drill.SnapshotId!=snapshot||drill.Total>100000||drill.Items.Count>50||drill.Total<drill.Items.Count
            ||drill.Items.Any(i=>i.LineNo<1||i.EntryDateLocal.Year!=key.BudgetMonth.Year||i.EntryDateLocal.Month!=key.BudgetMonth.Month))
            throw new BddsDrillControlException();
        return new(requestId,version,articleId,cfo,key.BudgetMonth,frame.State.Revision,snapshot,winner?.Number,
            limit,checked(line.PaidBeforeKzt+ownPaid),line.ReservedBeforeKzt,ownReserve,ownPaid,
            line.AvailableBeforeKzt,line.AvailableAfterKzt,laterPaid,rows,visible.Length,reserveSkip,
            checked(reserve.Sum(i=>i.Amount)-visible.Sum(i=>i.Amount)),drill);
    }
}
