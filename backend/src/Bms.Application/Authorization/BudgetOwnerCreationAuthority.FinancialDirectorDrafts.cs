using Bms.Application.Common;
using Bms.Domain.Workflow;

namespace Bms.Application.Authorization;

public sealed partial class BudgetOwnerCreationAuthority
{
    /// <summary>
    /// Separate draft authority for finance directors/controllers. This does not make
    /// the employee a budget owner and must not replace workflow/approval checks.
    /// Authentication or a role claim alone is insufficient: read the current
    /// active employee mapping and exact functional-role assignment from SQL.
    /// </summary>
    public async Task<bool> HasFinancialDraftAuthorityAsync(
        string actorExternalId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!ActorIdentity.IsCanonical(actorExternalId))
                throw new BudgetOwnerCreationAuthorityUnavailableException();

            var actor = await employeeActorResolver.ResolveAsync(actorExternalId, cancellationToken);
            if (!actor.HasActiveEmployee) return false;
            if (actor.EmployeeId is not Guid employeeId)
                throw new BudgetOwnerCreationAuthorityUnavailableException();

            var roles = await employeeFunctionalRoleResolver.ResolveAsync(employeeId, cancellationToken);
            return roles.HasExactRole(ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode)
                || roles.HasExactRole(ExpenseRequestWorkflowCodes.FinancialControllerRoleCode)
                || roles.HasExactRole(ExpenseRequestWorkflowCodes.GeneralDirectorRoleCode);
        }
        catch (OperationCanceledException) { throw; }
        catch (BudgetOwnerCreationAuthorityUnavailableException) { throw; }
        catch (Exception exception)
        {
            throw new BudgetOwnerCreationAuthorityUnavailableException(exception);
        }
    }

    /// <summary>Same concrete-CFO prerequisite for draft creation and submission;
    /// this is not ownership or approval authority.</summary>
    public async Task<bool> CanCreateForCostCenterAsync(string actorExternalId, Guid costCenterId,
        CancellationToken cancellationToken) =>
        costCenterId != Guid.Empty && (await HasFinancialDraftAuthorityAsync(actorExternalId, cancellationToken)
            || (await ResolveAsync(actorExternalId, costCenterId, cancellationToken)).CanCreateExpenseRequest);
}
