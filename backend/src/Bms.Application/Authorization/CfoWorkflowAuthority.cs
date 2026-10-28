using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Workflow;

namespace Bms.Application.Authorization;

public interface ICfoWorkflowAuthorityReader
{
    Task<bool> HasAssignmentAsync(Guid employeeId, Guid costCenterId, string roleCode, CancellationToken token);
    Task<IReadOnlyList<Guid>> ReadOwnersAsync(Guid costCenterId, Guid excludedEmployeeId, CancellationToken token);
}

public sealed class CfoWorkflowAuthority(EmployeeActorResolver actors, EmployeeFunctionalRoleResolver roles,
    ICfoWorkflowAuthorityReader reader)
{
    public async Task<Guid> RequireEmployeeAsync(ICurrentActor actor, CancellationToken token)
    {
        var external = ActorIdentity.RequireBusinessDataAccess(actor);
        if (actor.IsBmsAdministrator) throw new ActorForbiddenException();
        var resolved = await actors.ResolveAsync(external, token);
        if (!resolved.HasActiveEmployee || resolved.EmployeeId is not Guid employee) throw new ActorForbiddenException();
        return employee;
    }

    public async Task<bool> RequiresOwnerAsync(Guid employeeId, Guid costCenterId, CancellationToken token)
    {
        var assignment = await roles.ResolveAsync(employeeId, token);
        return assignment.HasExactRole(ExpenseRequestWorkflowCodes.CfoInitiatorRoleCode)
            && await reader.HasAssignmentAsync(employeeId, costCenterId, ExpenseRequestWorkflowCodes.CfoInitiatorRoleCode, token);
    }

    // Route classification of the immutable initiator is assignment inventory,
    // not a grant to the effective deputy. Caller must first prove submission authority.
    internal async Task<bool> RequiresOwnerForAuthorizedPrincipalAsync(Guid employeeId, Guid costCenterId, CancellationToken token)
    {
        var assignment = await roles.ResolveAssignedAsync(employeeId, token);
        return assignment.HasExactRole(ExpenseRequestWorkflowCodes.CfoInitiatorRoleCode)
            && await reader.HasAssignmentAsync(employeeId, costCenterId, ExpenseRequestWorkflowCodes.CfoInitiatorRoleCode, token);
    }

    public Task<IReadOnlyList<Guid>> ReadOwnersAsync(Guid costCenterId, Guid excludedEmployeeId, CancellationToken token) =>
        reader.ReadOwnersAsync(costCenterId, excludedEmployeeId, token);

    // Only call after the submitter/principal's creation authority is proved.
    // Assignment inventory identifies the actual CFO owner even if the author
    // is currently using their separate finance role.
    internal async Task<bool> IsOwnerForAuthorizedPrincipalAsync(Guid employeeId, Guid costCenterId, CancellationToken token)
    {
        var assigned = await roles.ResolveAssignedAsync(employeeId, token);
        return assigned.HasExactRole(ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode)
            && await reader.HasAssignmentAsync(employeeId, costCenterId, ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode, token);
    }

    public async Task RequireStageAsync(Guid employeeId, Guid costCenterId, string roleCode, CancellationToken token)
    {
        var assignment = await roles.ResolveAsync(employeeId, token);
        if (!assignment.HasExactRole(roleCode)) throw new ActorForbiddenException();
        if (roleCode == ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode
            && !await reader.HasAssignmentAsync(employeeId, costCenterId, roleCode, token))
            throw new ActorForbiddenException();
    }

    internal async Task<bool> IsEffectiveFinancialDirectorAsync(Guid employeeId, CancellationToken token,
        string roleCode = ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode) =>
        (await roles.ResolveAsync(employeeId, token)).HasExactRole(roleCode);
}
