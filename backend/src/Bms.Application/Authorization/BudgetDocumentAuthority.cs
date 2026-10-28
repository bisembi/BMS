using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Workflow;

namespace Bms.Application.Authorization;

/// <summary>
/// Budget-only authority; dated scope, draft state, idempotency and transactional
/// membership serialization are additional command/store responsibilities.
/// </summary>
public sealed class BudgetDocumentAuthority(
    EmployeeActorResolver employeeActorResolver,
    EmployeeFunctionalRoleResolver employeeFunctionalRoleResolver,
    IBudgetCostCenterMembershipReader membershipReader,
    IBudgetOwnerCostCenterAuthorityReader? ownerAssignmentReader = null)
{
    /// <summary>
    /// Returns the active authoritative employee permitted to approve budgets.
    /// Only the exact current FIN_DIRECTOR assignment grants this capability;
    /// administrator flags and CFO membership are not approval authority.
    /// The caller must separately enforce dated document scope and hold the
    /// required transaction/CostCenter lock through replay and commit.
    /// </summary>
    public async Task<Guid> RequireApproverAsync(
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var employee = await employeeActorResolver.ResolveAsync(externalId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (employee.EmployeeId is not Guid employeeId)
            {
                throw new ActorForbiddenException();
            }

            var roles = await employeeFunctionalRoleResolver.ResolveAsync(employeeId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (roles.EmployeeId != employeeId || !roles.EmployeeExists || !roles.EmployeeIsActive)
            {
                // The mapping already asserted one active employee. A contrary
                // role projection is unavailable data, not an ordinary no-role.
                throw new BudgetDocumentAuthorityUnavailableException();
            }

            if (!roles.HasExactRole(ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode))
            {
                throw new ActorForbiddenException();
            }

            return employeeId;
        }
        catch (OperationCanceledException) { throw; }
        catch (ActorForbiddenException) { throw; }
        catch (BudgetDocumentAuthorityUnavailableException) { throw; }
        catch (Exception exception) { throw new BudgetDocumentAuthorityUnavailableException(exception); }
    }

    public async Task<Guid> RequireEmployeeAsync(ICurrentActor actor, CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        try
        {
            var employee = await employeeActorResolver.ResolveAsync(externalId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return employee.EmployeeId ?? throw new ActorForbiddenException();
        }
        catch (OperationCanceledException) { throw; }
        catch (ActorForbiddenException) { throw; }
        catch (Exception exception) { throw new BudgetDocumentAuthorityUnavailableException(exception); }
    }

    /// <summary>
    /// A negative role decision is a false capability; unavailable projections,
    /// an unauthenticated actor and cancellation remain their original failures.
    /// This value is re-resolved inside the actual approval transaction.
    /// </summary>
    public async Task<bool> CanApproveAsync(ICurrentActor actor, CancellationToken cancellationToken)
    {
        try
        {
            _ = await RequireApproverAsync(actor, cancellationToken);
            return true;
        }
        catch (ActorForbiddenException)
        {
            return false;
        }
    }

    /// <summary>Financial editing is independent of FIN_DIRECTOR-only approval/deletion.</summary>
    public async Task<bool> CanChooseAnyCostCenterAsync(ICurrentActor actor, CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var employee = await employeeActorResolver.ResolveAsync(externalId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (employee.EmployeeId is not Guid employeeId) return false;
            var roles = await employeeFunctionalRoleResolver.ResolveAsync(employeeId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (roles.EmployeeId != employeeId || !roles.EmployeeExists || !roles.EmployeeIsActive)
                throw new BudgetDocumentAuthorityUnavailableException();
            return HasFinancialEditingRole(roles);
        }
        catch (OperationCanceledException) { throw; }
        catch (BudgetDocumentAuthorityUnavailableException) { throw; }
        catch (Exception exception) { throw new BudgetDocumentAuthorityUnavailableException(exception); }
    }

    private static bool HasFinancialEditingRole(EmployeeFunctionalRoleResolution roles) =>
        roles.HasExactRole(ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode)
        || roles.HasExactRole(ExpenseRequestWorkflowCodes.FinancialControllerRoleCode);

    public async Task<BudgetDocumentEditorResolution> ResolveEditorAsync(
        ICurrentActor actor,
        Guid costCenterId,
        CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (costCenterId == Guid.Empty)
            {
                throw new BudgetDocumentAuthorityUnavailableException();
            }

            var employee = await employeeActorResolver.ResolveAsync(externalId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (employee.EmployeeId is not Guid employeeId)
            {
                return BudgetDocumentEditorResolution.Denied;
            }

            var roles = await employeeFunctionalRoleResolver.ResolveAsync(employeeId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (roles.EmployeeId != employeeId || !roles.EmployeeExists || !roles.EmployeeIsActive)
            {
                // Actor mapping already proved one active employee. A contrary
                // role projection is unavailable data, never permission to
                // fall back to a narrower or stale membership decision.
                throw new BudgetDocumentAuthorityUnavailableException();
            }

            if (HasFinancialEditingRole(roles))
            {
                return new BudgetDocumentEditorResolution(
                    employeeId,
                    HasFullBudgetAccess: true);
            }

            if (roles.HasExactRole(ExpenseRequestWorkflowCodes.CfoInitiatorRoleCode) && ownerAssignmentReader is not null)
            {
                var creators = await ownerAssignmentReader.ReadCreatorsAsync(employeeId, costCenterId, cancellationToken);
                if (creators.Any(row => row.EmployeeId == employeeId && row.CostCenterId == costCenterId
                    && row.AssignmentIsActive && row.EmployeeIsActive && row.CostCenterIsActive && !row.CostCenterIsGroup))
                    return new BudgetDocumentEditorResolution(employeeId, HasFullBudgetAccess: true);
                if (employeeFunctionalRoleResolver.UsesActiveRoleSelection)
                    return BudgetDocumentEditorResolution.Denied;
            }

            // Legacy direct construction retains its membership contract.
            // A production request never gains edit rights from membership
            // while AUDITOR/another role (or no role) is selected.
            if (employeeFunctionalRoleResolver.UsesActiveRoleSelection
                && !roles.HasExactRole(ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode)
                && !roles.HasExactRole(ExpenseRequestWorkflowCodes.CfoInitiatorRoleCode))
                return BudgetDocumentEditorResolution.Denied;

            var rows = await membershipReader.ReadAsync(employeeId, costCenterId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (rows is null || rows.Count > 1 || rows.Any(row => row is null))
            {
                throw new BudgetDocumentAuthorityUnavailableException();
            }

            if (rows.Count == 0)
            {
                // The "Владельцы бюджета и ЦФО" directory is the canonical
                // ownership assignment used by the business flow. Older
                // installations may not yet have the derived membership and
                // dated actor-scope rows, so an active exact owner assignment
                // is a deliberate fallback. It is checked only after the
                // explicit membership reader returns no row; a contradictory
                // pair is never silently merged into a broader permission.
                if (ownerAssignmentReader is null)
                {
                    return BudgetDocumentEditorResolution.Denied;
                }

                var ownerRows = await ownerAssignmentReader.ReadAsync(
                    employeeId,
                    costCenterId,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (ownerRows is null || ownerRows.Count > 1 || ownerRows.Any(row => row is null))
                {
                    throw new BudgetDocumentAuthorityUnavailableException();
                }

                if (ownerRows.Count == 0)
                {
                    return BudgetDocumentEditorResolution.Denied;
                }

                var owner = ownerRows[0];
                if (owner.EmployeeId != employeeId
                    || owner.CostCenterId != costCenterId)
                {
                    throw new BudgetDocumentAuthorityUnavailableException();
                }

                return owner.AssignmentIsActive
                    && owner.EmployeeIsActive
                    && owner.CostCenterIsActive
                    && !owner.CostCenterIsGroup
                    ? new BudgetDocumentEditorResolution(employeeId, HasFullBudgetAccess: true)
                    : BudgetDocumentEditorResolution.Denied;
            }

            var membership = rows[0];
            if (membership.EmployeeId != employeeId
                || membership.CostCenterId != costCenterId
                || !membership.EmployeeIsActive
                || membership.MembershipKind is not (BudgetCostCenterMembershipKinds.Owner or BudgetCostCenterMembershipKinds.Participant))
            {
                throw new BudgetDocumentAuthorityUnavailableException();
            }

            // Creator identity is audit provenance, not shared-budget authority.
            // Other global roles and expense-request creation never replace
            // explicit CFO membership. The two financial editor roles were
            // resolved above from the current authoritative role projection.
            return membership.MembershipIsActive && membership.CostCenterIsActive && !membership.CostCenterIsGroup
                ? new BudgetDocumentEditorResolution(employeeId, HasFullBudgetAccess: false)
                : BudgetDocumentEditorResolution.Denied;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BudgetDocumentAuthorityUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new BudgetDocumentAuthorityUnavailableException(exception);
        }
    }

    /// <summary>Read-only directory inspection is not maintenance authority.</summary>
    public async Task<bool> CanReadDirectoryAsync(ICurrentActor actor, CancellationToken token)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        if (actor.ActiveRoleCode == ExpenseRequestWorkflowCodes.AuditorRoleCode)
        {
            var employee = await employeeActorResolver.ResolveAsync(externalId, token);
            if (employee.EmployeeId is not Guid id) return false;
            var roles = await employeeFunctionalRoleResolver.ResolveAsync(id, token);
            return roles.EmployeeExists && roles.EmployeeIsActive && roles.EmployeeId == id
                && roles.HasExactRole(ExpenseRequestWorkflowCodes.AuditorRoleCode);
        }
        return await CanMaintainDirectoryAsync(actor, token);
    }

    public async Task<bool> CanMaintainDirectoryAsync(
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        _ = ActorIdentity.RequireBusinessDataAccess(actor);
        cancellationToken.ThrowIfCancellationRequested();
        if (actor.IsBmsAdministrator && !actor.UsesActiveRoleSelection)
        {
            // Explicit non-scoped legacy adapters retain their directory-only
            // capability. Production scoped actors always re-read SQL below.
            return true;
        }

        try
        {
            return await new ReferenceDataMaintenanceAuthority(employeeActorResolver, employeeFunctionalRoleResolver)
                .ResolveAsync(actor, cancellationToken) is not null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BudgetDocumentAuthorityUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new BudgetDocumentAuthorityUnavailableException(exception);
        }
    }

    /// <summary>Identity links and role grants require the freshly selected administrator assignment.</summary>
    public async Task<bool> CanManageRoleAssignmentsAsync(ICurrentActor actor, CancellationToken token)
    {
        _ = ActorIdentity.RequireReferenceDataManagement(actor);
        if (!actor.UsesActiveRoleSelection)
            return actor.IsBmsAdministrator; // Explicit local/legacy adapter only.
        var resolved = await new ReferenceDataMaintenanceAuthority(employeeActorResolver, employeeFunctionalRoleResolver)
            .ResolveAsync(actor, token);
        return resolved?.RoleCode == Bms.Application.Security.BmsAccessProfileCodes.SystemAdministrator;
    }
}

public sealed record BudgetCostCenterMembershipProjectionRow(
    Guid EmployeeId,
    Guid CostCenterId,
    string MembershipKind,
    bool MembershipIsActive,
    bool EmployeeIsActive,
    bool CostCenterIsActive,
    bool CostCenterIsGroup);

public static class BudgetCostCenterMembershipKinds
{
    public const string Owner = "OWNER";
    public const string Participant = "PARTICIPANT";
}

public sealed record BudgetDocumentEditorResolution(
    Guid? EmployeeId,
    bool HasFullBudgetAccess)
{
    public static BudgetDocumentEditorResolution Denied { get; } = new(
        EmployeeId: null,
        HasFullBudgetAccess: false);

    public bool CanEditBudgetDocument => EmployeeId.HasValue;
}

public sealed class BudgetDocumentAuthorityUnavailableException : Exception
{
    private const string SafeMessage = "The budget-document authority could not be resolved consistently.";

    public BudgetDocumentAuthorityUnavailableException() : base(SafeMessage)
    {
    }

    public BudgetDocumentAuthorityUnavailableException(Exception innerException)
        : base(SafeMessage, innerException)
    {
    }
}
