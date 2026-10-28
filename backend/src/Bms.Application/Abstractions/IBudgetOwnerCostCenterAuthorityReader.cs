using Bms.Application.Authorization;

namespace Bms.Application.Abstractions;

/// <summary>
/// Reads one exact employee-to-cost-centre assignment for a business-command
/// authority decision. This is deliberately distinct from the public
/// directory reader and from read-access scope.
/// </summary>
public interface IBudgetOwnerCostCenterAuthorityReader
{
    /// <summary>Use only after authorization of substitution for the immutable document initiator.</summary>
    IBudgetOwnerCostCenterAuthorityReader ForAuthorizedPrincipal() => this;

    Task<IReadOnlyList<BudgetOwnerCostCenterAuthorityProjectionRow>> ReadCreatorsAsync(
        Guid employeeId, Guid costCenterId, CancellationToken cancellationToken) =>
        ReadAsync(employeeId, costCenterId, cancellationToken);

    Task<IReadOnlyList<BudgetOwnerCostCenterAuthorityProjectionRow>> ReadCreatorsForEmployeeAsync(
        Guid employeeId, CancellationToken cancellationToken) =>
        ReadForEmployeeAsync(employeeId, cancellationToken);

    Task<IReadOnlyList<BudgetOwnerCostCenterAuthorityProjectionRow>> ReadAsync(
        Guid employeeId,
        Guid costCenterId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BudgetOwnerCostCenterAuthorityProjectionRow>> ReadForEmployeeAsync(
        Guid employeeId,
        CancellationToken cancellationToken);
}
