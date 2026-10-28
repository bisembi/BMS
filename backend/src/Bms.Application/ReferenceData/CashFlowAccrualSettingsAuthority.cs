using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;

namespace Bms.Application.ReferenceData;

internal sealed record CashFlowAccrualSettingsActor(Guid EmployeeId, string RoleCode);

/// <summary>
/// Cash-flow articles are shared, not organization-owned. The approved
/// reference-data maintainers are FIN_CONTROLLER, FIN_DIRECTOR,
/// GENERAL_DIRECTOR and SYSTEM_ADMIN; an integration administrator remains
/// excluded by the actor capability boundary.
/// </summary>
public sealed class CashFlowAccrualSettingsAuthority(
    ICurrentActor currentActor, EmployeeActorResolver employees,
    EmployeeFunctionalRoleResolver roles)
{
    internal async Task<CashFlowAccrualSettingsActor?> ResolveAsync(
        string expectedActor, CancellationToken cancellationToken)
    {
        var actor = ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (!string.Equals(actor, expectedActor, StringComparison.Ordinal))
            throw new ActorForbiddenException();
        cancellationToken.ThrowIfCancellationRequested();
        var current = await new ReferenceDataMaintenanceAuthority(employees, roles)
            .ResolveAsync(currentActor, cancellationToken);
        return current is null ? null : new(current.EmployeeId, current.RoleCode);
    }
}
