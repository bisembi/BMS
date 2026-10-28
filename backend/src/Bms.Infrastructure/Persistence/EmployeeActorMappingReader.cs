using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Read-only SQL projection of the explicit SSO actor-to-employee mapping.
/// </summary>
public sealed class EmployeeActorMappingReader(BmsDbContext dbContext) :
    IEmployeeActorMappingReader
{
    public async Task<IReadOnlyList<EmployeeActorMappingProjectionRow>> ReadAsync(
        string actorExternalId,
        CancellationToken cancellationToken)
    {
        var rows = await BuildQuery(actorExternalId).ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(rows);
    }

    internal IQueryable<EmployeeActorMappingProjectionRow> BuildQuery(string actorExternalId) =>
        from mapping in dbContext.EmployeeActorMappings.AsNoTracking()
        where mapping.ActorExternalId == actorExternalId
        join employee in dbContext.Employees.AsNoTracking()
            on mapping.EmployeeId equals employee.Id
        select new EmployeeActorMappingProjectionRow(
            mapping.ActorExternalId,
            mapping.EmployeeId,
            mapping.IsActive,
            employee.IsActive,
            employee.Name);
}
