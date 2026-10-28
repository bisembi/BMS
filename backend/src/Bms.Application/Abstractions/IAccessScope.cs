using Bms.Application.Authorization;

namespace Bms.Application.Abstractions;

/// <summary>
/// Provider-neutral access check for reference values used by an expense request.
/// Implementations must fail closed: a missing or inactive grant is not access.
/// </summary>
public interface IAccessScope
{
    /// <summary>Draft-only authority after document-bound substitution has been proved.</summary>
    Task<bool> CanUseForAuthorizedExpenseDraftAsync(string actorExternalId, Guid? organizationId,
        IReadOnlyCollection<Guid> costCenterIds, DateOnly asOf,
        BudgetOwnerCreationAuthority principalAuthority, CancellationToken cancellationToken) =>
        CanUseForExpenseDraftAsync(actorExternalId, organizationId, costCenterIds, asOf, cancellationToken);
    Task<bool> CanUseAsync(
        string actorExternalId,
        Guid? organizationId,
        IReadOnlyCollection<Guid> costCenterIds,
        DateOnly asOf,
        CancellationToken cancellationToken);

    /// <summary>
    /// Draft-specific reference scope. Providers without a separate draft
    /// policy retain the existing fail-closed grant check. Other business
    /// operations must continue to call CanUseAsync.
    /// </summary>
    Task<bool> CanUseForExpenseDraftAsync(
        string actorExternalId,
        Guid? organizationId,
        IReadOnlyCollection<Guid> costCenterIds,
        DateOnly asOf,
        CancellationToken cancellationToken) =>
        CanUseAsync(actorExternalId, organizationId, costCenterIds, asOf, cancellationToken);
}
