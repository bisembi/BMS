using Bms.Application.Abstractions;

namespace Bms.Application.Authorization;

/// <summary>
/// Resolves only the current SQL015 projection for an already-authoritative
/// employee UUID. It does not map an SSO actor and does not assign roles.
/// </summary>
public sealed class EmployeeFunctionalRoleResolver(
    IEmployeeFunctionalRoleAssignmentReader reader,
    IActiveRoleSelection? activeRoleSelection = null)
{
    internal bool UsesActiveRoleSelection => activeRoleSelection is not null;

    // Only the already-authorized deputy path may inspect the immutable
    // principal's assignment inventory. It cannot widen the deputy's role.
    internal async Task<EmployeeFunctionalRoleResolver> ForAuthorizedSubstitutionPrincipalAsync(
        ICurrentActor actor, CancellationToken cancellationToken)
    {
        if (activeRoleSelection is not null)
        {
            if (!activeRoleSelection.IsInitialized
                || activeRoleSelection.ActorExternalId != actor.ExternalId
                || activeRoleSelection.EmployeeId is not Guid employeeId)
                throw new Bms.Application.Common.ActorForbiddenException();
            var effective = await ResolveAsync(employeeId, cancellationToken);
            if (!effective.HasExactRole(Bms.Domain.Workflow.ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode)
                && !effective.HasExactRole(Bms.Domain.Workflow.ExpenseRequestWorkflowCodes.CfoInitiatorRoleCode)
                && !effective.HasExactRole(Bms.Domain.Workflow.ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode)
                && !effective.HasExactRole(Bms.Domain.Workflow.ExpenseRequestWorkflowCodes.FinancialControllerRoleCode))
                throw new Bms.Application.Common.ActorForbiddenException();
        }
        return new EmployeeFunctionalRoleResolver(reader);
    }
    public async Task<EmployeeFunctionalRoleResolution> ResolveAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var assigned = await ResolveAssignedAsync(employeeId, cancellationToken);

        // Directly constructed legacy/test resolvers intentionally retain the
        // raw assignment contract.  Production DI always supplies the scoped
        // selector, and therefore never falls back to a union of assignments.
        if (activeRoleSelection is null)
        {
            return assigned;
        }

        if (!activeRoleSelection.IsInitialized
            || activeRoleSelection.EmployeeId != employeeId
            || activeRoleSelection.ActiveRoleCode is not string selectedRoleCode)
        {
            return WithoutEffectiveRoles(assigned);
        }

        var backingAssignment = ActiveRoleCatalog.FindActiveBackingAssignment(
            selectedRoleCode,
            assigned.ActiveRoleCodes);
        return backingAssignment is null
            ? WithoutEffectiveRoles(assigned)
            : new EmployeeFunctionalRoleResolution(
                assigned.EmployeeId,
                assigned.EmployeeExists,
                assigned.EmployeeIsActive,
                Array.AsReadOnly(new[] { backingAssignment }));
    }

    /// <summary>
    /// Reads every current server assignment.  This raw inventory is reserved
    /// for request-boundary initialization and other-employee catalogues; all
    /// current-actor business authorization must call <see cref="ResolveAsync"/>.
    /// </summary>
    public async Task<EmployeeFunctionalRoleResolution> ResolveAssignedAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (employeeId == Guid.Empty)
            {
                throw new EmployeeFunctionalRoleResolutionUnavailableException();
            }
            cancellationToken.ThrowIfCancellationRequested();
            var rows = await reader.ReadAsync(employeeId, cancellationToken);
            if (rows is null)
            {
                throw new EmployeeFunctionalRoleResolutionUnavailableException();
            }
            if (rows.Count == 0)
            {
                return new EmployeeFunctionalRoleResolution(
                    employeeId,
                    employeeExists: false,
                    employeeIsActive: false,
                    Array.Empty<string>());
            }

            if (rows.Any(row => row is null))
            {
                throw new EmployeeFunctionalRoleResolutionUnavailableException();
            }
            var employeeIsActive = rows[0].EmployeeIsActive;
            if (rows.Any(row => row.EmployeeId != employeeId
                    || row.EmployeeIsActive != employeeIsActive))
            {
                throw new EmployeeFunctionalRoleResolutionUnavailableException();
            }

            var sentinelRows = rows
                .Where(row => row.FunctionalRoleId is null)
                .ToArray();
            if (sentinelRows.Length != 0)
            {
                if (rows.Count != 1
                    || sentinelRows.Length != 1
                    || sentinelRows[0].AssignmentIsActive is not null
                    || sentinelRows[0].RoleCode is not null
                    || sentinelRows[0].RoleIsActive is not null)
                {
                    throw new EmployeeFunctionalRoleResolutionUnavailableException();
                }
                return new EmployeeFunctionalRoleResolution(
                    employeeId,
                    employeeExists: true,
                    employeeIsActive,
                    Array.Empty<string>());
            }

            var roleIds = new HashSet<Guid>();
            var roleCodes = new HashSet<string>(StringComparer.Ordinal);
            var activeRoleCodes = new List<string>();
            foreach (var row in rows)
            {
                if (row.FunctionalRoleId is not Guid roleId
                    || roleId == Guid.Empty
                    || !roleIds.Add(roleId)
                    || row.AssignmentIsActive is not bool assignmentIsActive
                    || row.RoleIsActive is not bool roleIsActive
                    || row.RoleCode is not string roleCode)
                {
                    throw new EmployeeFunctionalRoleResolutionUnavailableException();
                }
                ValidateRoleCode(roleCode);
                if (!roleCodes.Add(roleCode))
                {
                    throw new EmployeeFunctionalRoleResolutionUnavailableException();
                }
                if (assignmentIsActive && (!employeeIsActive || !roleIsActive))
                {
                    throw new EmployeeFunctionalRoleResolutionUnavailableException();
                }
                if (assignmentIsActive && roleIsActive)
                {
                    activeRoleCodes.Add(roleCode);
                }
            }

            activeRoleCodes.Sort(StringComparer.Ordinal);
            return new EmployeeFunctionalRoleResolution(
                employeeId,
                employeeExists: true,
                employeeIsActive,
                Array.AsReadOnly(activeRoleCodes.ToArray()));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (EmployeeFunctionalRoleResolutionUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new EmployeeFunctionalRoleResolutionUnavailableException(exception);
        }
    }

    private static EmployeeFunctionalRoleResolution WithoutEffectiveRoles(
        EmployeeFunctionalRoleResolution assigned) => new(
            assigned.EmployeeId,
            assigned.EmployeeExists,
            assigned.EmployeeIsActive,
            Array.Empty<string>());

    internal static void ValidateRoleCode(string roleCode)
    {
        if (string.IsNullOrWhiteSpace(roleCode)
            || roleCode.Length > 30
            || !string.Equals(roleCode, roleCode.Trim(), StringComparison.Ordinal)
            || roleCode.Any(char.IsControl))
        {
            throw new EmployeeFunctionalRoleResolutionUnavailableException();
        }
    }
}
