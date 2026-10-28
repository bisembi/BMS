namespace Bms.Application.Abstractions;

public sealed record ExpenseRequestAuditSnapshotRow(
    DateTimeOffset OccurredAtUtc,
    string ActorExternalId,
    string Action,
    string? BeforeJson,
    string AfterJson)
{
    // Display only; never an identity or authorization input. Resolved only
    // after the repository proved visibility of this request.
    public string? ActorName { get; init; }
    // Display names resolved by stable IDs after card visibility is proved.
    // Null preserves the technical-audit caller's deliberately redacted contract.
    public ExpenseRequestHistoryReferences? References { get; init; }
}

public sealed record ExpenseRequestHistoryReferences(
    IReadOnlyDictionary<Guid, string> Organizations,
    IReadOnlyDictionary<Guid, string> CostCenters,
    IReadOnlyDictionary<Guid, string> Counterparties,
    IReadOnlyDictionary<Guid, string> Contracts,
    IReadOnlyDictionary<Guid, string> CashFlowItems);

public sealed record VisibleExpenseRequestAuditPage(
    IReadOnlyList<ExpenseRequestAuditSnapshotRow> Items,
    int Total);

public interface IExpenseRequestHistoryRepository
{
    // Separate read projection; never fall back to an unfiltered page of recent
    // changes, which could hide an older instruction behind payment events.
    Task<VisibleExpenseRequestAuditPage?> ListCommentedWorkflowVisibleAsync(
        Guid expenseRequestId, string actorExternalId, bool includeAll,
        int skip, int take, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The filtered workflow-history reader is unavailable.");

    Task<VisibleExpenseRequestAuditPage?> ListVisibleAsync(
        Guid expenseRequestId,
        string actorExternalId,
        bool includeAll,
        int skip,
        int take,
        CancellationToken cancellationToken);
}
