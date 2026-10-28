using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// SQL-backed exact reader used solely by the actor-to-budget-owner authority
/// chain. It does not project labels, inspect actor access scope or write.
/// </summary>
public sealed class BudgetOwnerCostCenterAuthorityReader(BmsDbContext dbContext, bool readAssignedCreatorMemberships = false)
    : IBudgetOwnerCostCenterAuthorityReader
{
    public IBudgetOwnerCostCenterAuthorityReader ForAuthorizedPrincipal() =>
        new BudgetOwnerCostCenterAuthorityReader(dbContext, readAssignedCreatorMemberships: true);

    public async Task<IReadOnlyList<BudgetOwnerCostCenterAuthorityProjectionRow>> ReadCreatorsAsync(
        Guid employeeId, Guid costCenterId, CancellationToken cancellationToken) =>
        await BuildCreatorsQuery(employeeId, costCenterId).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<BudgetOwnerCostCenterAuthorityProjectionRow>> ReadCreatorsForEmployeeAsync(
        Guid employeeId, CancellationToken cancellationToken) =>
        await BuildCreatorsQuery(employeeId).ToArrayAsync(cancellationToken);

    private IQueryable<BudgetOwnerCostCenterAuthorityProjectionRow> BuildCreatorsQuery(Guid employeeId, Guid? costCenterId = null) =>
        from center in dbContext.CostCenters.AsNoTracking()
        where (readAssignedCreatorMemberships
            ? BusinessDataReadScopeQuery.AssignedCreatorCostCenterIds(dbContext, employeeId)
            : BusinessDataReadScopeQuery.OwnedCostCenterIds(dbContext, employeeId)).Contains(center.Id)
            && (costCenterId == null || center.Id == costCenterId.Value)
        select new BudgetOwnerCostCenterAuthorityProjectionRow(employeeId, center.Id, true, true, center.IsActive, center.IsGroup);

    public async Task<IReadOnlyList<BudgetOwnerCostCenterAuthorityProjectionRow>> ReadAsync(
        Guid employeeId,
        Guid costCenterId,
        CancellationToken cancellationToken)
    {
        var rows = await BuildQuery(employeeId, costCenterId).ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(rows);
    }

    public async Task<IReadOnlyList<BudgetOwnerCostCenterAuthorityProjectionRow>> ReadForEmployeeAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var rows = await BuildQuery(employeeId).ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(rows);
    }

    internal IQueryable<BudgetOwnerCostCenterAuthorityProjectionRow> BuildQuery(
        Guid employeeId,
        Guid? costCenterId = null) =>
        from assignment in dbContext.BudgetOwnerCostCenterAssignments.AsNoTracking()
        where assignment.EmployeeId == employeeId
            && (costCenterId == null || assignment.CostCenterId == costCenterId.Value)
        join employee in dbContext.Employees.AsNoTracking()
            on assignment.EmployeeId equals employee.Id
        join costCenter in dbContext.CostCenters.AsNoTracking()
            on assignment.CostCenterId equals costCenter.Id
        select new BudgetOwnerCostCenterAuthorityProjectionRow(
            assignment.EmployeeId,
            assignment.CostCenterId,
            assignment.IsActive,
            employee.IsActive,
            costCenter.IsActive,
            costCenter.IsGroup);
}
