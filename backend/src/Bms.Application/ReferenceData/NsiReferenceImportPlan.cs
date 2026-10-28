using System.Collections.ObjectModel;

namespace Bms.Application.ReferenceData;

internal static class NsiReferenceImportIssueCodes
{
    internal const string CounterpartyCodeConflict = "Counterparty.CodeConflict";
    internal const string ContractCodeConflict = "Contract.CodeConflict";
    internal const string ContractCounterpartyMissing = "Contract.CounterpartyMissing";
    internal const string ContractOrganizationMissing = "Contract.OrganizationMissing";
    internal const string ContractCurrencyMissing = "Contract.CurrencyMissing";

    internal static bool IsKnown(string value) =>
        value is CounterpartyCodeConflict or
            ContractCodeConflict or
            ContractCounterpartyMissing or
            ContractOrganizationMissing or
            ContractCurrencyMissing;
}

internal enum NsiReferenceImportOperation
{
    Insert = 1,
    Update = 2,
    Unchanged = 3
}

internal sealed class NsiCounterpartyImportAssignment
{
    internal NsiCounterpartyImportAssignment(
        NsiReferenceImportOperation operation,
        NsiCounterpartyImportItem item)
    {
        Operation = operation;
        Item = item ?? throw new ArgumentNullException(nameof(item));
    }

    internal NsiReferenceImportOperation Operation { get; }

    internal NsiCounterpartyImportItem Item { get; }
}

internal sealed class NsiContractImportAssignment
{
    internal NsiContractImportAssignment(
        NsiReferenceImportOperation operation,
        NsiContractImportItem item)
    {
        Operation = operation;
        Item = item ?? throw new ArgumentNullException(nameof(item));
    }

    internal NsiReferenceImportOperation Operation { get; }

    internal NsiContractImportItem Item { get; }
}

internal sealed class NsiReferenceProjectionSnapshot
{
    private NsiReferenceProjectionSnapshot(
        IReadOnlyList<NsiCounterpartyImportItem> counterparties,
        IReadOnlyList<NsiContractImportItem> contracts,
        IReadOnlyList<Guid> organizationIds,
        IReadOnlyList<Guid> currencyIds)
    {
        Counterparties = counterparties;
        Contracts = contracts;
        OrganizationIds = organizationIds;
        CurrencyIds = currencyIds;
    }

    internal IReadOnlyList<NsiCounterpartyImportItem> Counterparties { get; }

    internal IReadOnlyList<NsiContractImportItem> Contracts { get; }

    internal IReadOnlyList<Guid> OrganizationIds { get; }

    internal IReadOnlyList<Guid> CurrencyIds { get; }

    internal static NsiReferenceProjectionSnapshot Create(
        IEnumerable<NsiCounterpartyImportItem> counterparties,
        IEnumerable<NsiContractImportItem> contracts,
        IEnumerable<Guid> organizationIds,
        IEnumerable<Guid> currencyIds)
    {
        ArgumentNullException.ThrowIfNull(counterparties);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(organizationIds);
        ArgumentNullException.ThrowIfNull(currencyIds);

        var projection = NsiReferenceImportBatch.Create(counterparties, contracts);
        var organizationSnapshot = ValidateIdentifiers(organizationIds, nameof(organizationIds));
        var currencySnapshot = ValidateIdentifiers(currencyIds, nameof(currencyIds));
        return new NsiReferenceProjectionSnapshot(
            projection.Counterparties,
            projection.Contracts,
            organizationSnapshot,
            currencySnapshot);
    }

    internal static NsiReferenceProjectionSnapshot Create(
        NsiReferenceProjectionReadModel projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return Create(
            projection.Counterparties,
            projection.Contracts,
            projection.OrganizationIds,
            projection.CurrencyIds);
    }

    private static IReadOnlyList<Guid> ValidateIdentifiers(
        IEnumerable<Guid> values,
        string parameterName)
    {
        var snapshot = values.ToArray();
        if (snapshot.Any(value => value == Guid.Empty))
        {
            throw new ArgumentException("Projection identifiers must be non-empty.", parameterName);
        }
        if (snapshot.Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException("Projection identifiers must be unique.", parameterName);
        }

        return new ReadOnlyCollection<Guid>(snapshot);
    }
}

internal sealed class NsiReferenceImportPlan
{
    internal NsiReferenceImportPlan(
        IEnumerable<NsiCounterpartyImportAssignment> counterparties,
        IEnumerable<NsiContractImportAssignment> contracts)
    {
        ArgumentNullException.ThrowIfNull(counterparties);
        ArgumentNullException.ThrowIfNull(contracts);
        Counterparties = new ReadOnlyCollection<NsiCounterpartyImportAssignment>(
            counterparties.ToArray());
        Contracts = new ReadOnlyCollection<NsiContractImportAssignment>(contracts.ToArray());
    }

    internal IReadOnlyList<NsiCounterpartyImportAssignment> Counterparties { get; }

    internal IReadOnlyList<NsiContractImportAssignment> Contracts { get; }
}

internal sealed class NsiReferenceImportPlanResolution
{
    private NsiReferenceImportPlanResolution(NsiReferenceImportPlan? plan, string? issueCode)
    {
        Plan = plan;
        IssueCode = issueCode;
    }

    internal NsiReferenceImportPlan? Plan { get; }

    internal string? IssueCode { get; }

    internal bool IsReady => Plan is not null;

    internal static NsiReferenceImportPlanResolution Ready(NsiReferenceImportPlan plan) =>
        new(plan ?? throw new ArgumentNullException(nameof(plan)), null);

    internal static NsiReferenceImportPlanResolution Blocked(string issueCode)
    {
        if (string.IsNullOrWhiteSpace(issueCode) || !NsiReferenceImportIssueCodes.IsKnown(issueCode))
        {
            throw new ArgumentException("A known issue code is required.", nameof(issueCode));
        }

        return new NsiReferenceImportPlanResolution(null, issueCode);
    }
}

internal static class NsiReferenceImportPlanBuilder
{
    internal static NsiReferenceImportPlanResolution Resolve(
        NsiReferenceImportBatch batch,
        NsiReferenceProjectionSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(current);

        var currentCounterparties = current.Counterparties.ToDictionary(item => item.Id);
        var finalCounterparties = new Dictionary<Guid, NsiCounterpartyImportItem>(
            currentCounterparties);
        foreach (var item in batch.Counterparties)
        {
            finalCounterparties[item.Id] = item;
        }
        if (HasDuplicateCodes(finalCounterparties.Values.Select(item => item.Code)))
        {
            return NsiReferenceImportPlanResolution.Blocked(
                NsiReferenceImportIssueCodes.CounterpartyCodeConflict);
        }

        var currentContracts = current.Contracts.ToDictionary(item => item.Id);
        var finalContracts = new Dictionary<Guid, NsiContractImportItem>(currentContracts);
        foreach (var item in batch.Contracts)
        {
            finalContracts[item.Id] = item;
        }
        if (HasDuplicateCodes(finalContracts.Values.Select(item => item.Code)))
        {
            return NsiReferenceImportPlanResolution.Blocked(
                NsiReferenceImportIssueCodes.ContractCodeConflict);
        }

        var finalCounterpartyIds = finalCounterparties.Keys.ToHashSet();
        var organizationIds = current.OrganizationIds.ToHashSet();
        var currencyIds = current.CurrencyIds.ToHashSet();
        if (batch.Contracts.Any(item => !finalCounterpartyIds.Contains(item.CounterpartyId)))
        {
            return NsiReferenceImportPlanResolution.Blocked(
                NsiReferenceImportIssueCodes.ContractCounterpartyMissing);
        }
        if (batch.Contracts.Any(item => !organizationIds.Contains(item.OrganizationId)))
        {
            return NsiReferenceImportPlanResolution.Blocked(
                NsiReferenceImportIssueCodes.ContractOrganizationMissing);
        }
        if (batch.Contracts.Any(item => !currencyIds.Contains(item.CurrencyId)))
        {
            return NsiReferenceImportPlanResolution.Blocked(
                NsiReferenceImportIssueCodes.ContractCurrencyMissing);
        }

        var counterpartyAssignments = batch.Counterparties.Select(item =>
            new NsiCounterpartyImportAssignment(
                ResolveCounterpartyOperation(currentCounterparties, item),
                item));
        var contractAssignments = batch.Contracts.Select(item =>
            new NsiContractImportAssignment(
                ResolveContractOperation(currentContracts, item),
                item));
        return NsiReferenceImportPlanResolution.Ready(
            new NsiReferenceImportPlan(counterpartyAssignments, contractAssignments));
    }

    private static bool HasDuplicateCodes(IEnumerable<string> codes)
    {
        var uniqueCodes = new HashSet<string>(StringComparer.Ordinal);
        return codes.Any(code => !uniqueCodes.Add(code));
    }

    private static NsiReferenceImportOperation ResolveCounterpartyOperation(
        IReadOnlyDictionary<Guid, NsiCounterpartyImportItem> current,
        NsiCounterpartyImportItem candidate)
    {
        if (!current.TryGetValue(candidate.Id, out var existing))
        {
            return NsiReferenceImportOperation.Insert;
        }

        return CounterpartyEquals(existing, candidate)
            ? NsiReferenceImportOperation.Unchanged
            : NsiReferenceImportOperation.Update;
    }

    private static NsiReferenceImportOperation ResolveContractOperation(
        IReadOnlyDictionary<Guid, NsiContractImportItem> current,
        NsiContractImportItem candidate)
    {
        if (!current.TryGetValue(candidate.Id, out var existing))
        {
            return NsiReferenceImportOperation.Insert;
        }

        return ContractEquals(existing, candidate)
            ? NsiReferenceImportOperation.Unchanged
            : NsiReferenceImportOperation.Update;
    }

    private static bool CounterpartyEquals(
        NsiCounterpartyImportItem left,
        NsiCounterpartyImportItem right) =>
        left.Id == right.Id &&
        string.Equals(left.Code, right.Code, StringComparison.Ordinal) &&
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
        left.IsActive == right.IsActive;

    private static bool ContractEquals(NsiContractImportItem left, NsiContractImportItem right) =>
        left.Id == right.Id &&
        left.CounterpartyId == right.CounterpartyId &&
        left.OrganizationId == right.OrganizationId &&
        string.Equals(left.Code, right.Code, StringComparison.Ordinal) &&
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
        left.ContractDate == right.ContractDate &&
        left.CurrencyId == right.CurrencyId &&
        left.IsActive == right.IsActive;
}
