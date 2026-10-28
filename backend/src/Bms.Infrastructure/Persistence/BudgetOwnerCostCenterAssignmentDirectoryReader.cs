using Bms.Application.Abstractions;
using Bms.Application.ReferenceData;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// SQL-backed read model for the approved employee -- budget-owner -- cost
/// centre relation. Source user names, actor identifiers and database UUIDs
/// are intentionally not projected.
/// </summary>
public sealed class BudgetOwnerCostCenterAssignmentDirectoryReader(BmsDbContext dbContext)
    : IBudgetOwnerCostCenterAssignmentDirectoryReader
{
    internal const string BudgetOwnerRoleCode = "BUDGET_OWNER";

    public async Task<IReadOnlyList<BudgetOwnerCostCenterAssignmentDirectoryItemDto>> ListVisibleAsync(
        string actorExternalId,
        bool bypassActorScope,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorExternalId))
        {
            throw new ArgumentException("Actor external id is required.", nameof(actorExternalId));
        }

        var materialized = await BuildQuery(actorExternalId, bypassActorScope, asOf)
            .ToArrayAsync(cancellationToken);

        return Array.AsReadOnly(materialized);
    }

    internal IQueryable<BudgetOwnerCostCenterAssignmentDirectoryItemDto> BuildQuery(
        string actorExternalId,
        bool bypassActorScope,
        DateOnly asOf)
    {
        if (string.IsNullOrWhiteSpace(actorExternalId))
        {
            throw new ArgumentException("Actor external id is required.", nameof(actorExternalId));
        }

        var rows = from assignment in dbContext.BudgetOwnerCostCenterAssignments.AsNoTracking()
                   join employee in dbContext.Employees.AsNoTracking()
                       on assignment.EmployeeId equals employee.Id
                   join costCenter in dbContext.CostCenters.AsNoTracking()
                       on assignment.CostCenterId equals costCenter.Id
                   join employeeRole in dbContext.EmployeeFunctionalRoles.AsNoTracking()
                       on assignment.EmployeeId equals employeeRole.EmployeeId
                   join role in dbContext.FunctionalRoles.AsNoTracking()
                       on employeeRole.FunctionalRoleId equals role.Id
                   where assignment.IsActive
                         && employee.IsActive
                         && costCenter.IsActive
                         && !costCenter.IsGroup
                         && employeeRole.IsActive
                         && role.IsActive
                         && role.Code == BudgetOwnerRoleCode
                   select new
                   {
                       assignment.CostCenterId,
                       employee.Code,
                       employee.Name,
                       CostCenterCode = costCenter.Code,
                       CostCenterName = costCenter.Name
                   };

        if (!bypassActorScope)
        {
            var visible = BusinessDataReadScopeQuery.VisibleCostCenterIds(dbContext, actorExternalId);
            rows = rows.Where(row => visible.Contains(row.CostCenterId));
        }

        return rows
            .OrderBy(row => row.CostCenterCode)
            .ThenBy(row => row.Code)
            .ThenBy(row => row.Name)
            .Select(row => new BudgetOwnerCostCenterAssignmentDirectoryItemDto(
                row.Code,
                row.Name,
                row.CostCenterCode,
                row.CostCenterName))
            ;
    }
}
