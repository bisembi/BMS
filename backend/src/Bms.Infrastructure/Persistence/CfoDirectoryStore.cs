using System.Security.Cryptography;
using System.Text.Json;
using Bms.Application.Authorization;
using Bms.Domain.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class CfoDirectoryStore(BmsDbContext db, BudgetOwnerRoleSynchronizer ownerRoles) : ICfoDirectoryStore
{
    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != System.Data.IsolationLevel.Serializable)
            throw new InvalidOperationException("CFO assignment needs Serializable transaction.");
    }

    public async Task LockAsync(Guid employeeId, Guid costCenterId, CancellationToken token)
    {
        RequireTransaction();
        var employee = await db.Employees.FromSqlInterpolated($"""
            SELECT * FROM bms_web.Employees WITH(UPDLOCK,HOLDLOCK) WHERE Id={employeeId}
            """).AsNoTracking().SingleOrDefaultAsync(token);
        var center = await db.CostCenters.FromSqlInterpolated($"""
            SELECT * FROM bms_web.CostCenters WITH(UPDLOCK,HOLDLOCK) WHERE Id={costCenterId}
            """).AsNoTracking().SingleOrDefaultAsync(token);
        if (center is null || employee is null || !employee.IsActive || !center.IsActive || center.IsGroup)
            throw new AuthorizationDirectoryEmployeeNotFoundException();
    }

    public async Task<IReadOnlyList<EmployeeCfoAssignment>> ListAsync(CancellationToken token)
    {
        RequireTransaction();
        var owners = from a in db.BudgetOwnerCostCenterAssignments.AsNoTracking()
                     join c in db.CostCenters.AsNoTracking() on a.CostCenterId equals c.Id
                     select new { a.EmployeeId, CostCenterId = c.Id, c.Code, c.Name, RoleCode = ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode, a.IsActive };
        var initiators = from a in db.CfoInitiatorCostCenterAssignments.AsNoTracking()
                         join c in db.CostCenters.AsNoTracking() on a.CostCenterId equals c.Id
                         select new { a.EmployeeId, CostCenterId = c.Id, c.Code, c.Name, RoleCode = ExpenseRequestWorkflowCodes.CfoInitiatorRoleCode, a.IsActive };
        var rows = await owners.Concat(initiators).OrderBy(row => row.EmployeeId).ThenBy(row => row.CostCenterId)
            .ThenBy(row => row.RoleCode).ToArrayAsync(token);
        return rows.Select(row => new EmployeeCfoAssignment(row.EmployeeId, row.CostCenterId, row.Code,
            row.Name, row.RoleCode, row.IsActive)).ToArray();
    }

    public async Task<EmployeeCfoState> ReadAsync(Guid employeeId, CancellationToken token)
    {
        RequireTransaction();
        if (!await db.Employees.AsNoTracking().AnyAsync(e => e.Id == employeeId, token))
            throw new AuthorizationDirectoryEmployeeNotFoundException();
        var assignments = (await ListAsync(token)).Where(a => a.EmployeeId == employeeId).ToArray();
        var centers = await db.CostCenters.AsNoTracking().Where(c => c.IsActive && !c.IsGroup
            && !db.CostCenterPurposes457.Any(s => s.CostCenterId == c.Id && s.OnlyCostDepartment)).OrderBy(c => c.Code)
            .Select(c => new CfoDirectoryOption(c.Id, c.Code, c.Name)).ToArrayAsync(token);
        var stamp = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(assignments)));
        return new EmployeeCfoState(assignments, centers, stamp);
    }

    public async Task WriteAsync(Guid employeeId, WriteEmployeeCfoCommand command, CancellationToken token)
    {
        RequireTransaction();
        var role = await db.FunctionalRoles.SingleOrDefaultAsync(r => r.Code == command.RoleCode, token);
        if (role is null || !role.IsActive || role.Code != command.RoleCode)
            throw new AuthorizationDirectoryUnavailableException();
        if (command.IsActive)
        {
            var employeeRole = await db.EmployeeFunctionalRoles.SingleOrDefaultAsync(r => r.EmployeeId == employeeId && r.FunctionalRoleId == role.Id, token);
            if (employeeRole is null || !employeeRole.IsActive)
                throw new Bms.Domain.Common.DomainRuleViolationException("Сначала системный администратор должен назначить сотруднику выбранную роль.");
        }
        if (command.RoleCode == ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode)
        {
            var link = await db.BudgetOwnerCostCenterAssignments.SingleOrDefaultAsync(a => a.EmployeeId == employeeId && a.CostCenterId == command.CostCenterId, token);
            // An explicit repeated removal of a known historical pair repairs
            // legacy stale roles too; a never-assigned Owner-first employee is untouched.
            var removed = link is not null && !command.IsActive;
            var postimage = OwnerAssignmentPostimage(employeeId, command.CostCenterId, command.IsActive, link);
            if (link is null && postimage is not null) db.BudgetOwnerCostCenterAssignments.Add(postimage);
            if (removed)
                await ownerRoles.RemoveRoleWithoutScopeAsync([employeeId], "EmployeeCfoAssignmentRemoved", token);
        }
        else
        {
            var link = await db.CfoInitiatorCostCenterAssignments.SingleOrDefaultAsync(a => a.EmployeeId == employeeId && a.CostCenterId == command.CostCenterId, token);
            if (link is null) db.CfoInitiatorCostCenterAssignments.Add(new CfoInitiatorCostCenterAssignment { EmployeeId = employeeId, CostCenterId = command.CostCenterId, IsActive = command.IsActive });
            else link.IsActive = command.IsActive;
        }
    }

    internal static BudgetOwnerCostCenterAssignment? OwnerAssignmentPostimage(Guid employeeId,
        Guid costCenterId, bool isActive, BudgetOwnerCostCenterAssignment? current)
    {
        if (current is not null) { current.IsActive = isActive; return current; }
        // False for a never-bound pair is a no-op, not fabricated owner history.
        return isActive ? new BudgetOwnerCostCenterAssignment
            { EmployeeId = employeeId, CostCenterId = costCenterId, IsActive = true } : null;
    }
}
