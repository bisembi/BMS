using Bms.Domain.Budgeting;
using Bms.Application.Budgeting;

namespace Bms.Application.Abstractions;

public sealed record BudgetInstanceListProjection(
    Guid Id,
    string Number,
    Guid CostCenterId,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    string Status,
    int LineCount,
    decimal TotalLimitAmountKzt,
    Guid ConcurrencyStamp,
    string? CostCenterCode = null,
    string? CostCenterName = null,
    decimal TotalReceiptsAmountKzt = 0m,
    decimal TotalPaymentsAmountKzt = 0m,
    decimal NetCashFlowAmountKzt = 0m,
    bool IsEffective = false,
    string? ApprovalState = null,
    bool IsInCompanyVersion = false,
    string? AuthorName = null,
    IReadOnlyList<BudgetInstanceVersionMembershipDto>? IncludedVersions = null);

public sealed record BudgetInstanceCostCenterLabelProjection(Guid CostCenterId, string Code, string Name);

public interface IBudgetInstanceStore
{
    Task ValidateCompanyCompositionWarningsAsync(BudgetInstance changed, CancellationToken token) => Task.CompletedTask;
    Task<bool> IsInApprovalAsync(Guid id, CancellationToken token) => Task.FromResult(false);
    Task AddAsync(BudgetInstance instance, CancellationToken cancellationToken);
    Task RecordCreationAsync(Guid id, DateTimeOffset createdAtUtc, CancellationToken token) => Task.CompletedTask;

    // Captured before removal, within the same authorized Serializable transaction.
    // Persist the complete closed-review provenance in the deletion audit.
    Task<string?> ReadDraftDeletionHistoryAsync(Guid id, CancellationToken token) => Task.FromResult<string?>(null);

    // Draft-only removal under the same Serializable CFO anchor as approval.
    // Audit and idempotency are committed by the application in that transaction.
    Task RemoveDraftAsync(BudgetInstance instance, CancellationToken cancellationToken);

    Task<bool> NumberExistsAsync(string number, CancellationToken cancellationToken);

    Task<BudgetInstance?> FindAsync(Guid id, CancellationToken cancellationToken);

    async Task<string?> ReadNumberAsync(Guid id, Guid costCenterId, CancellationToken cancellationToken)
    {
        var instance = await FindAsync(id, cancellationToken);
        return instance?.CostCenterId == costCenterId ? instance.Number : null;
    }

    /// <summary>
    /// Allocates the next approval ordinal for one cost center. The caller must
    /// already hold that center's Serializable directory lock; the operation
    /// never commits independently.
    /// </summary>
    Task<long> AllocateApprovalOrdinalAsync(Guid costCenterId, CancellationToken cancellationToken);

    // No-tracking hint, outside the business transaction; the caller re-reads
    // the document after acquiring the CFO anchor.
    Task<Guid?> FindCostCenterIdAsync(Guid id, CancellationToken cancellationToken);

    // Called only after the service authorizes this CFO/document, while the
    // same Serializable business transaction and CFO anchor remain held.
    Task<BudgetInstanceCostCenterLabelProjection?> ReadCostCenterLabelAsync(
        Guid costCenterId, CancellationToken cancellationToken);

    Task<BudgetDirectoryOptionPageDto> ListMemberCostCentersAsync(
        string actorExternalId, Guid employeeId, string query, DateOnly validFrom,
        DateOnly asOfToday, int skip, int take, CancellationToken cancellationToken);

    Task<BudgetDirectoryOptionPageDto> ListActiveCostCentersAsync(
        string query, int skip, int take, CancellationToken cancellationToken);

    Task<BudgetDirectoryOptionPageDto> ListVisibleCostCentersAsync(
        string actorExternalId, Guid employeeId, string query, DateOnly validFrom,
        DateOnly asOfToday, int skip, int take, CancellationToken cancellationToken) =>
        ListMemberCostCentersAsync(actorExternalId, employeeId, query, validFrom, asOfToday, skip, take, cancellationToken);

    Task<BudgetDirectoryOptionPageDto> ListOwnerCostCentersAsync(
        Guid employeeId, int skip, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Member-accessible budget instances (Draft and Approved) with
    /// ActorCostCenterAccess (today and ValidFrom) applied before COUNT and
    /// OFFSET/FETCH. An explicit active owner-directory assignment is an
    /// alternative to the derived membership/access rows. When
    /// approverCanSeeAll is true, the store uses the active non-group CFO
    /// queue instead; it never grants edit authority.
    /// </summary>
    Task<(IReadOnlyList<BudgetInstanceListProjection> Items, int Total)> ListOwnedBudgetPageAsync(
        string actorExternalId,
        Guid ownerEmployeeId,
        Guid? costCenterId,
        DateOnly? periodFrom,
        DateOnly? periodTo,
        DateOnly asOfToday,
        int skip,
        int take,
        bool approverCanSeeAll,
        CancellationToken cancellationToken);

    // An additional narrowing predicate, applied inside the authorized query
    // before COUNT/OFFSET. Legacy stores must never silently ignore a version.
    Task<(IReadOnlyList<BudgetInstanceListProjection> Items, int Total)> ListOwnedBudgetPageForVersionAsync(
        string actorExternalId, Guid ownerEmployeeId, Guid? costCenterId,
        DateOnly? periodFrom, DateOnly? periodTo, DateOnly asOfToday,
        int skip, int take, bool approverCanSeeAll, Guid versionId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Version-filtered budget reads are unavailable in this store.");

    Task<bool> CashFlowItemExistsAsync(Guid cashFlowItemId, CancellationToken cancellationToken);
    Task<bool> CashFlowItemBelongsToCostCenterAsync(Guid cashFlowItemId, Guid costCenterId,
        CancellationToken cancellationToken);

    Task<bool> CostCenterIsActiveNonGroupAsync(Guid costCenterId, CancellationToken cancellationToken);
}
