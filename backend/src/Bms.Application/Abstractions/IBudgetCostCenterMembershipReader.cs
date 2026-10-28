using Bms.Application.Authorization;

namespace Bms.Application.Abstractions;

/// <summary>
/// Reads only the explicit budget-document membership of one authoritative
/// employee in one CFO. This is not the expense-request owner-assignment table.
/// A write caller must hold the CFO's transaction lock while reading authority,
/// replaying or allocating a number, and committing. No membership is inferred
/// from the document's author, a global role, claims, or a descriptive CFO code.
/// </summary>
public interface IBudgetCostCenterMembershipReader
{
    Task<IReadOnlyList<BudgetCostCenterMembershipProjectionRow>> ReadAsync(
        Guid employeeId,
        Guid costCenterId,
        CancellationToken cancellationToken);
}
