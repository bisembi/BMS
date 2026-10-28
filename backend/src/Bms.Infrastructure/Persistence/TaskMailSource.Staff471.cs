using Bms.Application.Authorization;
using Bms.Infrastructure.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Bms.Infrastructure.Persistence;

public sealed partial class TaskMailSource
{
    public async Task<IReadOnlyList<TaskMailTask>> ReadStaff471Async(Guid? documentId,Guid? employeeId,CancellationToken token)
    {
        if(configuration?.GetValue<bool>("Bms:StaffSubstitution471:Enabled") != true) return [];
        var now=DateTimeOffset.UtcNow;
        var query=from d in db.StaffSubstitutions471.AsNoTracking()
            join p in db.Employees.AsNoTracking() on d.PrincipalEmployeeId equals p.Id
            join e in db.Employees.AsNoTracking() on d.DeputyEmployeeId equals e.Id
            join m in db.EmployeeActorMappings.AsNoTracking() on e.Id equals m.EmployeeId
            join r in db.FunctionalRoles.AsNoTracking() on d.RoleCode equals r.Code
            where d.Status=="PENDING" && p.IsActive && e.IsActive && m.IsActive && r.IsActive
                && (d.ValidBeforeUtc==null || now<d.ValidBeforeUtc)
                && db.EmployeeActorMappings.Count(x=>x.EmployeeId==e.Id&&x.IsActive)==1
                && db.EmployeeActorMappings.Count(x=>x.ActorExternalId==m.ActorExternalId)==1
                && db.EmployeeFunctionalRoles.Any(a=>a.EmployeeId==p.Id&&a.FunctionalRoleId==r.Id&&a.IsActive)
                && !db.EmployeeFunctionalRoles.Any(a=>(a.EmployeeId==p.Id||a.EmployeeId==e.Id)&&a.IsActive
                    && db.FunctionalRoles.Any(role=>role.Id==a.FunctionalRoleId&&(!role.IsActive||StaffSubstitutionGuard471.IntegrationRoles.Contains(role.Code))))
            select new {d.Id,d.Number,d.From,d.Through,Principal=p.Name,Deputy=e.Name,EmployeeId=e.Id,m.ActorExternalId,Role=r.Name};
        if(documentId.HasValue)query=query.Where(d=>d.Id==documentId.Value);
        if(employeeId.HasValue)query=query.Where(d=>d.EmployeeId==employeeId.Value);
        var rows=await query.Take(2001).ToArrayAsync(token);
        if(rows.Length>2000)throw new StaffSubstitutionUnavailable471();
        return rows.Select(d=>new TaskMailTask(d.Id,d.EmployeeId,"SUBSTITUTION_RECIPIENT","staff:submit:1",d.ActorExternalId,d.Deputy,
            $"ЗМ-{d.Number:D5}","","",0,"KZT",
            $"{d.Principal} передаёт вам роль «{d.Role}» с {d.From:dd.MM.yy}"+(d.Through is {} end?$" по {end:dd.MM.yy}.":" на неопределённый срок."),
            DocumentKind:TaskMailDocumentKinds.StaffSubstitution)).ToArray();
    }
}
