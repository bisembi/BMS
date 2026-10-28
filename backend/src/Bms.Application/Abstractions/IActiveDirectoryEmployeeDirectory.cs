using Bms.Application.Authorization;

namespace Bms.Application.Abstractions;

/// <summary>
/// Server-side read access to enabled enterprise accounts. The browser never
/// receives LDAP credentials and cannot query the directory directly.
/// </summary>
public interface IActiveDirectoryEmployeeDirectory
{
    Task<ActiveDirectoryEmployeePage> SearchAsync(
        string query,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<ActiveDirectoryEmployeeCandidate?> FindByActorAsync(
        string actorExternalId,
        CancellationToken cancellationToken);
}
