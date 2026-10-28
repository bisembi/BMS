using Bms.Domain.Workflow;

namespace Bms.Application.Abstractions;

/// <summary>Persists and reloads an immutable route snapshot inside a business transaction.</summary>
public interface IApprovalRouteSnapshotStore
{
    Task CaptureCompanyBudgetBaselineAsync(Guid expenseRequestId, int snapshotNumber, CancellationToken token) => Task.CompletedTask;
    Task<bool> CompanyBudgetBaselineChangedAsync(Guid expenseRequestId, int snapshotNumber, CancellationToken token)
        => Task.FromResult(false); // Existing explicit in-memory/legacy adapters have no company-version projection.
    Task<int> NextSnapshotNumberAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken);

    void Add(ApprovalRouteSnapshot snapshot);

    Task<IReadOnlyDictionary<Guid, ApprovalRouteSnapshot>> FindActiveAsync(
        IReadOnlyDictionary<Guid, int> activeSnapshotNumbers, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return activeSnapshotNumbers.Count == 0
            ? Task.FromResult<IReadOnlyDictionary<Guid, ApprovalRouteSnapshot>>(new Dictionary<Guid, ApprovalRouteSnapshot>())
            : Task.FromException<IReadOnlyDictionary<Guid, ApprovalRouteSnapshot>>(new NotSupportedException(
                "This store does not implement bounded active approval-route lookup."));
    }

    Task<ApprovalRouteSnapshot?> FindAsync(
        Guid expenseRequestId,
        int snapshotNumber,
        CancellationToken cancellationToken);

    Task<ApprovalRouteSnapshot?> FindLatestAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken) =>
        Task.FromException<ApprovalRouteSnapshot?>(new NotSupportedException(
            "This store does not implement latest approval-route lookup."));

    Task<IReadOnlyDictionary<Guid, ApprovalRouteSnapshot>> FindLatestMany421Async(
        IReadOnlyCollection<Guid> expenseRequestIds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return expenseRequestIds.Count == 0
            ? Task.FromResult<IReadOnlyDictionary<Guid, ApprovalRouteSnapshot>>(new Dictionary<Guid, ApprovalRouteSnapshot>())
            : Task.FromException<IReadOnlyDictionary<Guid, ApprovalRouteSnapshot>>(new NotSupportedException(
                "This store does not implement bounded latest approval-route lookup."));
    }
}
