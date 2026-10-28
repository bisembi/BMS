using Bms.Application.Authorization;

namespace Bms.Application.Abstractions;

public interface IAuthorizationDirectoryStore
{
    Task<AuthorizationDirectoryEmployee> CreateEmployeeAsync(
        string code,
        string name,
        string? actorExternalId,
        bool employeeIsActive,
        CancellationToken cancellationToken);

    Task<AuthorizationDirectoryEmployeePage> ListEmployeesAsync(
        string query,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<AuthorizationDirectoryEmployee?> ReadAsync(
        Guid employeeId,
        CancellationToken cancellationToken);

    Task<bool> LockEmployeeAsync(
        Guid employeeId,
        CancellationToken cancellationToken);

    Task<AuthorizationDirectoryEmployee> ReplaceActorMappingAsync(
        Guid employeeId,
        string? actorExternalId,
        bool isActive,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AuthorizationDirectoryRoleOption>> ListRolesAsync(
        CancellationToken cancellationToken);

    Task<bool> LockRoleAssignmentAsync(
        Guid employeeId,
        Guid roleId,
        CancellationToken cancellationToken);

    Task<AuthorizationDirectoryEmployee> ReplaceRoleAssignmentAsync(
        Guid employeeId,
        Guid roleId,
        bool isActive,
        CancellationToken cancellationToken);

    Task<AuthorizationDirectoryEmployee> CreateEmployeeFromAdAsync(
        string code,
        string name,
        string actorExternalId,
        Guid roleId,
        CancellationToken cancellationToken);
}
