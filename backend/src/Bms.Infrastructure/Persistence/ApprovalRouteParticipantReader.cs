using Bms.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>Reads active employees assigned to each requested functional role.</summary>
public sealed class ApprovalRouteParticipantReader(BmsDbContext dbContext)
    : IApprovalRouteParticipantReader
{
    public async Task<IReadOnlyList<ApprovalRouteParticipantProjectionRow>> ReadAsync(
        IReadOnlyCollection<string> roleCodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roleCodes);
        var normalizedRoleCodes = roleCodes
            .Where(roleCode => !string.IsNullOrWhiteSpace(roleCode))
            .Select(roleCode => roleCode.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(roleCode => roleCode, StringComparer.Ordinal)
            .ToArray();
        if (normalizedRoleCodes.Length != roleCodes.Count)
        {
            return [];
        }

        var rows = await (
            from employee in dbContext.Employees.AsNoTracking()
            join assignment in dbContext.EmployeeFunctionalRoles.AsNoTracking()
                on employee.Id equals assignment.EmployeeId
            join role in dbContext.FunctionalRoles.AsNoTracking()
                on assignment.FunctionalRoleId equals role.Id
            where employee.IsActive
                && assignment.IsActive
                && role.IsActive
                && normalizedRoleCodes.Contains(role.Code)
            orderby role.Code, employee.Id
            select new ApprovalRouteParticipantProjectionRow(employee.Id, role.Code))
            .ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(rows);
    }
}
