using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class EmployeeFunctionalRoleAssignmentReader(BmsDbContext dbContext) :
    IEmployeeFunctionalRoleAssignmentReader
{
    public async Task<IReadOnlyList<EmployeeFunctionalRoleProjectionRow>> ReadAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var rows = await BuildQuery(employeeId).ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(rows);
    }

    internal IQueryable<EmployeeFunctionalRoleProjectionRow> BuildQuery(Guid employeeId) =>
        from employee in dbContext.Employees.AsNoTracking()
        where employee.Id == employeeId
        join assignment in dbContext.EmployeeFunctionalRoles.AsNoTracking()
            on employee.Id equals assignment.EmployeeId into assignments
        from assignment in assignments.DefaultIfEmpty()
        join role in dbContext.FunctionalRoles.AsNoTracking()
            on assignment.FunctionalRoleId equals role.Id into roles
        from role in roles.DefaultIfEmpty()
        orderby assignment.FunctionalRoleId
        select new EmployeeFunctionalRoleProjectionRow(
            employee.Id,
            employee.IsActive,
            assignment == null ? null : assignment.FunctionalRoleId,
            assignment == null ? null : assignment.IsActive,
            role == null ? null : role.Code,
            role == null ? null : role.IsActive);
}
