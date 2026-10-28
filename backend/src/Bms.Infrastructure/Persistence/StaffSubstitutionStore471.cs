using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Application.Treasury;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Bms.Infrastructure.Persistence;

public sealed class StaffSubstitutionStore471(BmsDbContext db,IBusinessTransactionRunner transactions,
    IClock clock,IDatedPaymentCalendarTimeZoneProvider zones,IConfiguration configuration):IStaffSubstitutionStore471
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private bool Enabled=>configuration.GetValue<bool>("Bms:StaffSubstitution471:Enabled");
    private Task<T> Within<T>(Func<CancellationToken,Task<T>> read,CancellationToken token)=>db.Database.CurrentTransaction is null
        ?transactions.ExecuteAsync(read,token):read(token);
    private async Task<Guid> PhysicalEmployee(string external,CancellationToken t)
    {
        if(!Enabled)throw new ActorForbiddenException();
        if(!ActorIdentity.IsCanonical(external))throw new ActorForbiddenException();
        var rows=await (from m in db.EmployeeActorMappings.AsNoTracking() join e in db.Employees.AsNoTracking() on m.EmployeeId equals e.Id
            where m.ActorExternalId==external select new{m.ActorExternalId,m.EmployeeId,m.IsActive,EmployeeActive=e.IsActive}).Take(2).ToArrayAsync(t);
        if(rows.Length!=1||rows[0].ActorExternalId!=external||!rows[0].IsActive||!rows[0].EmployeeActive||rows[0].EmployeeId==Guid.Empty)
            throw new ActorForbiddenException();
        var id=rows[0].EmployeeId;
        var assigned=await OwnRoles(id).Select(r=>r.Code).ToArrayAsync(t);
        if(assigned.Any(code=>StaffSubstitutionGuard471.IntegrationRoles.Contains(code,StringComparer.OrdinalIgnoreCase)))
            throw new ActorForbiddenException();
        return id;
    }
    private IQueryable<FunctionalRoleReference> OwnRoles(Guid employee)=>from a in db.EmployeeFunctionalRoles.AsNoTracking()
        join r in db.FunctionalRoles.AsNoTracking() on a.FunctionalRoleId equals r.Id where a.EmployeeId==employee&&a.IsActive&&r.IsActive select r;
    public Task<StaffOptions471> OptionsAsync(string external,CancellationToken token)=>Within(async t=>
    {
        var id=await PhysicalEmployee(external,t);var codes=ActiveRoleCatalog.BusinessRoleCodes.ToArray();
        var roles=await OwnRoles(id).Where(r=>codes.Contains(r.Code)).OrderBy(r=>r.Name).Select(r=>new StaffRole471(r.Code,r.Name)).ToArrayAsync(t);
        var employees=await (from e in db.Employees.AsNoTracking() where e.IsActive&&e.Id!=id
            &&db.EmployeeActorMappings.Count(m=>m.EmployeeId==e.Id&&m.IsActive)==1
            &&!db.EmployeeFunctionalRoles.Any(a=>a.EmployeeId==e.Id&&a.IsActive&&db.FunctionalRoles.Any(r=>r.Id==a.FunctionalRoleId&&r.IsActive
                &&(r.Code=="INTEGRATION_ADMIN"||r.Code=="BMS_АдминистраторИнтеграции"||r.Code=="BMS.IntegrationAdministrator")))
            orderby e.Name,e.Id select new StaffEmployee471(e.Id,e.Name)).Take(2001).ToArrayAsync(t);
        if(employees.Length>2000)throw new StaffSubstitutionUnavailable471();
        var name=await db.Employees.Where(e=>e.Id==id).Select(e=>e.Name).SingleAsync(t);
        return new StaffOptions471(id,name,roles,employees);
    },token);
    public Task<StaffSubstitutionPage471> ListAsync(string external,int skip,int take,CancellationToken token)=>Within(async t=>
    {
        if(skip<0||take is <1 or >100||skip>int.MaxValue-take)throw new ArgumentException("Invalid staff document page.");
        var id=await PhysicalEmployee(external,t);var q=db.StaffSubstitutions471.AsNoTracking().Where(d=>d.PrincipalEmployeeId==id||d.DeputyEmployeeId==id&&d.Status!="DRAFT");
        var count=await q.CountAsync(t);var rows=await q.OrderByDescending(d=>d.UpdatedAtUtc).ThenBy(d=>d.Id).Skip(skip).Take(take).ToArrayAsync(t);
        return new StaffSubstitutionPage471(await Dtos(rows,id,t),skip,take,count);
    },token);
    public Task<StaffSubstitutionDto471?> GetAsync(string external,Guid document,CancellationToken token)=>Within(async t=>
    {
        var employee=await PhysicalEmployee(external,t);var row=await db.StaffSubstitutions471.AsNoTracking().SingleOrDefaultAsync(d=>d.Id==document,t);
        return row is null||!Visible(row,employee)?null:await Dto(row,employee,t);
    },token);
    private static bool Visible(StaffSubstitutionDocument471 d,Guid employee)=>d.PrincipalEmployeeId==employee||d.DeputyEmployeeId==employee&&d.Status!="DRAFT";
    private static StaffSubstitutionSnapshot471 Snapshot(StaffSubstitutionDocument471 d)=>new(d.Id,d.Number,d.PrincipalEmployeeId,d.DeputyEmployeeId,d.RoleCode,d.From,d.Through,
        d.ValidFromUtc,d.ValidBeforeUtc,d.Status,d.Version,d.CreatedAtUtc,d.UpdatedAtUtc,d.AcceptedAtUtc);
    private async Task<StaffSubstitutionDto471> Dto(StaffSubstitutionDocument471 d,Guid employee,CancellationToken t)
    {
        return (await Dtos([d],employee,t))[0];
    }
    private async Task<IReadOnlyList<StaffSubstitutionDto471>> Dtos(IReadOnlyList<StaffSubstitutionDocument471> rows,Guid employee,CancellationToken t)
    {
        var ids=rows.SelectMany(d=>new[]{d.PrincipalEmployeeId,d.DeputyEmployeeId}).Distinct().ToArray();
        var codes=rows.Select(d=>d.RoleCode).Distinct().ToArray();
        var names=await db.Employees.AsNoTracking().Where(e=>ids.Contains(e.Id)).Select(e=>new{e.Id,e.Name}).ToDictionaryAsync(e=>e.Id,e=>e.Name,t);
        var roles=await db.FunctionalRoles.AsNoTracking().Where(r=>codes.Contains(r.Code)).Select(r=>new{r.Code,r.Name}).ToDictionaryAsync(r=>r.Code,r=>r.Name,t);
        return rows.Select(d=>Dto(d,employee,names,roles)).ToArray();
    }
    private StaffSubstitutionDto471 Dto(StaffSubstitutionDocument471 d,Guid employee,IReadOnlyDictionary<Guid,string> names,IReadOnlyDictionary<string,string> roles)
    {
        var commands=new List<string>();foreach(var c in new[]{"update","submit","confirm","reject","revoke"})
        {try{StaffSubstitutionPolicy471.Transition(Snapshot(d),employee,c,clock.UtcNow);commands.Add(c);}catch(StaffSubstitutionConflict471){}catch(ActorForbiddenException){}}
        var status=d.Status=="CONFIRMED"&&d.ValidBeforeUtc is{} end&&clock.UtcNow>=end?"EXPIRED":d.Status;
        return new(d.Id,$"ЗМ-{d.Number:D5}",d.PrincipalEmployeeId,names.GetValueOrDefault(d.PrincipalEmployeeId,"Имя недоступно"),d.DeputyEmployeeId,
            names.GetValueOrDefault(d.DeputyEmployeeId,"Имя недоступно"),d.RoleCode,roles.GetValueOrDefault(d.RoleCode,"Роль недоступна"),d.From,d.Through,status,d.Version,d.UpdatedAtUtc,commands);
    }
    public Task<StaffSubstitutionDto471> ExecuteAsync(string external,Guid document,Guid command,int version,string action,StaffSubstitutionInput471? input,CancellationToken token)=>Within(async t=>
    {
        if(document==Guid.Empty||command==Guid.Empty||version<0||action is not("create" or "update" or "submit" or "confirm" or "reject" or "revoke"))throw new ArgumentException("Invalid staff document command.");
        if(db.ActiveRoleSelection?.Substitution471 is not null)throw new ActorForbiddenException();
        await db.Database.ExecuteSqlRawAsync("DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource=N'BMS:StaffSubstitution471:Mutation',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;IF @r<0 THROW 65471,'STAFF471_BUSY',1;",t);
        var employee=await PhysicalEmployee(external,t);
        if(db.ActiveRoleSelection?.Substitution471 is not null)throw new ActorForbiddenException();
        var sha=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{document,version,action,input},Json))));
        var receipt=await db.StaffSubstitutionCommands471.AsNoTracking().SingleOrDefaultAsync(r=>r.Id==command,t);
        if(receipt is not null)
        {
            if(receipt.DocumentId!=document||receipt.ActorExternalId!=external||receipt.Action!=action||receipt.PayloadSHA!=sha)throw new StaffSubstitutionConflict471();
            var current=await db.StaffSubstitutions471.AsNoTracking().SingleAsync(d=>d.Id==document,t);if(!Visible(current,employee))throw new ActorForbiddenException();
            return await Dto(current,employee,t);
        }
        var row=await db.StaffSubstitutions471.SingleOrDefaultAsync(d=>d.Id==document,t);var now=clock.UtcNow;
        string? before=row is null?null:JsonSerializer.Serialize(Snapshot(row),Json);
        if(action=="create")
        {
            if(row is not null||version!=0||input is null)throw new StaffSubstitutionConflict471();
            row=new(){Id=document,PrincipalEmployeeId=employee,CreatedAtUtc=now,Status="DRAFT",Version=0};db.StaffSubstitutions471.Add(row);
        }
        else
        {
            if(row is null||!Visible(row,employee))throw new ActorForbiddenException();
            if(row.Version!=version)throw new StaffSubstitutionConflict471();
            row.Status=StaffSubstitutionPolicy471.Transition(Snapshot(row),employee,action,now);
        }
        if(action is "create" or "update")
        {
            if(input is null)throw new ArgumentException("Staff fields required.");StaffSubstitutionPolicy471.Validate(input,row.PrincipalEmployeeId);
            if(!await OwnRoles(employee).AnyAsync(r=>r.Code==input.RoleCode,t))throw new ActorForbiddenException();
            if(!await db.Employees.AnyAsync(e=>e.Id==input.DeputyEmployeeId&&e.IsActive,t)
                ||await db.EmployeeActorMappings.CountAsync(m=>m.EmployeeId==input.DeputyEmployeeId&&m.IsActive,t)!=1)throw new StaffSubstitutionConflict471();
            if(await OwnRoles(input.DeputyEmployeeId).AnyAsync(r=>StaffSubstitutionGuard471.IntegrationRoles.Contains(r.Code),t))throw new StaffSubstitutionConflict471();
            var bounds=StaffSubstitutionPolicy471.Period(input,zones.Resolve()??throw new StaffSubstitutionUnavailable471());row.DeputyEmployeeId=input.DeputyEmployeeId;row.RoleCode=input.RoleCode;
            row.From=input.From;row.Through=input.Through;row.ValidFromUtc=bounds.From;row.ValidBeforeUtc=bounds.Before;
        }
        if(action is "submit" or "confirm")
        {
            if(!await OwnRoles(row.PrincipalEmployeeId).AnyAsync(r=>r.Code==row.RoleCode,t)||!ActiveRoleCatalog.IsBusinessRole(row.RoleCode)
                ||!await db.Employees.AnyAsync(e=>e.Id==row.PrincipalEmployeeId&&e.IsActive,t)
                ||!await db.Employees.AnyAsync(e=>e.Id==row.DeputyEmployeeId&&e.IsActive,t))throw new StaffSubstitutionConflict471();
            var principalLinks=await db.EmployeeActorMappings.AsNoTracking().Where(m=>m.EmployeeId==row.PrincipalEmployeeId&&m.IsActive).Select(m=>m.ActorExternalId).Take(2).ToArrayAsync(t);
            if(principalLinks.Length!=1||!ActorIdentity.IsCanonical(principalLinks[0])
                ||await db.EmployeeActorMappings.CountAsync(m=>m.EmployeeId==row.DeputyEmployeeId&&m.IsActive,t)!=1)
                throw new StaffSubstitutionConflict471();
            if(await OwnRoles(row.PrincipalEmployeeId).AnyAsync(r=>StaffSubstitutionGuard471.IntegrationRoles.Contains(r.Code),t)
                ||await OwnRoles(row.DeputyEmployeeId).AnyAsync(r=>StaffSubstitutionGuard471.IntegrationRoles.Contains(r.Code),t))throw new StaffSubstitutionConflict471();
            if(await db.StaffSubstitutions471.AnyAsync(d=>d.Id!=row.Id&&d.PrincipalEmployeeId==row.PrincipalEmployeeId&&d.RoleCode==row.RoleCode
                &&(d.Status=="PENDING"||d.Status=="CONFIRMED")&&(row.ValidBeforeUtc==null||d.ValidFromUtc<row.ValidBeforeUtc)
                &&(d.ValidBeforeUtc==null||row.ValidFromUtc<d.ValidBeforeUtc),t))throw new StaffSubstitutionConflict471();
        }
        row.Version=checked(row.Version+1);row.UpdatedAtUtc=now;if(action=="confirm")row.AcceptedAtUtc=now;
        await db.SaveChangesAsync(t);var result=await Dto(row,employee,t);
        db.BusinessAuditEntries.Add(new(){Id=Guid.NewGuid(),OccurredAtUtc=now,ActorExternalId=external,ObjectType="StaffSubstitution471",ObjectId=row.Id.ToString("D"),
            Action="StaffSubstitution471."+action,BeforeJson=before,AfterJson=JsonSerializer.Serialize(Snapshot(row),Json),CorrelationId=command.ToString("N")});
        db.StaffSubstitutionCommands471.Add(new(){Id=command,DocumentId=document,ActorExternalId=external,Action=action,PayloadSHA=sha,ResponseJson=JsonSerializer.Serialize(result,Json),AtUtc=now});
        await db.SaveChangesAsync(t);return result;
    },token);
    public Task<IReadOnlyList<StaffNews471>> NewsAsync(string external,CancellationToken token)=>Within<IReadOnlyList<StaffNews471>>(async t=>
    {
        _=await PhysicalEmployee(external,t);
        return await (from d in db.StaffSubstitutions471.AsNoTracking() join p in db.Employees.AsNoTracking() on d.PrincipalEmployeeId equals p.Id
            join e in db.Employees.AsNoTracking() on d.DeputyEmployeeId equals e.Id join r in db.FunctionalRoles.AsNoTracking() on d.RoleCode equals r.Code
            where d.AcceptedAtUtc!=null && (d.Status=="CONFIRMED"||d.Status=="REVOKED") orderby d.UpdatedAtUtc descending,d.Id select new StaffNews471(d.Id,"ЗМ-"+d.Number,p.Name,e.Name,r.Name,d.From,d.Through,
                d.Status=="CONFIRMED"&&d.ValidBeforeUtc!=null&&clock.UtcNow>=d.ValidBeforeUtc?"EXPIRED":d.Status,d.UpdatedAtUtc))
            .Take(20).ToArrayAsync(t);
    },token);
    public Task<IReadOnlyList<StaffActingRole471>> ActiveAsync(string external,CancellationToken token)=>Within<IReadOnlyList<StaffActingRole471>>(async t=>
    {
        if(!Enabled)return [];var employee=await PhysicalEmployee(external,t);var now=clock.UtcNow;var allowed=ActiveRoleCatalog.BusinessRoleCodes.ToArray();
        var rows=await (from d in db.StaffSubstitutions471.AsNoTracking() join p in db.Employees.AsNoTracking() on d.PrincipalEmployeeId equals p.Id
            join pm in db.EmployeeActorMappings.AsNoTracking() on p.Id equals pm.EmployeeId
            where d.DeputyEmployeeId==employee&&d.Status=="CONFIRMED"&&p.IsActive&&pm.IsActive&&d.ValidFromUtc<=now&&(d.ValidBeforeUtc==null||now<d.ValidBeforeUtc)
                &&allowed.Contains(d.RoleCode)&&db.EmployeeActorMappings.Count(m=>m.EmployeeId==p.Id&&m.IsActive)==1
                &&db.EmployeeActorMappings.Count(m=>m.ActorExternalId==pm.ActorExternalId)==1
                &&!db.EmployeeFunctionalRoles.Any(a=>(a.EmployeeId==p.Id||a.EmployeeId==employee)&&a.IsActive
                    &&db.FunctionalRoles.Any(r=>r.Id==a.FunctionalRoleId&&(!r.IsActive||StaffSubstitutionGuard471.IntegrationRoles.Contains(r.Code))))
                &&db.EmployeeFunctionalRoles.Any(a=>a.EmployeeId==p.Id&&a.IsActive&&db.FunctionalRoles.Any(r=>r.Id==a.FunctionalRoleId&&r.IsActive&&r.Code==d.RoleCode))
            select new StaffActingRole471(d.Id,p.Id,employee,pm.ActorExternalId,external,p.Name,d.RoleCode,d.ValidFromUtc,d.ValidBeforeUtc)).Take(129).ToArrayAsync(t);
        if(rows.Length>128||rows.Any(r=>!ActorIdentity.IsCanonical(r.PrincipalActorExternalId)))throw new StaffSubstitutionUnavailable471();
        return rows;
    },token);
    public async Task<StaffActingRole471?> ResolveActiveAsync(string external,Guid document,string role,CancellationToken t)
    {
        var all=await ActiveAsync(external,t);var found=all.Where(d=>d.DocumentId==document&&d.RoleCode==role).ToArray();
        if(found.Length>1)throw new StaffSubstitutionUnavailable471();return found.SingleOrDefault();
    }
    public Task<IReadOnlyList<StaffSubstitutionDto471>> PendingAsync(string external,CancellationToken token)=>Within<IReadOnlyList<StaffSubstitutionDto471>>(async t=>
    {
        var employee=await PhysicalEmployee(external,t);var now=clock.UtcNow;
        var rows=await db.StaffSubstitutions471.AsNoTracking().Where(d=>d.DeputyEmployeeId==employee&&d.Status=="PENDING"
            &&(d.ValidBeforeUtc==null||now<d.ValidBeforeUtc)).OrderBy(d=>d.UpdatedAtUtc).ThenBy(d=>d.Id).Take(101).ToArrayAsync(t);
        if(rows.Length>100)throw new StaffSubstitutionUnavailable471();
        return await Dtos(rows,employee,t);
    },token);
    public Task<IReadOnlyList<StaffAction471>> ActionsAsync(string external,Guid documentId,CancellationToken token)=>Within<IReadOnlyList<StaffAction471>>(async t=>
    {
        var employee=await PhysicalEmployee(external,t);var doc=await db.StaffSubstitutions471.AsNoTracking().SingleOrDefaultAsync(d=>d.Id==documentId,t);
        if(doc is null||!Visible(doc,employee))throw new ActorForbiddenException();
        return await (from a in db.StaffSubstitutionActions471.AsNoTracking()
            join p in db.Employees.AsNoTracking() on a.PrincipalEmployeeId equals p.Id
            join e in db.Employees.AsNoTracking() on a.DeputyEmployeeId equals e.Id
            join r in db.FunctionalRoles.AsNoTracking() on a.RoleCode equals r.Code
            where a.DocumentId==documentId orderby a.AtUtc descending,a.Id
            select new StaffAction471(a.Id,a.AtUtc,e.Name,p.Name,r.Name,a.ObjectType,a.ObjectId,a.Action)).Take(100).ToArrayAsync(t);
    },token);
}
