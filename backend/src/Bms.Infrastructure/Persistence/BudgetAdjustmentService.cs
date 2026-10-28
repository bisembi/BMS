using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Budgeting;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Bms.Infrastructure.Persistence;

/// Adjustment commands take the same global company-plan Exclusive lock as
/// activation. Every reservation/fact admission holds Shared first: validation
/// and GD publication cannot race an expense submission or another adjustment.
public sealed class BudgetAdjustmentService(BmsDbContext db, ICurrentActor actor,
    IBusinessTransactionRunner transactions, BudgetDocumentAuthority editors, CfoWorkflowAuthority roles,
    ApprovalRouteParticipantResolver participants, IAccessScope access, IBudgetDirectoryStore directory,
    IBudgetRepository budgets, IIdempotencyStore idempotency, IBusinessAuditSink audit, IClock clock,
    IConfiguration configuration, IDirectDdsReplicaReader? directSource = null, AccountingReplicaLocation? replicaLocation = null)
{
    private static readonly JsonSerializerOptions Json = BudgetAdjustmentProjection.Json;
    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json,Json)
        ?? throw new InvalidOperationException("Stored budget adjustment is unavailable.");
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Json)));
    private static BudgetAdjustmentTransfer[] Moves(BudgetAdjustmentEntity row) => Read<BudgetAdjustmentTransfer[]>(row.TransfersJson);
    private static BudgetAdjustmentStage[] Stages(BudgetAdjustmentEntity row) => Read<BudgetAdjustmentStage[]>(row.StagesJson);
    private static BudgetAdjustmentDecision[] Decisions(BudgetAdjustmentEntity row) => Read<BudgetAdjustmentDecision[]>(row.DecisionsJson);
    private static string? StageRole(BudgetAdjustmentEntity row) =>
        row.State=="PendingFd"?"FIN_DIRECTOR":row.State=="PendingGd"?"GENERAL_DIRECTOR":null;
    private static void Expected(BudgetAdjustmentEntity row, Guid stamp)
    {
        if(stamp==Guid.Empty||stamp!=row.Stamp)throw new BudgetInstanceConcurrencyConflictException();
    }
    private async Task<BudgetInstance> ParentAsync(Guid id, CancellationToken token)
    {
        ActorIdentity.RequireBusinessDataAccess(actor);
        _=await roles.RequireEmployeeAsync(actor,token);
        var scope=await actor.ResolveBusinessDataReadScopeAsync(token);
        var original=await db.BudgetInstances.AsNoTracking().Include(b=>b.Lines).SingleOrDefaultAsync(b=>b.Id==id,token);
        if(original is null||!scope.Contains(original.CostCenterId))throw new BudgetInstanceNotFoundException();
        if(original.Status!=BudgetInstanceStatus.Approved)
            throw new BudgetAdjustmentRuleException("Корректировать можно только утверждённый экземпляр бюджета.");
        if(!await directory.LockCostCenterAsync(original.CostCenterId,token))throw new BudgetInstanceNotFoundException();
        return (await BudgetAdjustmentProjection.ApplyAsync(db,[original],token))[0];
    }
    private async Task<bool> EditorAsync(BudgetInstance b,CancellationToken token)
    {
        if(actor.IsBmsAdministrator)return false;
        var editor=await editors.ResolveEditorAsync(actor,b.CostCenterId,token);
        if(!editor.CanEditBudgetDocument)return false;
        if(editor.HasFullBudgetAccess)return true;
        var external=ActorIdentity.RequireBusinessDataAccess(actor);
        return await access.CanUseAsync(external,null,[b.CostCenterId],DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),token)
            &&await access.CanUseAsync(external,null,[b.CostCenterId],b.ValidFrom,token);
    }
    private async Task<BudgetAdjustmentUsage[]> UsageAsync(BudgetInstance b,CancellationToken token)
    {
        var keys=b.Lines.Where(l=>l.FlowDirection==BudgetFlowDirection.Payment)
            .Select(l=>BudgetKey.Create(b.CostCenterId,l.CashFlowItemId,l.BudgetMonth)).Distinct().ToArray();
        var positions=new List<BudgetPosition>();
        // Approval belongs to the parent instance, not a company version.
        // Recheck real fact and reservations without resolving published limits.
        foreach(var chunk in keys.Chunk(400))positions.AddRange(await budgets.ListConsumptionPositionsAsync(chunk,token));
        if(positions.Count!=keys.Length||positions.Select(p=>p.Key).Distinct().Count()!=keys.Length
            ||positions.Any(p=>!keys.Contains(p.Key)))throw new BudgetAdjustmentRuleException("Снимок факта и резервов неполон. Повторите обновление.");
        var articleIds=keys.Select(k=>k.CashFlowItemId).Distinct().ToArray();
        var months=keys.Select(k=>k.BudgetMonth).Distinct().ToArray();
        var reservationSource=db.BudgetRequestProjections.AsNoTracking();
        if(db.BudgetRegisterEnabled343)
            reservationSource=reservationSource.Where(p=>db.ExpenseRequests.Any(r=>r.Id==p.ExpenseRequestId
                &&new[]{Bms.Domain.ExpenseRequests.ExpenseRequestStatus.InApproval,Bms.Domain.ExpenseRequests.ExpenseRequestStatus.AwaitingNsi,
                    Bms.Domain.ExpenseRequests.ExpenseRequestStatus.Approved,Bms.Domain.ExpenseRequests.ExpenseRequestStatus.ToPay,
                    Bms.Domain.ExpenseRequests.ExpenseRequestStatus.PartiallyPaid}.Contains(r.Status)));
        var reserved=await reservationSource.Where(p=>p.CostCenterId==b.CostCenterId
            &&articleIds.Contains(p.CashFlowItemId)&&months.Contains(p.BudgetMonth))
            .GroupBy(p=>new{p.CashFlowItemId,p.BudgetMonth})
            .Select(g=>new{g.Key.CashFlowItemId,g.Key.BudgetMonth,Amount=g.Sum(p=>p.ReservedAmountKzt)}).ToArrayAsync(token);
        if(positions.Any(p=>p.ReservedAmountKzt!=(reserved.SingleOrDefault(r=>r.CashFlowItemId==p.CashFlowItemId&&r.BudgetMonth==p.BudgetMonth)?.Amount??0m)))
            throw new BudgetAdjustmentRuleException("Агрегат резервов не совпадает с заявками. Перед переносом требуется сверка финансового контролёра.");
        var result=positions.Select(p=>new BudgetAdjustmentUsage(p.CashFlowItemId,p.BudgetMonth,p.ReservedAmountKzt,p.PaidAmountKzt)).ToList();
        var incoming=b.Lines.Where(l=>l.FlowDirection==BudgetFlowDirection.Receipt).Select(l=>(l.CashFlowItemId,l.BudgetMonth)).Distinct().ToArray();
        if(incoming.Length>0)
        {
            if(directSource is null||incoming.Any(k=>!AccountingPeriod467.IsMonth(k.BudgetMonth)))
                throw new BudgetAdjustmentRuleException("Для переноса поступлений требуется подтверждённый актуальный факт 1С за год бюджета.");
            await AccountingPaymentCaptureLock.AcquireAsync(db,false,token,replicaLocation);
            var fact=await CurrentDdsActualsSource.ReadRequiredAsync(directSource,token);
            var now=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5));
            var expected=new DateTimeOffset(now.Date.AddHours(7),TimeSpan.FromHours(5));if(now<expected)expected=expected.AddDays(-1);
            if(fact.CapturedAtUtc<expected)throw new CashFlowActualsNotAvailableException();
            foreach(var key in incoming)
            {
                // The validated complete capture proves zero for a key without
                // movements; never net receipts against outgoing cash.
                var actual=fact.Cells.SingleOrDefault(c=>c.CashFlowItemId==key.CashFlowItemId&&c.BudgetMonth==key.BudgetMonth)?.ReceiptsAmountKzt??0m;
                result.Add(new(key.CashFlowItemId,key.BudgetMonth,0m,actual,BudgetFlowDirection.Receipt));
            }
        }
        return result.ToArray();
    }
    private async Task ValidateAsync(BudgetInstance b,IReadOnlyCollection<BudgetAdjustmentTransfer> moves,bool consumption,CancellationToken token)
    {
        _=BudgetAdjustmentPlan.Apply(b.ValidFrom,b.ValidTo,BudgetAdjustmentPlan.Cells(b),moves,
            consumption?await UsageAsync(b,token):null);
        var ids=moves.SelectMany(m=>new[]{m.SourceArticleId,m.TargetArticleId}).Distinct().ToArray();
        var items=await db.CashFlowItems.AsNoTracking().Where(i=>ids.Contains(i.Id)).ToArrayAsync(token);
        foreach(var m in moves)
        {
            var source=items.SingleOrDefault(i=>i.Id==m.SourceArticleId);
            var target=items.SingleOrDefault(i=>i.Id==m.TargetArticleId);
            if(source is null||target is null||source.IsGroup||target.IsGroup||!target.IsActive
                ||target.CostCenterId!=b.CostCenterId||source.CostCenterId!=b.CostCenterId)
                throw new BudgetAdjustmentRuleException("Статьи должны принадлежать ЦФО исходного экземпляра; получатель — действующая статья, не группа.");
            var direction=m.FlowDirection.ToString();
            if((source.FlowDirection is not null&&source.FlowDirection!=direction)
                ||(target.FlowDirection is not null&&target.FlowDirection!=direction))
                throw new BudgetAdjustmentRuleException("Нельзя переносить бюджет между поступлениями и выплатами.");
        }
    }
    private async Task<bool> StageAsync(BudgetAdjustmentEntity row,Guid employee,bool stop,CancellationToken token)
    {
        var code=StageRole(row);if(code is null)return false;
        if(stop&&code=="FIN_DIRECTOR")
        {
            try{await roles.RequireStageAsync(employee,row.CostCenterId,"FIN_CONTROLLER",token);return true;}
            catch(ActorForbiddenException){}
        }
        if(!Stages(row).Any(s=>s.RoleCode==code&&s.EmployeeIds.Contains(employee)))return false;
        try{await roles.RequireStageAsync(employee,row.CostCenterId,code,token);return true;}
        catch(ActorForbiddenException){return false;}
    }
    private async Task<BudgetAdjustmentItem> ItemAsync(BudgetAdjustmentEntity row,BudgetInstance parent,CancellationToken token)
    {
        var center=await db.CostCenters.AsNoTracking().Where(c=>c.Id==row.CostCenterId).Select(c=>c.Name).SingleAsync(token);
        var name=await db.Employees.AsNoTracking().Where(e=>e.Id==row.AuthorEmployeeId).Select(e=>e.Name).SingleAsync(token);
        return new(row.Id,row.Number,row.BaseBudgetId,parent.Number,row.CostCenterId,center,row.State,row.Stamp,
            Moves(row).Sum(m=>m.AmountKzt),name,row.CreatedAtUtc,row.ApprovedAtUtc);
    }
    private async Task<BudgetAdjustmentDto> DtoAsync(BudgetAdjustmentEntity row,BudgetInstance parent,CancellationToken token)
    {
        var employee=await roles.RequireEmployeeAsync(actor,token);
        var write=configuration.GetValue<bool>("Bms:BudgetAdjustments:WriteEnabled");
        var edit=write&&(row.State is "Draft" or "Returned")&&await EditorAsync(parent,token);
        var versions=await (from m in db.CompanyBudgetVersionInstances.AsNoTracking()
            join v in db.CompanyBudgetVersions.AsNoTracking() on m.VersionId equals v.Id
            where m.BudgetInstanceId==parent.Id orderby v.Name select v.Name).ToArrayAsync(token);
        var people=Stages(row).SelectMany(s=>s.EmployeeIds).Distinct().ToArray();
        var names=await db.Employees.AsNoTracking().Where(e=>people.Contains(e.Id)).ToDictionaryAsync(e=>e.Id,e=>e.Name,token);
        return new(await ItemAsync(row,parent,token),row.Reason,parent.ValidFrom,parent.ValidTo,Moves(row),
            BudgetAdjustmentPlan.Cells(parent),await UsageAsync(parent,token),Decisions(row),versions,edit,edit,
            write&&await StageAsync(row,employee,false,token),write&&await StageAsync(row,employee,true,token),
            Stages(row).OrderBy(s=>s.Number).Select(s=>(s.RoleCode=="FIN_DIRECTOR"?"Финансовый директор: ":"Генеральный директор: ")
                +string.Join(", ",s.EmployeeIds.Select(id=>names.GetValueOrDefault(id,"Сотрудник не найден")))).ToArray());
    }
    public Task<BudgetAdjustmentBasis> BasisAsync(Guid baseId,CancellationToken token)=>
        transactions.ExecuteAsync(async ct=>{
            var b=await ParentAsync(baseId,ct);
            var ids=b.Lines.Select(l=>l.CashFlowItemId).Distinct().ToArray();
            var articles=await db.CashFlowItems.AsNoTracking().Where(i=>!i.IsGroup
                &&((i.IsActive&&i.CostCenterId==b.CostCenterId)||ids.Contains(i.Id))).OrderBy(i=>i.Code)
                .Select(i=>new BudgetAdjustmentArticle(i.Id,i.Code,i.Name,i.FlowDirection,i.IsActive&&i.CostCenterId==b.CostCenterId)).ToArrayAsync(ct);
            return new BudgetAdjustmentBasis(b.Id,b.Number,b.CostCenterId,b.ValidFrom,b.ValidTo,BudgetAdjustmentPlan.Cells(b),
                await UsageAsync(b,ct),configuration.GetValue<bool>("Bms:BudgetAdjustments:WriteEnabled")&&await EditorAsync(b,ct),articles);
        },token);
    public Task<BudgetAdjustmentDto> GetAsync(Guid id,CancellationToken token)=>
        transactions.ExecuteAsync(async ct=>{
            var row=await db.BudgetAdjustments.AsNoTracking().SingleOrDefaultAsync(a=>a.Id==id,ct)??throw new BudgetInstanceNotFoundException();
            return await DtoAsync(row,await ParentAsync(row.BaseBudgetId,ct),ct);
        },token);
    public Task<BudgetAdjustmentPage> ListAsync(Guid? baseId,int skip,int take,CancellationToken token)=>
        transactions.ExecuteAsync(async ct=>{
            ActorIdentity.RequireBusinessDataAccess(actor);_=await roles.RequireEmployeeAsync(actor,ct);
            if(skip<0||take is <1 or >100||baseId==Guid.Empty)throw new ArgumentException("Invalid adjustment filter.");
            var scope=await actor.ResolveBusinessDataReadScopeAsync(ct);
            var query=db.BudgetAdjustments.AsNoTracking().Where(a=>!baseId.HasValue||a.BaseBudgetId==baseId.Value);
            if(!scope.CanReadAll)query=query.Where(a=>scope.CostCenterIds.Contains(a.CostCenterId));
            var total=await query.CountAsync(ct);
            var page=await query.OrderByDescending(a=>a.CreatedAtUtc).ThenBy(a=>a.Id).Skip(skip).Take(take).ToArrayAsync(ct);
            var items=new List<BudgetAdjustmentItem>();
            foreach(var row in page)
            {
                // No per-CFO lock acquisition here: list never writes and all data is
                // already inside the caller's Shared company-plan transaction.
                var b=await db.BudgetInstances.AsNoTracking().SingleAsync(b=>b.Id==row.BaseBudgetId,ct);
                items.Add(await ItemAsync(row,b,ct));
            }
            return new BudgetAdjustmentPage(items,skip,take,total);
        },token);
    public Task<BudgetAdjustmentDto> SaveAsync(Guid? id,SaveBudgetAdjustmentCommand command,string key,CancellationToken token)=>
        MutateAsync(id,"save",command,key,async (row,b,employee,ct)=>{
            if(!await EditorAsync(b,ct))throw new ActorForbiddenException();
            if(row.State is not("Draft" or "Returned"))throw new BudgetAdjustmentRuleException("Редактирование доступно только в черновике или после возврата.");
            if(id.HasValue)Expected(row,command.ExpectedStamp??Guid.Empty);
            else if(command.ExpectedStamp.HasValue)throw new BudgetInstanceConcurrencyConflictException();
            if(string.IsNullOrWhiteSpace(command.Reason)||command.Reason.Length>2000||command.Reason.Any(c=>char.IsControl(c)&&c is not('\r' or '\n' or '\t')))
                throw new BudgetAdjustmentRuleException("Укажите основание корректировки (не более 2000 символов).");
            await ValidateAsync(b,command.Transfers,false,ct);
            row.Reason=command.Reason.Trim();row.TransfersJson=JsonSerializer.Serialize(command.Transfers,Json);
        },command.BaseBudgetId,token);
    public Task<BudgetAdjustmentDto> ActAsync(Guid id,string action,BudgetAdjustmentActionCommand command,string key,CancellationToken token)=>
        MutateAsync(id,action,command,key,async (row,b,employee,ct)=>{
            Expected(row,command.ExpectedStamp);
            if(action=="submit")
            {
                if(row.State is not("Draft" or "Returned")||!await EditorAsync(b,ct))throw new ActorForbiddenException();
                await ValidateAsync(b,Moves(row),true,ct);
                var route=BudgetApprovalRoutePlanner.Plan(0m);
                var resolved=await participants.ResolveBudgetAsync(route,ct);
                row.StagesJson=JsonSerializer.Serialize(resolved.Select(s=>new BudgetAdjustmentStage(s.StageNumber,
                    route[s.StageNumber-1].RoleCode,s.EligibleEmployeeIds.ToArray())),Json);
                row.State="PendingFd";row.SubmissionNo++;row.BasisHash=Hash(BudgetAdjustmentPlan.Cells(b));
                return;
            }
            if(action is not("approve" or "return" or "reject"))throw new ArgumentException("Unknown adjustment action.");
            var stop=action!="approve";if(!await StageAsync(row,employee,stop,ct))throw new ActorForbiddenException();
            var comment=command.Comment?.Trim();
            if((stop&&string.IsNullOrWhiteSpace(comment))||comment?.Length>2000||comment?.Any(c=>char.IsControl(c)&&c is not('\r' or '\n' or '\t'))==true)
                throw new BudgetAdjustmentRuleException("Для возврата или отклонения укажите причину (до 2000 символов).");
            var stage=StageRole(row)??throw new ActorForbiddenException();
            if(!stop)
            {
                if(row.BasisHash!=Hash(BudgetAdjustmentPlan.Cells(b)))
                    throw new BudgetAdjustmentRuleException("Экземпляр уже изменён другой утверждённой корректировкой. Верните документ на доработку и подайте заново.");
                // Recheck both directors; GD is the sole publication boundary.
                await ValidateAsync(b,Moves(row),true,ct);
            }
            string? acting=null;
            if(stop&&stage=="FIN_DIRECTOR")
            {
                try{await roles.RequireStageAsync(employee,row.CostCenterId,"FIN_CONTROLLER",ct);acting="FIN_CONTROLLER";}
                catch(ActorForbiddenException){}
            }
            var name=await db.Employees.AsNoTracking().Where(e=>e.Id==employee).Select(e=>e.Name).SingleAsync(ct);
            row.DecisionsJson=JsonSerializer.Serialize(Decisions(row).Append(new BudgetAdjustmentDecision(row.SubmissionNo,
                stage=="FIN_DIRECTOR"?1:2,stage,employee,name,action,clock.UtcNow,comment,acting)),Json);
            if(action=="return")row.State="Returned";
            else if(action=="reject")row.State="Rejected";
            else if(stage=="FIN_DIRECTOR")row.State="PendingGd";
            else{row.State="Approved";row.ApprovedAtUtc=clock.UtcNow;row.ApprovedByEmployeeId=employee;
                row.ApprovalOrder=(await db.BudgetAdjustments.Where(a=>a.BaseBudgetId==row.BaseBudgetId)
                    .MaxAsync(a=>a.ApprovalOrder,ct)??0)+1;}
        },null,token);
    private Task<BudgetAdjustmentDto> MutateAsync<T>(Guid? id,string action,T command,string key,
        Func<BudgetAdjustmentEntity,BudgetInstance,Guid,CancellationToken,Task> mutation,Guid? baseId,CancellationToken token)
    {
        if(id==Guid.Empty||string.IsNullOrWhiteSpace(key)||key.Length>127||key!=key.Trim()||key.Any(char.IsControl))
            throw new InvalidIdempotencyKeyException("Invalid adjustment command key.");
        return transactions.ExecuteCompanyBudgetMutationAsync(async ct=>{
            if(!configuration.GetValue<bool>("Bms:BudgetAdjustments:WriteEnabled"))
                throw new BudgetAdjustmentRuleException("Запись корректировок временно отключена. Утверждённые корректировки продолжают учитываться.");
            var external=ActorIdentity.RequireBusinessDataAccess(actor);var employee=await roles.RequireEmployeeAsync(actor,ct);
            var row=id.HasValue?await db.BudgetAdjustments.SingleOrDefaultAsync(a=>a.Id==id.Value,ct):null;
            if(id.HasValue&&row is null)throw new BudgetInstanceNotFoundException();
            var parentId=row?.BaseBudgetId??baseId??throw new ArgumentException("Missing original budget.");
            if(baseId.HasValue&&baseId!=parentId)throw new BudgetAdjustmentRuleException("Исходный экземпляр сохранённой корректировки менять нельзя.");
            var b=await ParentAsync(parentId,ct);
            var scope=$"budget-adjustment:v1:{Hash(external)}:{(id.HasValue?id.Value.ToString("N"):"new")}:{action}";
            // Commands may replay only while the caller still has the corresponding authority.
            if(action is "save" or "submit"){if(!await EditorAsync(b,ct))throw new ActorForbiddenException();}
            else
            {
                var effective=actor.ActiveRoleCode;
                if(effective is not("FIN_DIRECTOR" or "GENERAL_DIRECTOR" or "FIN_CONTROLLER"))throw new ActorForbiddenException();
                await roles.RequireStageAsync(employee,b.CostCenterId,effective,ct);
            }
            var hash=Hash(command);var prior=await idempotency.FindAsync(scope,key,ct);
            if(prior is not null)
            {
                if(prior.RequestHash!=hash)throw new IdempotencyConflictException();
                return Read<BudgetAdjustmentDto>(prior.ResponseJson);
            }
            BudgetAdjustmentDto? before=row is null?null:await DtoAsync(row,b,ct);
            if(row is null)
            {
                var sequence=(await db.BudgetAdjustments.Where(a=>a.BaseBudgetId==parentId).MaxAsync(a=>(int?)a.SequenceNo,ct)??0)+1;
                row=new(){Id=Guid.NewGuid(),BaseBudgetId=parentId,CostCenterId=b.CostCenterId,SequenceNo=sequence,
                    Number=b.Number+"-"+sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),Stamp=Guid.NewGuid(),AuthorEmployeeId=employee,
                    AuthorExternalId=external,CreatedAtUtc=clock.UtcNow,UpdatedAtUtc=clock.UtcNow};
                db.BudgetAdjustments.Add(row);
            }
            await mutation(row,b,employee,ct);
            row.Stamp=Guid.NewGuid();row.UpdatedAtUtc=clock.UtcNow;
            await db.SaveChangesAsync(ct);
            // Only approved rows affect the read projection. Original document remains untouched.
            var refreshed=await ParentAsync(parentId,ct);
            var after=await DtoAsync(row,refreshed,ct);
            var json=JsonSerializer.Serialize(after,Json);
            audit.Append(Guid.NewGuid(),clock.UtcNow,external,"BudgetAdjustment",row.Id.ToString("D"),
                "BudgetAdjustment"+action,before is null?null:JsonSerializer.Serialize(before,Json),json,Guid.NewGuid().ToString("N"));
            idempotency.Add(scope,key,hash,200,json,clock.UtcNow);
            await db.SaveChangesAsync(ct);
            return after;
        },token);
    }
}
