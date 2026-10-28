using System.DirectoryServices.Protocols;
using System.Net;
using System.Text;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Reads enabled user accounts from the domain controller using the Windows
/// identity of the service. It is deliberately read-only; BMS remains the
/// authority for local employee rows and functional-role assignments.
/// </summary>
public sealed partial class ActiveDirectoryEmployeeDirectory(
    IConfiguration configuration,
    BmsDbContext dbContext) : IActiveDirectoryEmployeeDirectory
{
    private const string DisabledAccountFilter =
        "(!(userAccountControl:1.2.840.113556.1.4.803:=2))";
    private const int MaximumDirectoryRows = 10_000;

    public async Task<ActiveDirectoryEmployeePage> SearchAsync(
        string query,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var candidates = await ReadDirectoryAsync(query, cancellationToken);
        var enriched = await EnrichAsync(candidates, cancellationToken);
        return new ActiveDirectoryEmployeePage(
            enriched.Skip(skip).Take(take).ToArray(), skip, take, enriched.Count);
    }

    public async Task<ActiveDirectoryEmployeeCandidate?> FindByActorAsync(
        string actorExternalId,
        CancellationToken cancellationToken)
    {
        var candidates = await ReadDirectoryAsync(string.Empty, cancellationToken);
        var match = candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.ActorExternalId, actorExternalId, StringComparison.OrdinalIgnoreCase));
        if (match is null) return null;
        return (await EnrichAsync([match], cancellationToken)).Single();
    }

    private async Task<IReadOnlyList<RawDirectoryEmployee>> ReadDirectoryAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled())
        {
            throw new AuthorizationDirectoryUnavailableException();
        }

        return await ReadDirectoryCoreAsync(query, cancellationToken);
    }

    private async Task<IReadOnlyList<RawDirectoryEmployee>> ReadDirectoryCoreAsync(
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            using var connection = CreateConnection();
            connection.Bind();
            var baseDn = await ResolveBaseDnAsync(connection, cancellationToken);
            var filter = BuildFilter(query);
            var request = new SearchRequest(
                baseDn,
                filter,
                SearchScope.Subtree,
                "objectSid", "sAMAccountName", "displayName", "userPrincipalName");
            var pageSize = Math.Clamp(configuration.GetValue("BmsSecurity:ActiveDirectory:PageSize", 500), 50, 1000);
            var cookie = Array.Empty<byte>();
            var result = new List<RawDirectoryEmployee>();
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                request.Controls.Clear();
                request.Controls.Add(new PageResultRequestControl(pageSize) { Cookie = cookie });
                var response = (SearchResponse)await SendRequestAsync(
                    connection,
                    request,
                    cancellationToken);
                foreach (SearchResultEntry entry in response.Entries)
                {
                    var actorExternalId = ReadSid(entry);
                    var accountName = ReadText(entry, "sAMAccountName");
                    if (actorExternalId is null || string.IsNullOrWhiteSpace(accountName)) continue;
                    var name = ReadText(entry, "displayName")
                               ?? ReadText(entry, "userPrincipalName")
                               ?? accountName;
                    result.Add(new RawDirectoryEmployee(actorExternalId, accountName, name));
                    if (result.Count >= MaximumDirectoryRows) break;
                }
                var page = response.Controls.OfType<PageResultResponseControl>().SingleOrDefault();
                cookie = page?.Cookie ?? Array.Empty<byte>();
            }
            while (cookie.Length > 0 && result.Count < MaximumDirectoryRows);

            return result
                .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.AccountName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (OperationCanceledException) { throw; }
        catch (AuthorizationDirectoryUnavailableException) { throw; }
        catch (Exception exception)
        {
            throw new AuthorizationDirectoryUnavailableException(exception);
        }
    }

    private async Task<IReadOnlyList<ActiveDirectoryEmployeeCandidate>> EnrichAsync(
        IReadOnlyList<RawDirectoryEmployee> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return Array.Empty<ActiveDirectoryEmployeeCandidate>();
        var sids = rows.Select(row => row.ActorExternalId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var mappings = await dbContext.EmployeeActorMappings.AsNoTracking()
            .Where(row => sids.Contains(row.ActorExternalId))
            .Select(row => new { row.ActorExternalId, row.EmployeeId, row.IsActive })
            .ToArrayAsync(cancellationToken);
        var employeeIds = mappings.Select(row => row.EmployeeId).Distinct().ToArray();
        var employees = await dbContext.Employees.AsNoTracking()
            .Where(row => employeeIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var roleRows = await (
            from assignment in dbContext.EmployeeFunctionalRoles.AsNoTracking()
            join role in dbContext.FunctionalRoles.AsNoTracking()
                on assignment.FunctionalRoleId equals role.Id
            where employeeIds.Contains(assignment.EmployeeId)
                && assignment.IsActive && role.IsActive
            select new { assignment.EmployeeId, role.Name }).ToArrayAsync(cancellationToken);
        var rolesByEmployee = roleRows.GroupBy(row => row.EmployeeId).ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<string>)group.Select(row => row.Name)
                .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        var mappingBySid = mappings
            .GroupBy(row => row.ActorExternalId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);

        return rows.Select(row =>
        {
            if (!mappingBySid.TryGetValue(row.ActorExternalId, out var mapping)
                || !employees.TryGetValue(mapping.EmployeeId, out var employee))
            {
                return new ActiveDirectoryEmployeeCandidate(
                    row.ActorExternalId, row.AccountName, row.Name, true, null, null,
                    Array.Empty<string>(), null);
            }
            return new ActiveDirectoryEmployeeCandidate(
                row.ActorExternalId, row.AccountName, employee.Name, employee.IsActive,
                employee.Id, employee.Code,
                rolesByEmployee.TryGetValue(employee.Id, out var roles) ? roles : Array.Empty<string>(),
                null);
        }).ToArray();
    }

    private LdapConnection CreateConnection()
    {
        var server = configuration["BmsSecurity:ActiveDirectory:Server"];
        if (string.IsNullOrWhiteSpace(server))
        {
            server = Environment.GetEnvironmentVariable("LOGONSERVER")?.TrimStart('\\');
        }
        if (string.IsNullOrWhiteSpace(server)) server = Environment.UserDomainName;
        if (string.IsNullOrWhiteSpace(server))
        {
            throw new InvalidOperationException("An Active Directory server is not configured.");
        }
        var port = configuration.GetValue("BmsSecurity:ActiveDirectory:Port", 389);
        var useSsl = configuration.GetValue("BmsSecurity:ActiveDirectory:UseSsl", false);
        var timeout = TimeSpan.FromSeconds(
            Math.Clamp(configuration.GetValue("BmsSecurity:ActiveDirectory:TimeoutSeconds", 10), 1, 60));
        var connection = new LdapConnection(new LdapDirectoryIdentifier(server, port, true, false));
        try
        {
            connection.AuthType = AuthType.Negotiate;
            connection.Timeout = timeout;
            connection.SessionOptions.ProtocolVersion = 3;
            if (useSsl)
            {
                // Keep the platform's certificate validation; never fall back.
                connection.SessionOptions.SecureSocketLayer = true;
            }
            else
            {
                // Negotiate authentication alone is not a channel-protection
                // requirement. Require both properties before the first Bind.
                connection.SessionOptions.Signing = true;
                connection.SessionOptions.Sealing = true;
            }
            return connection;
        }
        catch
        {
            // The caller's using begins only after this factory returns.
            connection.Dispose();
            throw;
        }
    }

    private async Task<string> ResolveBaseDnAsync(
        LdapConnection connection,
        CancellationToken cancellationToken)
    {
        var configured = configuration["BmsSecurity:ActiveDirectory:BaseDn"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        var response = (SearchResponse)await SendRequestAsync(
            connection,
            new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext"),
            cancellationToken);
        var value = ReadText(response.Entries.Cast<SearchResultEntry>().Single(), "defaultNamingContext");
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException("Active Directory defaultNamingContext is unavailable.");
    }

    private static async Task<DirectoryResponse> SendRequestAsync(
        LdapConnection connection,
        DirectoryRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var responseTask = Task.Factory.FromAsync(
            (callback, state) => connection.BeginSendRequest(
                request,
                PartialResultProcessing.NoPartialResultSupport,
                callback,
                state),
            connection.EndSendRequest,
            state: null);
        var response = await responseTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return response;
    }

    private static string BuildFilter(string query)
    {
        var escaped = EscapeFilter(query);
        return string.IsNullOrEmpty(escaped)
            ? $"(&(objectCategory=person)(objectClass=user){DisabledAccountFilter})"
            : $"(&(objectCategory=person)(objectClass=user){DisabledAccountFilter}(|(sAMAccountName=*{escaped}*)(displayName=*{escaped}*)(userPrincipalName=*{escaped}*)))";
    }

    private bool IsEnabled() => configuration.GetValue<bool?>("BmsSecurity:ActiveDirectory:Enabled")
        ?? configuration.GetValue("BmsSecurity:WindowsAuthentication:Enabled", false);

    private static string? ReadSid(SearchResultEntry entry)
    {
        if (!entry.Attributes.Contains("objectSid")) return null;
        var value = entry.Attributes["objectSid"]?[0];
        return value is byte[] bytes ? SidToString(bytes) : null;
    }

    private static string? ReadText(SearchResultEntry entry, string attribute)
    {
        if (!entry.Attributes.Contains(attribute)) return null;
        return entry.Attributes[attribute]?[0]?.ToString();
    }

    private static string SidToString(byte[] bytes)
    {
        if (bytes.Length < 8) throw new InvalidOperationException("Invalid AD objectSid.");
        var revision = bytes[0];
        var count = bytes[1];
        if (bytes.Length < 8 + count * 4) throw new InvalidOperationException("Invalid AD objectSid.");
        ulong authority = 0;
        for (var index = 2; index < 8; index++) authority = (authority << 8) | bytes[index];
        var builder = new StringBuilder($"S-{revision}-{authority}");
        for (var index = 0; index < count; index++)
        {
            var offset = 8 + index * 4;
            var subAuthority = (uint)(bytes[offset]
                | bytes[offset + 1] << 8
                | bytes[offset + 2] << 16
                | bytes[offset + 3] << 24);
            builder.Append('-').Append(subAuthority);
        }
        return builder.ToString();
    }

    private static string EscapeFilter(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\': builder.Append("\\5c"); break;
                case '*': builder.Append("\\2a"); break;
                case '(': builder.Append("\\28"); break;
                case ')': builder.Append("\\29"); break;
                case '\0': builder.Append("\\00"); break;
                default: builder.Append(character); break;
            }
        }
        return builder.ToString();
    }

    private sealed record RawDirectoryEmployee(string ActorExternalId, string AccountName, string Name);
}
