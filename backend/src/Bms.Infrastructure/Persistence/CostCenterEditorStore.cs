using System.Security.Cryptography;
using System.Text.Json;
using Bms.Application.Authorization;
using Bms.Domain.Common;
using Bms.Domain.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class CostCenterEditorStore(BmsDbContext db, BudgetOwnerRoleSynchronizer ownerRoles) : ICostCenterEditorStore
{
    public async Task<Guid> CreateAsync(CreateCostCenterEditorCommand command, CancellationToken token)
    {
        RequireTransaction();
        // Same employee-before-CFO order as the existing aggregate editor.
        if (command.OwnerEmployeeId is Guid employeeId)
            _ = await db.Employees.FromSqlInterpolated($"""
                SELECT * FROM bms_web.Employees WITH(UPDLOCK,HOLDLOCK) WHERE Id={employeeId}
                """).AsNoTracking().SingleOrDefaultAsync(token);
        var duplicate = await db.CostCenters.FromSqlInterpolated($"""
            SELECT * FROM bms_web.CostCenters WITH(UPDLOCK,HOLDLOCK,INDEX(UQ_bms_web_CostCenters_Code))
                WHERE Code={command.Code}
            """).AsNoTracking().SingleOrDefaultAsync(token);
        if (duplicate is not null) throw new AuthorizationDirectoryStateConflictException();
        var center = new CostCenterReference { Id = Guid.NewGuid(), Code = command.Code, Name = command.Name,
            GroupingName = command.GroupingName, IsActive = true, IsGroup = false };
        db.CostCenters.Add(center);
        if (command.OnlyCostDepartment)
            db.CostCenterPurposes457.Add(new CostCenterPurpose457 { CostCenterId = center.Id,
                OnlyCostDepartment = true, ConcurrencyStamp = Guid.NewGuid() });
        if (command.OwnerEmployeeId is not null)
            await AssignOwnerAsync(center, command.OwnerEmployeeId.Value, token);
        return center.Id;
    }
    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != System.Data.IsolationLevel.Serializable)
            throw new InvalidOperationException("CFO editor requires Serializable transaction.");
    }
    public async Task LockAsync(Guid id, Guid? ownerEmployeeId, CancellationToken token)
    {
        RequireTransaction();
        // Same employee-before-CFO order as NSI and employee-role writers.
        // Lock absence too; validation stays in WriteAsync so a valid replay
        // does not depend on the selected employee's later activity.
        if (ownerEmployeeId is Guid employeeId)
            _ = await db.Employees.FromSqlInterpolated($"""
                SELECT * FROM bms_web.Employees WITH(UPDLOCK,HOLDLOCK) WHERE Id={employeeId}
                """).AsNoTracking().SingleOrDefaultAsync(token);
        var center = await db.CostCenters.FromSqlInterpolated($"""
            SELECT * FROM bms_web.CostCenters WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}
            """).AsNoTracking().SingleOrDefaultAsync(token);
        if (center is null || !center.IsActive || center.IsGroup)
            throw new AuthorizationDirectoryEmployeeNotFoundException();
    }
    public async Task<CostCenterEditorState> ReadAsync(Guid id, CancellationToken token)
    {
        RequireTransaction();
        var center = await db.CostCenters.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, token);
        if (center is null || !center.IsActive || center.IsGroup)
            throw new AuthorizationDirectoryEmployeeNotFoundException();
        var links = await (from a in db.BudgetOwnerCostCenterAssignments.AsNoTracking()
            join e in db.Employees.AsNoTracking() on a.EmployeeId equals e.Id
            where a.CostCenterId == id
            orderby a.EmployeeId
            select new { a.EmployeeId, a.IsActive, e.Name, EmployeeIsActive = e.IsActive,
                RoleIsActive = db.EmployeeFunctionalRoles.Any(er => er.EmployeeId == e.Id && er.IsActive
                    && db.FunctionalRoles.Any(r => r.Id == er.FunctionalRoleId && r.IsActive
                        && r.Code == ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode)) }).ToArrayAsync(token);
        var purpose = await db.CostCenterPurposes457.AsNoTracking().SingleOrDefaultAsync(p => p.CostCenterId == id, token);
        // Include inactive links and effective role status: a concurrent employee editor invalidates CAS.
        var stamp = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            { center.Id, center.Code, center.Name, center.ParentId, center.IsGroup, center.IsActive,
                center.GroupingName, center.OwnerManagedLocally, Links = links, Purpose = purpose })));
        return new(center.Id, center.Code, center.Name, center.GroupingName,
            links.Where(a => a.IsActive).Select(a => new CostCenterOwner(a.EmployeeId, a.Name,
                a.EmployeeIsActive, a.RoleIsActive)).ToArray(), stamp, purpose?.OnlyCostDepartment ?? false);
    }
    public async Task WriteAsync(Guid id, UpdateCostCenterEditorCommand command, CancellationToken token)
    {
        RequireTransaction();
        if (await db.CostCenters.AnyAsync(c => c.Id != id && c.Code == command.Code, token))
            throw new AuthorizationDirectoryStateConflictException();
        var center = await db.CostCenters.SingleAsync(c => c.Id == id, token);
        center.Code = command.Code; center.Name = command.Name; center.GroupingName = command.GroupingName;
        if (!command.OwnerSpecified) return;
        if (command.OwnerEmployeeId is not Guid employeeId || employeeId == Guid.Empty)
            throw new DomainRuleViolationException("Choose an active employee.");
        await AssignOwnerAsync(center, employeeId, token);
    }
    private async Task AssignOwnerAsync(CostCenterReference center, Guid employeeId, CancellationToken token)
    {
        var id = center.Id;
        center.OwnerManagedLocally = true;
        var employee = await db.Employees.FromSqlInterpolated($"""
            SELECT * FROM bms_web.Employees WITH(UPDLOCK,HOLDLOCK) WHERE Id={employeeId}
            """).SingleOrDefaultAsync(token);
        if (employee is null || !employee.IsActive)
            throw new DomainRuleViolationException("Choose an active employee.");
        var role = await db.FunctionalRoles.SingleOrDefaultAsync(r => r.Code == ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode, token);
        if (role is null || !role.IsActive || role.Code != ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode)
            throw new AuthorizationDirectoryUnavailableException();
        var employeeRole = await db.EmployeeFunctionalRoles.SingleOrDefaultAsync(r => r.EmployeeId == employeeId
            && r.FunctionalRoleId == role.Id, token);
        if (employeeRole is null || !employeeRole.IsActive)
            throw new DomainRuleViolationException("Сначала системный администратор должен назначить сотруднику роль «Владелец бюджета».");
        var owners = await db.BudgetOwnerCostCenterAssignments.Where(a => a.CostCenterId == id).ToArrayAsync(token);
        var removedEmployees = owners.Where(a => a.IsActive && a.EmployeeId != employeeId)
            .Select(a => a.EmployeeId).ToArray();
        foreach (var link in owners) link.IsActive = link.EmployeeId == employeeId;
        if (!owners.Any(a => a.EmployeeId == employeeId)) db.BudgetOwnerCostCenterAssignments.Add(new
            BudgetOwnerCostCenterAssignment { EmployeeId = employeeId, CostCenterId = id, IsActive = true });
        await ownerRoles.RemoveRoleWithoutScopeAsync(removedEmployees, "CostCenterOwnerReplaced", token);
        // No initiator, other role/CFO or captured workflow is touched.
    }
}
