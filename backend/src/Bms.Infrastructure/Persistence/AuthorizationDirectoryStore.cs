using System.Data;
using Bms.Application.Authorization;
using Bms.Application.Abstractions;
using Bms.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// The AD actor link is an explicit, audited directory writer. It only edits
/// the existing projection table; it cannot assign SYSTEM_ADMIN or any
/// Windows group claim.
/// </summary>
public sealed partial class AuthorizationDirectoryStore(BmsDbContext dbContext, ICurrentActor actor) : IAuthorizationDirectoryStore
{
    private static readonly StringComparer Ordinal = StringComparer.Ordinal;

    public async Task<AuthorizationDirectoryEmployee> CreateEmployeeAsync(
        string code,
        string name,
        string? actorExternalId,
        bool employeeIsActive,
        CancellationToken cancellationToken)
    {
        EnsureSerializable();
        var duplicateCode = await dbContext.Employees.FromSqlInterpolated($"""
            SELECT [employee].*
            FROM [bms_web].[Employees] AS [employee] WITH (UPDLOCK, HOLDLOCK, INDEX([UQ_bms_web_Employees_Code]))
            WHERE [employee].[Code] = {code}
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (duplicateCode is not null)
        {
            throw new AuthorizationDirectoryCodeConflictException();
        }

        if (actorExternalId is not null)
        {
            var duplicateActor = await dbContext.EmployeeActorMappings.FromSqlInterpolated($"""
                SELECT [mapping].*
                FROM [bms_web].[EmployeeActorMappings] AS [mapping] WITH (UPDLOCK, HOLDLOCK, INDEX([PK_bms_web_EmployeeActorMappings]))
                WHERE [mapping].[ActorExternalId] = {actorExternalId}
                """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (duplicateActor is not null)
            {
                throw new AuthorizationDirectoryActorConflictException();
            }
        }

        var employee = new EmployeeReference
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            IsActive = employeeIsActive,
        };
        dbContext.Employees.Add(employee);
        var mappings = Array.Empty<ActorMappingRow>();
        if (actorExternalId is not null)
        {
            dbContext.EmployeeActorMappings.Add(new EmployeeActorMapping
            {
                ActorExternalId = actorExternalId,
                EmployeeId = employee.Id,
                IsActive = true,
            });
            mappings = [new ActorMappingRow(actorExternalId, true)];
            if (employeeIsActive)
                await EnsureActiveOrganizationGrants431Async(actorExternalId, cancellationToken);
        }

        return ToEmployee(employee, mappings, Array.Empty<string>());
    }

    public async Task<AuthorizationDirectoryEmployeePage> ListEmployeesAsync(
        string query,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        EnsureSerializable();
        var employees = dbContext.Employees.AsNoTracking();
        if (query.Length > 0)
        {
            employees = employees.Where(employee =>
                employee.Code.Contains(query) || employee.Name.Contains(query));
        }

        var total = await employees.CountAsync(cancellationToken);
        var employeesPage = await employees
            .OrderBy(employee => employee.Code)
            .ThenBy(employee => employee.Id)
            .Skip(skip)
            .Take(take)
            .ToArrayAsync(cancellationToken);

        var employeeIds = employeesPage.Select(employee => employee.Id).ToArray();
        var mappingRows = await dbContext.EmployeeActorMappings.AsNoTracking()
            .Where(mapping => employeeIds.Contains(mapping.EmployeeId))
            .OrderBy(mapping => mapping.EmployeeId)
            .ThenBy(mapping => mapping.ActorExternalId)
            .Select(mapping => new
            {
                mapping.EmployeeId,
                mapping.ActorExternalId,
                mapping.IsActive
            })
            .ToArrayAsync(cancellationToken);
        var mappingsByEmployee = mappingRows
            .GroupBy(row => row.EmployeeId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => new ActorMappingRow(
                    row.ActorExternalId,
                    row.IsActive)).ToArray());
        var roleRows = await (
            from assignment in dbContext.EmployeeFunctionalRoles.AsNoTracking()
            join role in dbContext.FunctionalRoles.AsNoTracking()
                on assignment.FunctionalRoleId equals role.Id
            where employeeIds.Contains(assignment.EmployeeId)
                && assignment.IsActive
                && role.IsActive
            orderby assignment.EmployeeId, role.Name, role.Code
            select new
            {
                assignment.EmployeeId,
                role.Name,
            }).ToArrayAsync(cancellationToken);
        var rolesByEmployee = roleRows
            .GroupBy(row => row.EmployeeId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.Name)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray());
        var items = employeesPage.Select(employee => ToListItem(
            employee,
            mappingsByEmployee.TryGetValue(employee.Id, out var mappings)
                ? mappings
                : Array.Empty<ActorMappingRow>(),
            rolesByEmployee.TryGetValue(employee.Id, out var roles)
                ? roles
                : Array.Empty<string>())).ToArray();
        return new AuthorizationDirectoryEmployeePage(items, skip, take, total);
    }

    public async Task<AuthorizationDirectoryEmployee?> ReadAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        EnsureSerializable();
        var employee = await dbContext.Employees.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == employeeId, cancellationToken);
        if (employee is null) return null;
        var mappings = await ReadMappingsAsync(employeeId, cancellationToken);
        var roles = await ReadRolesAsync(employeeId, cancellationToken);
        return ToEmployee(employee, mappings, roles);
    }

    public async Task<bool> LockEmployeeAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        EnsureSerializable();
        var employee = await dbContext.Employees.FromSqlInterpolated($"""
            SELECT [employee].*
            FROM [bms_web].[Employees] AS [employee] WITH (UPDLOCK, HOLDLOCK, INDEX([PK_bms_web_Employees]))
            WHERE [employee].[Id] = {employeeId}
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (employee is null) return false;

        // Lock every existing mapping for the employee before changing the
        // actor key. Serializable alone does not make a key replacement
        // self-documenting to reviewers.
        _ = await dbContext.EmployeeActorMappings.FromSqlInterpolated($"""
            SELECT [mapping].*
            FROM [bms_web].[EmployeeActorMappings] AS [mapping] WITH (UPDLOCK, HOLDLOCK)
            WHERE [mapping].[EmployeeId] = {employeeId}
            ORDER BY [mapping].[ActorExternalId]
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        return true;
    }

    public async Task<AuthorizationDirectoryEmployee> ReplaceActorMappingAsync(
        Guid employeeId,
        string? actorExternalId,
        bool isActive,
        CancellationToken cancellationToken)
    {
        EnsureSerializable();
        var employee = await dbContext.Employees.SingleOrDefaultAsync(
            row => row.Id == employeeId,
            cancellationToken) ?? throw new AuthorizationDirectoryEmployeeNotFoundException();
        // The service has locked this employee. Serializable holds the raw
        // assignment/role reads until commit, including mapping clear/disable.
        var activeCodes = await (from assignment in dbContext.EmployeeFunctionalRoles.AsNoTracking()
            join role in dbContext.FunctionalRoles.AsNoTracking() on assignment.FunctionalRoleId equals role.Id
            where assignment.EmployeeId == employeeId && assignment.IsActive && role.IsActive
            select role.Code).ToArrayAsync(cancellationToken);
        AuthorizationDirectoryTechnicalRolePolicy.RequireMappingPermission(activeCodes, actor.IsBmsAdministrator);
        if (isActive && !employee.IsActive)
        {
            throw new DomainRuleViolationException(
                "An active actor mapping requires an active employee.");
        }

        var mappings = await dbContext.EmployeeActorMappings
            .Where(mapping => mapping.EmployeeId == employeeId)
            .OrderBy(mapping => mapping.ActorExternalId)
            .ToListAsync(cancellationToken);

        if (actorExternalId is not null)
        {
            var conflicting = await dbContext.EmployeeActorMappings
                .AsNoTracking()
                .SingleOrDefaultAsync(mapping => mapping.ActorExternalId == actorExternalId,
                    cancellationToken);
            if (conflicting is not null && conflicting.EmployeeId != employeeId)
            {
                throw new AuthorizationDirectoryActorConflictException();
            }
        }

        foreach (var mapping in mappings)
        {
            mapping.IsActive = false;
        }

        if (actorExternalId is not null)
        {
            var current = mappings.SingleOrDefault(mapping =>
                Ordinal.Equals(mapping.ActorExternalId, actorExternalId));
            if (current is null)
            {
                current = await dbContext.EmployeeActorMappings.SingleOrDefaultAsync(
                    mapping => mapping.ActorExternalId == actorExternalId,
                    cancellationToken);
            }

            if (current is null)
            {
                dbContext.EmployeeActorMappings.Add(new EmployeeActorMapping
                {
                    ActorExternalId = actorExternalId,
                    EmployeeId = employeeId,
                    IsActive = isActive
                });
            }
            else
            {
                current.EmployeeId = employeeId;
                current.IsActive = isActive;
            }
            if (isActive)
                await EnsureActiveOrganizationGrants431Async(actorExternalId, cancellationToken);
        }

        // The service owns the commit. Build the post-image from the tracked
        // graph, including a newly Added row which a database query is not
        // required to return before SaveChangesAsync.
        var afterMappings = mappings
            .Select(mapping => new ActorMappingRow(mapping.ActorExternalId, mapping.IsActive))
            .ToList();
        if (actorExternalId is not null && afterMappings.All(row =>
                !Ordinal.Equals(row.ActorExternalId, actorExternalId)))
        {
            afterMappings.Add(new ActorMappingRow(actorExternalId, isActive));
        }

        var roles = await ReadRolesAsync(employeeId, cancellationToken);
        return ToEmployee(employee, afterMappings, roles);
    }

    public async Task<IReadOnlyList<AuthorizationDirectoryRoleOption>> ListRolesAsync(
        CancellationToken cancellationToken)
    {
        EnsureSerializable();
        var roles = await dbContext.FunctionalRoles.AsNoTracking()
            .Where(role => role.IsActive)
            .OrderBy(role => role.Name)
            .ThenBy(role => role.Code)
            .Select(role => new AuthorizationDirectoryRoleOption(role.Id, role.Code, role.Name))
            .ToArrayAsync(cancellationToken);
        return roles.Where(role => AuthorizationDirectoryTechnicalRolePolicy.CanOfferRole(
            role.Code, actor.IsBmsAdministrator)).ToArray();
    }

    public async Task<bool> LockRoleAssignmentAsync(
        Guid employeeId,
        Guid roleId,
        CancellationToken cancellationToken)
    {
        EnsureSerializable();
        var employee = await dbContext.Employees.FromSqlInterpolated($"""
            SELECT [employee].*
            FROM [bms_web].[Employees] AS [employee] WITH (UPDLOCK, HOLDLOCK, INDEX([PK_bms_web_Employees]))
            WHERE [employee].[Id] = {employeeId}
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (employee is null) return false;

        _ = await dbContext.FunctionalRoles.FromSqlInterpolated($"""
            SELECT [role].*
            FROM [bms_web].[FunctionalRoles] AS [role] WITH (UPDLOCK, HOLDLOCK, INDEX([PK_bms_web_FunctionalRoles]))
            WHERE [role].[Id] = {roleId}
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainRuleViolationException("The functional role is unavailable.");

        _ = await dbContext.EmployeeFunctionalRoles.FromSqlInterpolated($"""
            SELECT [assignment].*
            FROM [bms_web].[EmployeeFunctionalRoles] AS [assignment] WITH (UPDLOCK, HOLDLOCK)
            WHERE [assignment].[EmployeeId] = {employeeId}
              AND [assignment].[FunctionalRoleId] = {roleId}
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return true;
    }

    public async Task<AuthorizationDirectoryEmployee> ReplaceRoleAssignmentAsync(
        Guid employeeId,
        Guid roleId,
        bool isActive,
        CancellationToken cancellationToken)
    {
        EnsureSerializable();
        var employee = await dbContext.Employees.SingleOrDefaultAsync(
            row => row.Id == employeeId,
            cancellationToken) ?? throw new AuthorizationDirectoryEmployeeNotFoundException();
        var role = await dbContext.FunctionalRoles.SingleOrDefaultAsync(
            row => row.Id == roleId && row.IsActive,
            cancellationToken) ?? throw new DomainRuleViolationException("The functional role is unavailable.");
        AuthorizationDirectoryTechnicalRolePolicy.RequireAssignmentPermission(role.Code, actor.IsBmsAdministrator);
        if (isActive && !employee.IsActive)
        {
            throw new DomainRuleViolationException(
                "An inactive employee cannot receive an active functional role.");
        }

        var assignment = await dbContext.EmployeeFunctionalRoles.SingleOrDefaultAsync(
            row => row.EmployeeId == employeeId && row.FunctionalRoleId == roleId,
            cancellationToken);
        if (assignment is null)
        {
            dbContext.EmployeeFunctionalRoles.Add(new EmployeeFunctionalRole
            {
                EmployeeId = employeeId,
                FunctionalRoleId = roleId,
                IsActive = isActive,
            });
        }
        else
        {
            assignment.IsActive = isActive;
        }

        var mappings = await ReadMappingsAsync(employeeId, cancellationToken);
        var roles = await ReadRolesAsync(employeeId, cancellationToken);
        if (isActive && !roles.Contains(role.Name, StringComparer.Ordinal))
        {
            roles = roles.Concat([role.Name]).Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal).ToArray();
        }
        else if (!isActive && roles.Contains(role.Name, StringComparer.Ordinal))
        {
            roles = roles.Where(name => !StringComparer.Ordinal.Equals(name, role.Name)).ToArray();
        }
        return ToEmployee(employee, mappings, roles);
    }

    public async Task<AuthorizationDirectoryEmployee> CreateEmployeeFromAdAsync(
        string code,
        string name,
        string actorExternalId,
        Guid roleId,
        CancellationToken cancellationToken)
    {
        EnsureSerializable();
        var duplicateCode = await dbContext.Employees.FromSqlInterpolated($"""
            SELECT [employee].*
            FROM [bms_web].[Employees] AS [employee] WITH (UPDLOCK, HOLDLOCK, INDEX([UQ_bms_web_Employees_Code]))
            WHERE [employee].[Code] = {code}
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (duplicateCode is not null) throw new AuthorizationDirectoryCodeConflictException();
        var duplicateActor = await dbContext.EmployeeActorMappings.FromSqlInterpolated($"""
            SELECT [mapping].*
            FROM [bms_web].[EmployeeActorMappings] AS [mapping] WITH (UPDLOCK, HOLDLOCK, INDEX([PK_bms_web_EmployeeActorMappings]))
            WHERE [mapping].[ActorExternalId] = {actorExternalId}
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (duplicateActor is not null)
        {
            throw new AuthorizationDirectoryActorConflictException();
        }

        var role = await dbContext.FunctionalRoles.SingleOrDefaultAsync(
            row => row.Id == roleId && row.IsActive,
            cancellationToken) ?? throw new DomainRuleViolationException("The functional role is unavailable.");
        AuthorizationDirectoryTechnicalRolePolicy.RequireAssignmentPermission(role.Code, actor.IsBmsAdministrator);
        var employee = new EmployeeReference
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            IsActive = true,
        };
        dbContext.Employees.Add(employee);
        dbContext.EmployeeActorMappings.Add(new EmployeeActorMapping
        {
            ActorExternalId = actorExternalId,
            EmployeeId = employee.Id,
            IsActive = true,
        });
        dbContext.EmployeeFunctionalRoles.Add(new EmployeeFunctionalRole
        {
            EmployeeId = employee.Id,
            FunctionalRoleId = role.Id,
            IsActive = true,
        });
        await EnsureActiveOrganizationGrants431Async(actorExternalId, cancellationToken);
        return ToEmployee(employee,
            [new ActorMappingRow(actorExternalId, true)],
            [role.Name]);
    }

    private async Task<ActorMappingRow[]> ReadMappingsAsync(
        Guid employeeId,
        CancellationToken cancellationToken) => await dbContext.EmployeeActorMappings
        .AsNoTracking()
        .Where(mapping => mapping.EmployeeId == employeeId)
        .OrderBy(mapping => mapping.ActorExternalId)
        .Select(mapping => new ActorMappingRow(mapping.ActorExternalId, mapping.IsActive))
        .ToArrayAsync(cancellationToken);

    private async Task<string[]> ReadRolesAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var names = await (
            from assignment in dbContext.EmployeeFunctionalRoles.AsNoTracking()
            join role in dbContext.FunctionalRoles.AsNoTracking()
                on assignment.FunctionalRoleId equals role.Id
            where assignment.EmployeeId == employeeId
                && assignment.IsActive
                && role.IsActive
            orderby role.Name, role.Code
            select role.Name).ToArrayAsync(cancellationToken);

        return names
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static AuthorizationDirectoryEmployeeListItem ToListItem(
        EmployeeReference employee,
        IEnumerable<ActorMappingRow> mappings,
        IEnumerable<string> roles)
    {
        var (actorExternalId, isActive) = CurrentMapping(mappings);
        return new AuthorizationDirectoryEmployeeListItem(
            employee.Id,
            employee.Code,
            employee.Name,
            employee.IsActive,
            actorExternalId,
            isActive,
            roles.ToArray(),
            AuthorizationDirectoryStateHash.Compute(
                employee.Id,
                employee.Code,
                employee.Name,
                employee.IsActive,
                mappings.Select(row => (row.ActorExternalId, row.IsActive)),
                roles));
    }

    private static AuthorizationDirectoryEmployee ToEmployee(
        EmployeeReference employee,
        IEnumerable<ActorMappingRow> mappings,
        IEnumerable<string> roles)
    {
        var materialized = mappings.ToArray();
        var (actorExternalId, isActive) = CurrentMapping(materialized);
        return new AuthorizationDirectoryEmployee(
            employee.Id,
            employee.Code,
            employee.Name,
            employee.IsActive,
            actorExternalId,
            isActive,
            roles.ToArray(),
            AuthorizationDirectoryStateHash.Compute(
                employee.Id,
                employee.Code,
                employee.Name,
                employee.IsActive,
                materialized.Select(row => (row.ActorExternalId, row.IsActive)),
                roles));
    }

    private static (string? ActorExternalId, bool IsActive) CurrentMapping(
        IEnumerable<ActorMappingRow> rows)
    {
        var materialized = rows.ToArray();
        var active = materialized.Where(row => row.IsActive).ToArray();
        if (active.Length > 1)
        {
            throw new AuthorizationDirectoryUnavailableException();
        }

        return active.Length == 1
            ? (active[0].ActorExternalId, true)
            : (null, false);
    }

    private void EnsureSerializable()
    {
        var transaction = dbContext.Database.CurrentTransaction;
        if (transaction is null || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
        {
            throw new InvalidOperationException(
                "Authorization-directory operations require an active Serializable transaction.");
        }
    }

    private sealed record ActorMappingRow(string ActorExternalId, bool IsActive);
}
