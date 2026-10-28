using System.Security.Cryptography;
using System.Text;

namespace Bms.Application.Authorization;

/// <summary>
/// The small, explicit directory slice used to connect a canonical Windows
/// identity to an imported employee. It deliberately does not expose or edit
/// SYSTEM_ADMIN: that capability remains an AD assignment and is checked from
/// the authenticated principal.
/// </summary>
public sealed record AuthorizationDirectoryEmployeeListItem(
    Guid EmployeeId,
    string Code,
    string Name,
    bool IsActive,
    string? ActorExternalId,
    bool ActorMappingIsActive,
    IReadOnlyList<string> Roles,
    string ConcurrencyStamp);

public sealed record AuthorizationDirectoryEmployeePage(
    IReadOnlyList<AuthorizationDirectoryEmployeeListItem> Items,
    int Skip,
    int Take,
    int Total);

public sealed record AuthorizationDirectoryEmployee(
    Guid EmployeeId,
    string Code,
    string Name,
    bool IsActive,
    string? ActorExternalId,
    bool ActorMappingIsActive,
    IReadOnlyList<string> Roles,
    string ConcurrencyStamp);

public sealed record UpdateAuthorizationDirectoryActorCommand(
    string ExpectedConcurrencyStamp,
    string? ActorExternalId,
    bool IsActive);

public sealed record CreateAuthorizationDirectoryEmployeeCommand(
    string Code,
    string Name,
    string? ActorExternalId,
    bool IsActive);

public sealed record AuthorizationDirectoryRoleOption(
    Guid RoleId,
    string Code,
    string Name);

public sealed record ActiveDirectoryEmployeeCandidate(
    string ActorExternalId,
    string AccountName,
    string Name,
    bool IsActive,
    Guid? EmployeeId,
    string? EmployeeCode,
    IReadOnlyList<string> Roles,
    string? ConcurrencyStamp);

public sealed record ActiveDirectoryEmployeePage(
    IReadOnlyList<ActiveDirectoryEmployeeCandidate> Items,
    int Skip,
    int Take,
    int Total);

public sealed record AssignAuthorizationDirectoryRoleCommand(
    string ExpectedConcurrencyStamp,
    Guid RoleId,
    bool IsActive);

public sealed record CreateAuthorizationDirectoryEmployeeFromAdCommand(
    string ActorExternalId,
    Guid RoleId);

internal static class AuthorizationDirectoryStateHash
{
    internal static string Compute(
        Guid employeeId,
        string code,
        string name,
        bool employeeIsActive,
        IEnumerable<(string ActorExternalId, bool IsActive)> mappings,
        IEnumerable<string>? roles = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(mappings);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "BMS-AD-AUTHORIZATION-DIRECTORY-V1");
        Append(hash, employeeId.ToString("D"));
        Append(hash, code);
        Append(hash, name);
        Append(hash, employeeIsActive ? "1" : "0");
        var rows = mappings
            .OrderBy(row => row.ActorExternalId, StringComparer.Ordinal)
            .ThenBy(row => row.IsActive ? 1 : 0)
            .ToArray();
        Append(hash, rows.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var row in rows)
        {
            Append(hash, row.ActorExternalId);
            Append(hash, row.IsActive ? "1" : "0");
        }

        var roleRows = (roles ?? Array.Empty<string>())
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToArray();
        Append(hash, roleRows.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var role in roleRows)
        {
            Append(hash, role);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static string Require(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value is null || value.Length != 64 || value.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
        {
            throw new Bms.Domain.Common.DomainRuleViolationException(
                "The authorization-directory concurrency stamp is invalid.");
        }

        return value;
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}

public sealed class AuthorizationDirectoryEmployeeNotFoundException : Exception
{
    public AuthorizationDirectoryEmployeeNotFoundException()
        : base("The authorization-directory employee was not found.")
    {
    }
}

public sealed class AuthorizationDirectoryActorConflictException : Exception
{
    public AuthorizationDirectoryActorConflictException()
        : base("The Windows actor is already mapped to another employee.")
    {
    }
}

public sealed class AuthorizationDirectoryCodeConflictException : Exception
{
    public AuthorizationDirectoryCodeConflictException()
        : base("The employee code is already assigned.")
    {
    }
}

public sealed class AuthorizationDirectoryStateConflictException : Exception
{
    public AuthorizationDirectoryStateConflictException()
        : base("The authorization-directory employee changed before the update.")
    {
    }
}

public sealed class AuthorizationDirectoryUnavailableException : Exception
{
    public AuthorizationDirectoryUnavailableException()
        : base("The authorization directory could not be resolved consistently.")
    {
    }

    public AuthorizationDirectoryUnavailableException(Exception innerException)
        : base("The authorization directory could not be resolved consistently.", innerException)
    {
    }
}

public sealed class AuthorizationDirectoryAdEmployeeNotFoundException : Exception
{
    public AuthorizationDirectoryAdEmployeeNotFoundException()
        : base("The active AD employee was not found.")
    {
    }
}
