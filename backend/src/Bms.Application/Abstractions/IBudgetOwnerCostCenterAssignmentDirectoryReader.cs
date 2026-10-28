using Bms.Application.ReferenceData;

namespace Bms.Application.Abstractions;

/// <summary>
/// Reads the active, public-safe budget-owner to cost-centre directory. The
/// reader never resolves an SSO actor to an employee and never grants a role.
/// </summary>
public interface IBudgetOwnerCostCenterAssignmentDirectoryReader
{
    Task<IReadOnlyList<BudgetOwnerCostCenterAssignmentDirectoryItemDto>> ListVisibleAsync(
        string actorExternalId,
        bool bypassActorScope,
        DateOnly asOf,
        CancellationToken cancellationToken);
}
