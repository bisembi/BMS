using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>Cancellation and payment release reservations, not the historical
/// submission result. The legacy reader name is retained for compatibility.
/// Reads immutable route headers only; never recreates a reservation.</summary>
internal static class CancelledRequestBudgetIndicatorReader
{
    internal static IQueryable<PersistedApprovalRouteSnapshot> LatestSnapshots(BmsDbContext db)
    {
        var eligible = db.ApprovalRouteSnapshots.AsNoTracking().Where(snapshot =>
            db.ExpenseRequests.Any(request => request.Id == snapshot.ExpenseRequestId
                && (request.Status == ExpenseRequestStatus.Cancelled
                    || ((request.Status == ExpenseRequestStatus.Paid || request.Status == ExpenseRequestStatus.Closed)
                        && request.FullyPaidAtUtc != null && snapshot.CapturedAtUtc <= request.FullyPaidAtUtc))));
        return eligible.Where(snapshot => !eligible.Any(newer => newer.ExpenseRequestId == snapshot.ExpenseRequestId
            && newer.SnapshotNumber > snapshot.SnapshotNumber));
    }

    internal static async Task<IReadOnlyDictionary<Guid, bool>> ReadAsync(BmsDbContext db,
        IReadOnlyCollection<Guid> expenseRequestIds, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expenseRequestIds);
        token.ThrowIfCancellationRequested();
        if (expenseRequestIds.Count > 128 || expenseRequestIds.Any(id => id == Guid.Empty)
            || expenseRequestIds.Distinct().Count() != expenseRequestIds.Count)
            throw new ArgumentException("Invalid bounded cancellation budget lookup.");
        if (expenseRequestIds.Count == 0) return new Dictionary<Guid, bool>();
        var rows = await LatestSnapshots(db).Where(s => expenseRequestIds.Contains(s.ExpenseRequestId))
            .Select(s => new { s.ExpenseRequestId, s.PositiveDeficitKzt }).ToArrayAsync(token);
        if (rows.Any(s => s.PositiveDeficitKzt < 0m))
            throw new InvalidOperationException("Invalid historical budget deficit.");
        return rows.ToDictionary(s => s.ExpenseRequestId, s => s.PositiveDeficitKzt > 0m);
    }
}
