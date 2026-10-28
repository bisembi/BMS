using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Application.ReferenceData;

public sealed class ReferenceDataService(
    IReferenceDataRepository repository,
    ICurrentActor currentActor,
    IClock clock)
{
    /// The published limits of the windowed catalogue route.
    public const int MaximumSearchTermLength = 100;
    public const int MaximumWindowSize = 100;
    public const int DefaultWindowSize = 50;
    public const int MaximumResolveBatchSize = 50;

    private static readonly ReferenceCodeItemDto[] PaymentBases =
    [
        new("Contract", "Договор"),
        new("InvoiceWithoutContract", "Счет без договора"),
        new("PublicOffer", "Публичная оферта"),
        new("OtherBasis", "Иное основание")
    ];

    private static readonly ReferenceCodeItemDto[] CounterpartyResidencies =
    [
        new("ResidentKz", "Резидент РК"),
        new("NonResident", "Нерезидент")
    ];

    public async Task<ReferenceDataCatalogDto> ListOrganizationsAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        EnsureAdministrativeCatalogOption(includeInactive);
        var items = await repository.ListOrganizationsAsync(
            actorExternalId,
            currentActor.HasFullBusinessDataAccess,
            includeInactive,
            cancellationToken);

        return new ReferenceDataCatalogDto(
            "organizations",
            "bms_web",
            null,
            items);
    }

    public async Task<ReferenceDataCatalogDto> ListCostCentersAsync(
        bool includeInactive,
        bool includeGroups,
        DateOnly? asOf,
        CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        EnsureAdministrativeCatalogOption(includeInactive || includeGroups);
        var effectiveDate = asOf ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var items = await repository.ListCostCentersAsync(
            actorExternalId,
            currentActor.HasFullBusinessDataAccess,
            includeInactive,
            includeGroups,
            effectiveDate,
            cancellationToken);

        return new ReferenceDataCatalogDto(
            "cost-centers",
            "bms_web",
            null,
            items);
    }

    public async Task<ReferenceDataCatalogDto> ListCashFlowItemsAsync(
        bool includeInactive,
        bool includeGroups,
        CancellationToken cancellationToken)
    {
        ActorIdentity.RequireBusinessDataAccess(currentActor);
        EnsureAdministrativeCatalogOption(includeInactive || includeGroups);
        var items = await repository.ListCashFlowItemsAsync(
            includeInactive,
            includeGroups,
            cancellationToken);

        return new ReferenceDataCatalogDto(
            "cash-flow-items",
            "bms_web",
            null,
            items);
    }

    public async Task<ReferenceDataCatalogDto> ListCashFlowItemGroupsAsync(
        CancellationToken cancellationToken)
    {
        ActorIdentity.RequireReferenceDataManagement(currentActor);
        var items = await repository.ListCashFlowItemGroupsAsync(cancellationToken);

        return new ReferenceDataCatalogDto(
            "cash-flow-item-groups",
            "bms_web",
            null,
            items);
    }

    /// One page of the cash-flow-item catalogue.
    ///
    /// The order of the three checks is deliberate and is asserted by the
    /// contract suite: business-data access first, then the administrative
    /// options, then the window. An ordinary actor asking for inactive rows
    /// with an impossible window is refused for the option, not for the
    /// window, because the option is the thing they are not allowed to ask.
    public async Task<ReferenceDataPageDto> QueryCashFlowItemsAsync(
        string? query,
        int skip,
        int take,
        bool includeInactive,
        bool includeGroups,
        CancellationToken cancellationToken)
    {
        ActorIdentity.RequireBusinessDataAccess(currentActor);
        EnsureAdministrativeCatalogOption(includeInactive || includeGroups);
        var search = NormalizeSearchTerm(query);
        EnsureWindow(skip, take);

        var window = await repository.QueryCashFlowItemsAsync(
            search,
            skip,
            take,
            includeInactive,
            includeGroups,
            cancellationToken);

        return new ReferenceDataPageDto(window.Items, window.Total, skip, take);
    }

    /// Resolves identifiers already present in a bounded client view. Access
    /// is judged before the input, so malformed values cannot reveal a
    /// different boundary to an actor who cannot read business data.
    public async Task<ReferenceDataCatalogDto> ResolveCashFlowItemsAsync(
        IReadOnlyList<string>? ids,
        CancellationToken cancellationToken)
    {
        ActorIdentity.RequireBusinessDataAccess(currentActor);
        var parsedIds = ParseResolveIds(ids);
        var items = await repository.ResolveCashFlowItemsAsync(parsedIds, cancellationToken);

        return new ReferenceDataCatalogDto(
            "cash-flow-items",
            "bms_web",
            null,
            items);
    }

    public async Task<ReferenceDataCatalogDto> ListExpenseItemsAsync(
        CancellationToken cancellationToken)
    {
        ActorIdentity.RequireBusinessDataAccess(currentActor);
        var items = await repository.ListExpenseItemsAsync(cancellationToken);

        return new ReferenceDataCatalogDto(
            "expense-items",
            "bms_web",
            null,
            items);
    }

    private static Guid[] ParseResolveIds(IReadOnlyList<string>? ids)
    {
        if (ids is null || ids.Count == 0)
        {
            throw new ArgumentException("At least one cash-flow item id is required.", nameof(ids));
        }

        if (ids.Count > MaximumResolveBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ids),
                $"At most {MaximumResolveBatchSize} cash-flow item ids are accepted.");
        }

        var parsed = new List<Guid>(ids.Count);
        var seen = new HashSet<Guid>();
        foreach (var value in ids)
        {
            if (!Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty)
            {
                throw new ArgumentException("A cash-flow item id is invalid.", nameof(ids));
            }

            if (seen.Add(id))
            {
                parsed.Add(id);
            }
        }

        return [.. parsed];
    }

    public async Task<ReferenceDataCatalogDto> ListCounterpartiesAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        ActorIdentity.RequireBusinessDataAccess(currentActor);
        EnsureAdministrativeCatalogOption(includeInactive);
        var items = await repository.ListCounterpartiesAsync(
            includeInactive,
            cancellationToken);

        return new ReferenceDataCatalogDto(
            "counterparties",
            "bms_web",
            null,
            items);
    }

    public async Task<ReferenceDataCatalogDto> ListContractsAsync(
        Guid counterpartyId,
        Guid organizationId,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        ActorIdentity.RequireBusinessDataAccess(currentActor);
        EnsureRequiredReference(counterpartyId, nameof(counterpartyId));
        EnsureRequiredReference(organizationId, nameof(organizationId));
        EnsureAdministrativeCatalogOption(includeInactive);
        var items = await repository.ListContractsAsync(
            counterpartyId,
            organizationId,
            includeInactive,
            cancellationToken);

        return new ReferenceDataCatalogDto(
            "contracts",
            "bms_web",
            null,
            items);
    }

    public async Task<ReferenceDataCatalogDto> ListCurrenciesAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        ActorIdentity.RequireBusinessDataAccess(currentActor);
        EnsureAdministrativeCatalogOption(includeInactive);
        var items = await repository.ListCurrenciesAsync(
            includeInactive,
            cancellationToken);

        return new ReferenceDataCatalogDto(
            "currencies",
            "bms_web",
            null,
            items);
    }

    public ReferenceCodeCatalogDto ListPaymentBases()
    {
        ActorIdentity.RequireBusinessDataAccess(currentActor);
        return new ReferenceCodeCatalogDto(
            "payment-bases",
            "fixed",
            null,
            PaymentBases);
    }

    public ReferenceCodeCatalogDto ListCounterpartyResidencies()
    {
        ActorIdentity.RequireBusinessDataAccess(currentActor);
        return new ReferenceCodeCatalogDto(
            "counterparty-residencies",
            "fixed",
            null,
            CounterpartyResidencies);
    }

    /// Trims the term, and treats a term that is only whitespace as no term
    /// at all. The length is judged after trimming, so padding cannot push a
    /// term over the limit.
    private static string? NormalizeSearchTerm(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var trimmed = query.Trim();
        if (trimmed.Length > MaximumSearchTermLength)
        {
            throw new ArgumentException(
                "The search term is longer than the published limit.",
                nameof(query));
        }

        return trimmed;
    }

    /// The window is refused rather than quietly adjusted. Silently clamping
    /// take to the maximum would let a caller believe it received the page it
    /// asked for.
    private static void EnsureWindow(int skip, int take)
    {
        if (skip < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(skip),
                "The window offset cannot be negative.");
        }

        if (take < 1 || take > MaximumWindowSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(take),
                "The window size is outside the published range.");
        }
    }

    private void EnsureAdministrativeCatalogOption(bool requested)
    {
        if (requested && !currentActor.CanManageReferenceData
            && currentActor.ActiveRoleCode != Bms.Domain.Workflow.ExpenseRequestWorkflowCodes.AuditorRoleCode)
        {
            throw new ActorForbiddenException();
        }
    }

    private static void EnsureRequiredReference(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty reference id is required.", parameterName);
        }
    }
}
