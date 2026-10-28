using Bms.Application.Common;
using Bms.Domain.Workflow;

namespace Bms.Application.Authorization;

public sealed partial class BudgetOwnerCreationAuthority
{
    /// <summary>Fresh read scope, separate from all command capabilities.</summary>
    public async Task<BusinessDataReadScope> ResolveBusinessDataReadScopeAsync(
        string actorExternalId, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ActorIdentity.IsCanonical(actorExternalId))
                throw new BudgetOwnerCreationAuthorityUnavailableException();
            var mapping = await employeeActorResolver.ResolveAsync(actorExternalId, cancellationToken);
            if (!mapping.HasActiveEmployee || mapping.EmployeeId is not Guid employeeId)
                return BusinessDataReadScope.None;
            var roles = await employeeFunctionalRoleResolver.ResolveAsync(employeeId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (roles.EmployeeId != employeeId || !roles.EmployeeExists || !roles.EmployeeIsActive)
                throw new BudgetOwnerCreationAuthorityUnavailableException();
            if (BusinessDataReadPolicy.HasGlobalRole(roles)) return BusinessDataReadScope.All;
            if (roles.HasExactRole(ExpenseRequestWorkflowCodes.PayrollCalculatorRoleCode))
                return new BusinessDataReadScope(false, [], payrollRequestIds: payrollRequestScope372 is null ? []
                    : await payrollRequestScope372.ReadAsync(actorExternalId, cancellationToken));
            if (roles.HasExactRole(ExpenseRequestWorkflowCodes.NsiSpecialistRoleCode))
                return new BusinessDataReadScope(false, [], nsiWorkQueueScopeReader is null ? []
                    : await nsiWorkQueueScopeReader.ReadOrganizationIdsAsync(actorExternalId, cancellationToken));
            if (!roles.HasExactRole(ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode)
                && !roles.HasExactRole(ExpenseRequestWorkflowCodes.CfoInitiatorRoleCode))
                return BusinessDataReadScope.None;
            var rows = await assignmentReader.ReadCreatorsForEmployeeAsync(employeeId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (rows is null || rows.Any(row => row is null || row.EmployeeId != employeeId
                || row.CostCenterId == Guid.Empty || !row.EmployeeIsActive)
                || rows.Select(row => row.CostCenterId).Distinct().Count() != rows.Count)
                throw new BudgetOwnerCreationAuthorityUnavailableException();
            return new BusinessDataReadScope(false, rows.Where(row => row.AssignmentIsActive
                && row.CostCenterIsActive && !row.CostCenterIsGroup).Select(row => row.CostCenterId));
        }
        catch (OperationCanceledException) { throw; }
        catch (BudgetOwnerCreationAuthorityUnavailableException) { throw; }
        catch (Exception exception) { throw new BudgetOwnerCreationAuthorityUnavailableException(exception); }
    }
}
