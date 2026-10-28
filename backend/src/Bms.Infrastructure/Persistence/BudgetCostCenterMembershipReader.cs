using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class BudgetCostCenterMembershipReader(BmsDbContext dbContext) : IBudgetCostCenterMembershipReader
{
    public async Task<IReadOnlyList<BudgetCostCenterMembershipProjectionRow>> ReadAsync(
        Guid employeeId, Guid costCenterId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await BuildQuery(employeeId, costCenterId).ToArrayAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Array.AsReadOnly(rows);
    }

    internal IQueryable<BudgetCostCenterMembershipProjectionRow> BuildQuery(Guid employeeId, Guid costCenterId)
    {
        if (employeeId == Guid.Empty || costCenterId == Guid.Empty)
        {
            throw new ArgumentException("An exact employee and cost center are required.");
        }

        // Keep inactive and group flags observable; the authority decides denial
        // versus a contradictory projection. No document-author/expense-role fallback.
        return from membership in dbContext.BudgetCostCenterMemberships.AsNoTracking()
               where membership.EmployeeId == employeeId && membership.CostCenterId == costCenterId
               join employee in dbContext.Employees.AsNoTracking() on membership.EmployeeId equals employee.Id
               join costCenter in dbContext.CostCenters.AsNoTracking() on membership.CostCenterId equals costCenter.Id
               select new BudgetCostCenterMembershipProjectionRow(
                   membership.EmployeeId, membership.CostCenterId, membership.MembershipKind,
                   membership.IsActive, employee.IsActive, costCenter.IsActive, costCenter.IsGroup);
    }
}
