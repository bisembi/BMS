using Bms.Application.Authorization;

namespace Bms.Application.Abstractions;

/// <summary>
/// Reads the authoritative mapping of one already canonical SSO actor to an
/// employee. It does not accept claims, headers or a descriptive source username.
/// </summary>
public interface IEmployeeActorMappingReader
{
    Task<IReadOnlyList<EmployeeActorMappingProjectionRow>> ReadAsync(
        string actorExternalId,
        CancellationToken cancellationToken);
}
