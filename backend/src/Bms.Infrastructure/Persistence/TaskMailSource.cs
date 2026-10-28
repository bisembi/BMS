using Bms.Domain.ExpenseRequests;
using Bms.Application.Authorization;
using Bms.Infrastructure.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Bms.Infrastructure.Persistence;

public sealed partial class TaskMailSource(BmsDbContext db, IConfiguration? configuration = null)
{
    internal bool AdjustmentsEnabled448 => configuration?.GetValue<bool>("Bms:BudgetAdjustments:WriteEnabled") ?? true;
    public async Task<IReadOnlyList<TaskMailTask>> ReadAsync(CancellationToken token) =>
        (await ReadAsync(null,null,token)).Concat(await ReadBudgets391Async(null,null,token))
            .Concat(await ReadAdjustments448Async(null,null,token)).Concat(await ReadStaff471Async(null,null,token)).ToArray();

    public Task<IReadOnlyList<TaskMailTask>> ReadCurrentAsync(TaskMailEnvelope envelope,CancellationToken token) =>
        envelope.DocumentKind switch
        {
            TaskMailDocumentKinds.ExpenseRequest=>ReadAsync(envelope.RequestId,envelope.EmployeeId,token),
            TaskMailDocumentKinds.BudgetInstance=>ReadBudgets391Async(envelope.RequestId,envelope.EmployeeId,token),
            TaskMailDocumentKinds.BudgetAdjustment=>ReadAdjustments448Async(envelope.RequestId,envelope.EmployeeId,token),
            TaskMailDocumentKinds.StaffSubstitution=>ReadStaff471Async(envelope.RequestId,envelope.EmployeeId,token),
            _=>throw new InvalidOperationException("Unknown mail document kind.")
        };
    public async Task<IReadOnlyList<TaskMailTask>> ReadAsync(Guid? requestId,Guid? employeeId,CancellationToken token)
    {
        var requests=db.ExpenseRequests.AsNoTracking();if(requestId.HasValue)requests=requests.Where(r=>r.Id==requestId.Value);
        var recipients=from e in db.Employees.AsNoTracking()
            join a in db.EmployeeFunctionalRoles.AsNoTracking() on e.Id equals a.EmployeeId
            join role in db.FunctionalRoles.AsNoTracking() on a.FunctionalRoleId equals role.Id
            join m in db.EmployeeActorMappings.AsNoTracking() on e.Id equals m.EmployeeId
            where e.IsActive&&a.IsActive&&role.IsActive&&m.IsActive
                // Match the API's overriding technical-role denial. This common
                // source is also used for both authorization rechecks of queued mail.
                && !(from assignment in db.EmployeeFunctionalRoles.AsNoTracking()
                     join assignedRole in db.FunctionalRoles.AsNoTracking() on assignment.FunctionalRoleId equals assignedRole.Id
                     where assignment.EmployeeId==e.Id && assignment.IsActive
                         && (!assignedRole.IsActive
                             || assignedRole.Code==ActiveRoleCatalog.IntegrationAdministratorAssignmentCode
                             || assignedRole.Code==ActiveRoleCatalog.IntegrationAdministratorAssignmentAlias)
                     select assignment.EmployeeId).Any()
                &&(role.Code=="NSI_SPECIALIST"||role.Code=="BUDGET_OWNER"||role.Code=="FIN_CONTROLLER"||role.Code=="PAYROLL_CALCULATOR"
                    ||role.Code=="FIN_DIRECTOR"||role.Code=="GENERAL_DIRECTOR"||role.Code=="TREASURER")
                &&db.EmployeeActorMappings.Count(x=>x.EmployeeId==e.Id&&x.IsActive)==1
                &&db.EmployeeActorMappings.Count(x=>x.ActorExternalId==m.ActorExternalId)==1
            select new {e.Id,e.Name,m.ActorExternalId,Role=role.Code};
        if(employeeId.HasValue)recipients=recipients.Where(e=>e.Id==employeeId.Value);
        var rows=await (from r in requests
            from e in recipients
            where r.OrganizationId!=null && db.Organizations.Any(o=>o.Id==r.OrganizationId&&o.IsActive)
             && ((r.Status==ExpenseRequestStatus.AwaitingNsi&&e.Role=="NSI_SPECIALIST"
                 &&db.ActorOrganizationGrants.Any(g=>g.ActorExternalId==e.ActorExternalId&&g.OrganizationId==r.OrganizationId&&g.IsActive))
              ||(r.Status==ExpenseRequestStatus.Approved&&e.Role=="TREASURER")
              ||(r.Status==ExpenseRequestStatus.InApproval
                 &&(e.Role=="BUDGET_OWNER"||e.Role=="FIN_CONTROLLER"||e.Role=="FIN_DIRECTOR"||e.Role=="GENERAL_DIRECTOR"||e.Role=="PAYROLL_CALCULATOR")
                 &&db.ApprovalRouteStageParticipants.Any(p=>p.ExpenseRequestId==r.Id&&p.SnapshotNumber==r.ActiveApprovalRouteSnapshotNumber
                     &&p.StageNumber==r.ActiveApprovalRouteStageNumber&&p.EmployeeId==e.Id)
                 &&db.ApprovalRouteStages.Any(s=>s.ExpenseRequestId==r.Id&&s.SnapshotNumber==r.ActiveApprovalRouteSnapshotNumber
                     &&s.StageNumber==r.ActiveApprovalRouteStageNumber&&s.RoleCode==e.Role)
                 &&db.ApprovalRouteSnapshots.Any(s=>s.ExpenseRequestId==r.Id&&s.SnapshotNumber==r.ActiveApprovalRouteSnapshotNumber
                     &&s.WorkflowDefinitionVersion>=1&&s.WorkflowDefinitionVersion<=21
                     &&db.CostCenters.Any(c=>c.Id==s.CostCenterId&&c.IsActive&&!c.IsGroup)
                     &&(e.Role!="BUDGET_OWNER"||db.BudgetOwnerCostCenterAssignments.Any(a=>a.EmployeeId==e.Id
                         &&a.CostCenterId==s.CostCenterId&&a.IsActive)))
                 &&(e.Role!="BUDGET_OWNER"||!r.Lines.Any(l=>l.CostCenterId.HasValue
                     &&!db.BudgetOwnerCostCenterAssignments.Any(a=>a.EmployeeId==e.Id&&a.CostCenterId==l.CostCenterId&&a.IsActive)))))
            select new {RequestId=r.Id,EmployeeId=e.Id,e.Role,e.ActorExternalId,EmployeeName=e.Name,r.RequestNumber,r.CurrencyCode,r.Purpose,
                Organization=db.Organizations.Where(o=>o.Id==r.OrganizationId).Select(o=>o.Name).FirstOrDefault(),
                CostCenter=db.CostCenters.Where(c=>c.Id==r.DefaultCostCenterId).Select(c=>c.Name).FirstOrDefault(),
                Amount=r.Lines.Sum(l=>(decimal?)l.Amount)??0m,
                Snapshot=r.ActiveApprovalRouteSnapshotNumber,Stage=r.ActiveApprovalRouteStageNumber,
                Submission=db.BusinessAuditEntries.Where(a=>a.ObjectType=="ExpenseRequest"&&a.ObjectId==r.Id.ToString()
                    &&(a.Action=="SubmittedToNsi"||a.Action=="SubmittedForApproval"||a.Action=="SubmittedApproved"||a.Action=="SubmittedToPayment"))
                    .OrderByDescending(a=>a.OccurredAtUtc).ThenByDescending(a=>a.Id).Select(a=>(Guid?)a.Id).FirstOrDefault(),
                TreasuryEntry=db.BusinessAuditEntries.Where(a=>a.ObjectType=="ExpenseRequest"&&a.ObjectId==r.Id.ToString()
                    &&(a.Action=="WorkflowApproved"||a.Action=="WorkflowNsiConfirmed"||a.Action=="SubmittedApproved"
                        ||a.Action=="SubmittedToPayment"||a.Action=="BudgetRouteRecalculated"))
                    .OrderByDescending(a=>a.OccurredAtUtc).ThenByDescending(a=>a.Id).Select(a=>(Guid?)a.Id).FirstOrDefault()})
            .ToArrayAsync(token);
        var approvals = rows.Where(r=>!string.IsNullOrWhiteSpace(r.RequestNumber)&&(r.Role!="NSI_SPECIALIST"||r.Submission.HasValue)
                &&(r.Role!="TREASURER"||r.TreasuryEntry.HasValue))
            .Select(r=>new TaskMailTask(r.RequestId,r.EmployeeId,r.Role,r.Role switch
                {"NSI_SPECIALIST"=>"submit:"+r.Submission.GetValueOrDefault().ToString("N"),
                 "TREASURER"=>"treasury:"+r.TreasuryEntry.GetValueOrDefault().ToString("N"),
                 _=>$"route:{r.Snapshot}:{r.Stage}"},
                r.ActorExternalId,r.EmployeeName,r.RequestNumber??throw new InvalidOperationException("Submitted request number missing."),r.Organization??"",r.CostCenter??"",r.Amount,r.CurrencyCode,r.Purpose)).ToArray();
        return approvals.Concat(await ReadReturned377Async(requestId, employeeId, null, token)).ToArray();
    }
}
