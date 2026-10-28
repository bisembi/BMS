using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Application.ReferenceData;

/// <summary>
/// Read-only directory of active budget-owner assignments. It intentionally
/// has no writer: ownership, maintenance authority and temporal rules remain
/// an explicit business decision.
/// </summary>
public sealed class BudgetOwnerCostCenterAssignmentDirectoryService(
    IBudgetOwnerCostCenterAssignmentDirectoryReader reader,
    ICurrentActor currentActor,
    IClock clock)
{
    public async Task<BudgetOwnerCostCenterAssignmentDirectoryDto> ListAsync(
        CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        var asOf = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var items = await reader.ListVisibleAsync(
            actorExternalId,
            currentActor.HasFullBusinessDataAccess,
            asOf,
            cancellationToken);

        return new BudgetOwnerCostCenterAssignmentDirectoryDto(
            "budget-owner-cost-center-assignments",
            "bms_web",
            null,
            items);
    }
}
