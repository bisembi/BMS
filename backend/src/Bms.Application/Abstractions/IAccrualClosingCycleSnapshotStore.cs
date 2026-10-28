using Bms.Application.ExpenseRequests;

namespace Bms.Application.Abstractions;

/// <summary>
/// Persistence seam for immutable closing-cycle evidence. The active closing
/// service owns the request lock and Serializable business transaction.
/// </summary>
internal interface IAccrualClosingCycleSnapshotStore
{
    Task<int> NextCycleNumberAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken);

    void Add(AccrualClosingCycleSnapshot snapshot);

    Task<AccrualClosingCycleSnapshot?> FindAsync(
        Guid expenseRequestId,
        Guid cycleId,
        CancellationToken cancellationToken);
}
