using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Workflow;

namespace Bms.Application.OverpaymentDeficits;

internal sealed record ManualDeficitCorrectionActor(Guid EmployeeId, string RoleCode);

/// <summary>
/// Resolves current command authority from the imported employee projection and
/// the organization's dated grant. Call inside every Serializable attempt and
/// before returning a stored result; neither creation rights nor a technical
/// administrator flag substitutes for a functional role.
/// </summary>
public sealed class ManualDeficitCorrectionAuthority(
    ICurrentActor currentActor,
    EmployeeActorResolver employeeActorResolver,
    EmployeeFunctionalRoleResolver roleResolver,
    IAccessScope accessScope)
{
    internal async Task<ManualDeficitReadAccess> ResolveReadAsync(
        string expectedActorExternalId, CancellationToken cancellationToken)
    {
        var actualActor = ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (!string.Equals(actualActor, expectedActorExternalId, StringComparison.Ordinal))
            throw new ActorForbiddenException();
        cancellationToken.ThrowIfCancellationRequested();
        // These ledger deficits have no CFO binding. Technical admin is not a bypass.
        if (!(await currentActor.ResolveBusinessDataReadScopeAsync(cancellationToken)).CanReadAll)
            throw new ActorForbiddenException();
        var employee = await employeeActorResolver.ResolveAsync(actualActor, cancellationToken);
        var canPropose = false;
        var canDecide = false;
        if (employee.HasActiveEmployee && employee.EmployeeId is { } employeeId)
        {
            var roles = await roleResolver.ResolveAsync(employeeId, cancellationToken);
            canPropose = roles.HasExactRole(ExpenseRequestWorkflowCodes.ResponsibleAccountantRoleCode);
            canDecide = roles.HasExactRole(ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode);
        }
        return new(actualActor, true, canPropose && !currentActor.IsBmsAdministrator,
            canDecide && !currentActor.IsBmsAdministrator);
    }

    internal async Task<ManualDeficitCorrectionActor> RequireAsync(
        string expectedActorExternalId,
        Guid organizationId,
        ManualDeficitCorrectionOperation operation,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var actualActor = ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (!string.Equals(actualActor, expectedActorExternalId, StringComparison.Ordinal))
        {
            throw new ActorForbiddenException();
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (organizationId == Guid.Empty || asOf == default)
        {
            throw new ManualDeficitCorrectionUnavailableException();
        }
        if (currentActor.IsBmsAdministrator)
        {
            // System-administrator profile may inspect BMS configuration but
            // business approvals remain available only in the employee's
            // selected business profile, even when FIN_DIRECTOR is assigned.
            throw new ActorForbiddenException();
        }
        var requiredRole = operation switch
        {
            ManualDeficitCorrectionOperation.Create =>
                ExpenseRequestWorkflowCodes.ResponsibleAccountantRoleCode,
            ManualDeficitCorrectionOperation.Approve or ManualDeficitCorrectionOperation.Reject =>
                ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode,
            _ => throw new ManualDeficitCorrectionUnavailableException()
        };

        var actor = await employeeActorResolver.ResolveAsync(actualActor, cancellationToken);
        if (actor.EmployeeId is not { } employeeId || !actor.HasActiveEmployee)
        {
            throw new ActorForbiddenException();
        }
        var roles = await roleResolver.ResolveAsync(employeeId, cancellationToken);
        if (!roles.HasExactRole(requiredRole))
        {
            throw new ActorForbiddenException();
        }
        if (!await accessScope.CanUseAsync(
                actualActor, organizationId, [], asOf, cancellationToken))
        {
            throw new ActorForbiddenException();
        }

        // TZ 16 specifically permits manual-correction confirmation only with
        // the functional director role. BMS-admin by itself grants nothing;
        // an integration administrator was already rejected by the outer gate.
        return new ManualDeficitCorrectionActor(employeeId, requiredRole);
    }
}
