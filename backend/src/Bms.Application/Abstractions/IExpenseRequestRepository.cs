using Bms.Application.ExpenseRequests;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.Abstractions;

public interface IExpenseRequestRepository
{
    void Add(ExpenseRequest expenseRequest);

    Task<ExpenseRequest?> FindOwnedForUpdateAsync(
        Guid id,
        string actorExternalId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Locks one request for a workflow command without applying the
    /// initiator read scope. The command service performs the authoritative
    /// role/route check after this lock is held.
    /// </summary>
    Task<ExpenseRequest?> FindForWorkflowUpdateAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<ExpenseRequest?> FindOwnedForCopyAsync(
        Guid id,
        string actorExternalId,
        CancellationToken cancellationToken);

    Task<ExpenseRequest?> FindVisibleAsync(
        Guid id,
        string actorExternalId,
        bool includeAll,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads the parent only for the attachment service's independent access
    /// decision. The method itself grants no visibility and must never be
    /// exposed directly by an HTTP handler.
    /// </summary>
    Task<ExpenseRequest?> FindForAttachmentAuthorizationAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        Task.FromException<ExpenseRequest?>(new NotSupportedException(
            "This repository does not implement attachment authorization reads."));

    Task<IReadOnlyList<ExpenseRequest>> ListVisibleAsync(
        string actorExternalId,
        bool includeAll,
        ExpenseRequestFilter filter,
        int skip,
        int take,
        CancellationToken cancellationToken);

    /// <summary>SQL submission clock for an already visible page only.
    /// A request never submitted uses its persisted creation clock.</summary>
    Task<IReadOnlyDictionary<Guid, DateTimeOffset>> ReadRequestDatesAsync(
        IReadOnlyCollection<Guid> visibleRequestIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, DateTimeOffset>>(new Dictionary<Guid, DateTimeOffset>());

    /// <summary>
    /// Returns the subset of an already visible page that the actor may open
    /// as a full card. The list scope is intentionally wider for treasurers:
    /// before approval they see the row and status, but not the card body.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListOpenableIdsAsync(
        string actorExternalId,
        bool includeAll,
        IReadOnlyCollection<Guid> requestIds,
        CancellationToken cancellationToken);

    Task<int> CountVisibleAsync(
        string actorExternalId,
        bool includeAll,
        ExpenseRequestFilter filter,
        CancellationToken cancellationToken);
}
