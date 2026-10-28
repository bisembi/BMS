using Bms.Application.Authorization;
using Bms.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class CfoWorkflowAuthorityReader(BmsDbContext db) : ICfoWorkflowAuthorityReader
{
    public async Task<bool> HasAssignmentAsync(Guid employeeId, Guid costCenterId, string roleCode, CancellationToken token)
    {
        if (!await db.Employees.AsNoTracking().AnyAsync(e => e.Id == employeeId && e.IsActive, token)
            || !await db.CostCenters.AsNoTracking().AnyAsync(c => c.Id == costCenterId && c.IsActive && !c.IsGroup, token))
            return false;
        return roleCode switch
        {
            ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode => await db.BudgetOwnerCostCenterAssignments.AsNoTracking()
                .AnyAsync(a => a.EmployeeId == employeeId && a.CostCenterId == costCenterId && a.IsActive, token),
            ExpenseRequestWorkflowCodes.CfoInitiatorRoleCode => await db.CfoInitiatorCostCenterAssignments.AsNoTracking()
                .AnyAsync(a => a.EmployeeId == employeeId && a.CostCenterId == costCenterId && a.IsActive, token),
            _ => false
        };
    }

    public async Task<IReadOnlyList<Guid>> ReadOwnersAsync(Guid costCenterId, Guid excludedEmployeeId, CancellationToken token)
    {
        var candidates = await db.BudgetOwnerCostCenterAssignments.AsNoTracking()
            .Where(a => a.CostCenterId == costCenterId && a.IsActive && a.EmployeeId != excludedEmployeeId)
            .Select(a => a.EmployeeId).ToArrayAsync(token);
        var result = new List<Guid>();
        foreach (var employee in candidates.Distinct())
        {
            var ids = db.Employees.AsNoTracking().Where(e => e.Id == employee && e.IsActive).Select(e => e.Id);
            if (await BusinessDataReadScopeQuery.AssignedRoleCodes(db, ids).AnyAsync(code => code == ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode, token)
                && await db.EmployeeActorMappings.AsNoTracking().AnyAsync(m => m.EmployeeId == employee && m.IsActive, token)
                && await HasAssignmentAsync(employee, costCenterId, ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode, token))
                result.Add(employee);
        }
        return result.OrderBy(id => id).ToArray();
    }
}
