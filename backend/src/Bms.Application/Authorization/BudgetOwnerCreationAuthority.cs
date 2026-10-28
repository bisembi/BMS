using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Workflow;

namespace Bms.Application.Authorization;

/// <summary>
/// Resolves the complete, authoritative chain required before a production
/// actor may create an expense request for one CFO: exact SSO actor, active
/// employee, active BUDGET_OWNER role and active non-group CFO assignment.
/// It deliberately does not grant a command, persist data or replace access
/// scope; callers still decide when this prerequisite becomes active.
/// </summary>
public sealed partial class BudgetOwnerCreationAuthority(
    EmployeeActorResolver employeeActorResolver,
    EmployeeFunctionalRoleResolver employeeFunctionalRoleResolver,
    IBudgetOwnerCostCenterAuthorityReader assignmentReader,
    INsiWorkQueueScopeReader? nsiWorkQueueScopeReader = null,
    IPayrollRequestScope372? payrollRequestScope372 = null)
{
    /// <summary>Use only after proving active substitution for the document's
    /// immutable initiator. Current actor remains restricted to a creator role.</summary>
    public async Task<BudgetOwnerCreationAuthority> ForAuthorizedSubstitutionPrincipalAsync(
        ICurrentActor actor, CancellationToken cancellationToken) =>
        new(employeeActorResolver,
            await employeeFunctionalRoleResolver.ForAuthorizedSubstitutionPrincipalAsync(actor, cancellationToken),
            assignmentReader.ForAuthorizedPrincipal());
    /// <summary>
    /// Resolves the authority for one selected header CFO. The selected CFO is
    /// authoritative for every line; callers must not replace this check with
    /// a broader access-scope decision.
    /// </summary>
    public async Task<BudgetOwnerCreationAuthorityResolution> ResolveAsync(
        string actorExternalId,
        Guid costCenterId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!ActorIdentity.IsCanonical(actorExternalId) || costCenterId == Guid.Empty)
            {
                throw new BudgetOwnerCreationAuthorityUnavailableException();
            }

            var employeeId = await ResolveEligibleEmployeeAsync(
                actorExternalId,
                cancellationToken);
            if (employeeId is not Guid resolvedEmployeeId)
            {
                return BudgetOwnerCreationAuthorityResolution.Denied;
            }

            var rows = await assignmentReader.ReadCreatorsAsync(
                resolvedEmployeeId,
                costCenterId,
                cancellationToken);
            return ResolveExactAssignment(
                resolvedEmployeeId,
                costCenterId,
                rows);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (EmployeeActorResolutionUnavailableException exception)
        {
            throw new BudgetOwnerCreationAuthorityUnavailableException(exception);
        }
        catch (EmployeeFunctionalRoleResolutionUnavailableException exception)
        {
            throw new BudgetOwnerCreationAuthorityUnavailableException(exception);
        }
        catch (BudgetOwnerCreationAuthorityUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new BudgetOwnerCreationAuthorityUnavailableException(exception);
        }
    }

    /// <summary>
    /// Resolves whether a production actor has at least one active,
    /// non-group CFO assignment. This is used only for actor/session
    /// capability and for incomplete drafts whose header CFO has not yet been
    /// selected; a concrete draft command still calls <see cref="ResolveAsync"/>
    /// for its selected header CFO.
    /// </summary>
    public async Task<BudgetOwnerCreationAuthorityResolution> ResolveAnyAsync(
        string actorExternalId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!ActorIdentity.IsCanonical(actorExternalId))
            {
                throw new BudgetOwnerCreationAuthorityUnavailableException();
            }

            var employeeId = await ResolveEligibleEmployeeAsync(
                actorExternalId,
                cancellationToken);
            if (employeeId is not Guid resolvedEmployeeId)
            {
                return BudgetOwnerCreationAuthorityResolution.Denied;
            }

            var rows = await assignmentReader.ReadCreatorsForEmployeeAsync(
                resolvedEmployeeId,
                cancellationToken);
            return ResolveAnyAssignment(resolvedEmployeeId, rows);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (EmployeeActorResolutionUnavailableException exception)
        {
            throw new BudgetOwnerCreationAuthorityUnavailableException(exception);
        }
        catch (EmployeeFunctionalRoleResolutionUnavailableException exception)
        {
            throw new BudgetOwnerCreationAuthorityUnavailableException(exception);
        }
        catch (BudgetOwnerCreationAuthorityUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new BudgetOwnerCreationAuthorityUnavailableException(exception);
        }
    }

    private async Task<Guid?> ResolveEligibleEmployeeAsync(
        string actorExternalId,
        CancellationToken cancellationToken)
    {
        var actor = await employeeActorResolver.ResolveAsync(
            actorExternalId,
            cancellationToken);
        if (!actor.HasActiveEmployee)
        {
            return null;
        }

        if (actor.EmployeeId is not Guid employeeId)
        {
            throw new BudgetOwnerCreationAuthorityUnavailableException();
        }

        var roles = await employeeFunctionalRoleResolver.ResolveAsync(
            employeeId,
            cancellationToken);
        return (roles.HasExactRole(ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode)
            || roles.HasExactRole(ExpenseRequestWorkflowCodes.CfoInitiatorRoleCode))
            ? employeeId
            : null;
    }

    private static BudgetOwnerCreationAuthorityResolution ResolveExactAssignment(
        Guid employeeId,
        Guid costCenterId,
        IReadOnlyList<BudgetOwnerCostCenterAuthorityProjectionRow> rows)
    {
        if (rows is null || rows.Any(row => row is null) || rows.Count > 1)
        {
            throw new BudgetOwnerCreationAuthorityUnavailableException();
        }

        if (rows.Count == 0)
        {
            return BudgetOwnerCreationAuthorityResolution.Denied;
        }

        var row = rows[0];
        if (row.EmployeeId != employeeId ||
            row.CostCenterId != costCenterId ||
            row.CostCenterId == Guid.Empty ||
            !row.EmployeeIsActive)
        {
            throw new BudgetOwnerCreationAuthorityUnavailableException();
        }

        return row.AssignmentIsActive && row.CostCenterIsActive && !row.CostCenterIsGroup
            ? new BudgetOwnerCreationAuthorityResolution(employeeId)
            : BudgetOwnerCreationAuthorityResolution.Denied;
    }

    private static BudgetOwnerCreationAuthorityResolution ResolveAnyAssignment(
        Guid employeeId,
        IReadOnlyList<BudgetOwnerCostCenterAuthorityProjectionRow> rows)
    {
        if (rows is null || rows.Any(row => row is null) || rows.Count == 0)
        {
            return rows is null
                ? throw new BudgetOwnerCreationAuthorityUnavailableException()
                : BudgetOwnerCreationAuthorityResolution.Denied;
        }

        var costCenterIds = new HashSet<Guid>();
        var hasActiveNonGroupAssignment = false;
        foreach (var row in rows)
        {
            if (row.EmployeeId != employeeId ||
                row.CostCenterId == Guid.Empty ||
                !costCenterIds.Add(row.CostCenterId) ||
                !row.EmployeeIsActive)
            {
                throw new BudgetOwnerCreationAuthorityUnavailableException();
            }

            hasActiveNonGroupAssignment |=
                row.AssignmentIsActive && row.CostCenterIsActive && !row.CostCenterIsGroup;
        }

        return hasActiveNonGroupAssignment
            ? new BudgetOwnerCreationAuthorityResolution(employeeId)
            : BudgetOwnerCreationAuthorityResolution.Denied;
    }
}

public sealed record BudgetOwnerCostCenterAuthorityProjectionRow(
    Guid EmployeeId,
    Guid CostCenterId,
    bool AssignmentIsActive,
    bool EmployeeIsActive,
    bool CostCenterIsActive,
    bool CostCenterIsGroup);

public sealed record BudgetOwnerCreationAuthorityResolution(Guid? EmployeeId)
{
    public static BudgetOwnerCreationAuthorityResolution Denied { get; } = new((Guid?)null);

    public bool CanCreateExpenseRequest => EmployeeId.HasValue;
}

public sealed class BudgetOwnerCreationAuthorityUnavailableException : Exception
{
    private const string SafeMessage =
        "The budget-owner creation authority could not be resolved consistently.";

    public BudgetOwnerCreationAuthorityUnavailableException()
        : base(SafeMessage)
    {
    }

    public BudgetOwnerCreationAuthorityUnavailableException(Exception innerException)
        : base(SafeMessage, innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}
