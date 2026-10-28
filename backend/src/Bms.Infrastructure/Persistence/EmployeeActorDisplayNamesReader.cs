using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class EmployeeActorDisplayNamesReader(BmsDbContext db) : IEmployeeActorDisplayNamesReader
{
    public async Task<IReadOnlyList<EmployeeActorMappingProjectionRow>> ReadAsync(
        IReadOnlyCollection<string> actorExternalIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actorExternalIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (actorExternalIds.Count > 100 || actorExternalIds.Any(a => !ActorIdentity.IsCanonical(a)))
            throw new ArgumentException("Invalid presentation actor page.", nameof(actorExternalIds));
        var actors = actorExternalIds.Distinct(StringComparer.Ordinal).ToArray();
        if (actors.Length == 0) return [];
        var rows = await (from mapping in db.EmployeeActorMappings.AsNoTracking()
            where actors.Contains(mapping.ActorExternalId)
            join employee in db.Employees.AsNoTracking() on mapping.EmployeeId equals employee.Id
            select new EmployeeActorMappingProjectionRow(mapping.ActorExternalId, mapping.EmployeeId,
                mapping.IsActive, employee.IsActive, employee.Name)).ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(rows);
    }
}
