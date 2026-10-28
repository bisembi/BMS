using Bms.Application.Authorization;

namespace Bms.Application.Abstractions;

/// Optional presentation lookup, not an authority resolver. Only receives the
/// distinct canonical actors of an already-authorized page (at most 100).
public interface IEmployeeActorDisplayNamesReader
{
    Task<IReadOnlyList<EmployeeActorMappingProjectionRow>> ReadAsync(
        IReadOnlyCollection<string> actors, CancellationToken cancellationToken);
}
