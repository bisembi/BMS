using Bms.Application.ReferenceData;

namespace Bms.Application.Abstractions;

public interface IReferenceDataRepository
{
    Task<IReadOnlyList<ReferenceDataItemDto>> ListOrganizationsAsync(
        string actorExternalId,
        bool bypassActorScope,
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReferenceDataItemDto>> ListCostCentersAsync(
        string actorExternalId,
        bool bypassActorScope,
        bool includeInactive,
        bool includeGroups,
        DateOnly asOf,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReferenceDataItemDto>> ListCashFlowItemsAsync(
        bool includeInactive,
        bool includeGroups,
        CancellationToken cancellationToken);

    /// Active cash-flow groups are exposed only to reference-data managers so
    /// that an editor can assign a leaf article to a valid parent without
    /// downloading the full (potentially very large) article catalogue.
    Task<IReadOnlyList<ReferenceDataItemDto>> ListCashFlowItemGroupsAsync(
        CancellationToken cancellationToken);

    /// One page of the cash-flow-item catalogue, filtered by an optional
    /// search term over code and name.
    ///
    /// The term arrives already trimmed and length-checked. It is a value to
    /// be bound, never text to be pasted into a command.
    Task<ReferenceDataItemWindow> QueryCashFlowItemsAsync(
        string? search,
        int skip,
        int take,
        bool includeInactive,
        bool includeGroups,
        CancellationToken cancellationToken);

    /// Resolves one bounded set of identifiers. Only ordinary active leaves
    /// belong to this product path; administrative rows stay behind the
    /// existing administrator-only catalogue options.
    Task<IReadOnlyList<ReferenceDataItemDto>> ResolveCashFlowItemsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReferenceDataItemDto>> ListExpenseItemsAsync(
        CancellationToken cancellationToken);

    Task<ReferenceDataItemDto> CreateExpenseItemAsync(
        string code,
        string name,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReferenceDataItemDto>> ListCounterpartiesAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReferenceDataItemDto>> ListContractsAsync(
        Guid counterpartyId,
        Guid organizationId,
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReferenceDataItemDto>> ListCurrenciesAsync(
        bool includeInactive,
        CancellationToken cancellationToken);
}
