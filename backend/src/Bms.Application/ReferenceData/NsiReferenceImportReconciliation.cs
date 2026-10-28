namespace Bms.Application.ReferenceData;

internal sealed class NsiReferenceImportReconciliation
{
    internal NsiReferenceImportReconciliation(
        NsiReferenceImportBatchEvidence evidence,
        int matchingCounterpartyCount,
        int matchingContractCount)
    {
        Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        if (matchingCounterpartyCount < 0 ||
            matchingCounterpartyCount > evidence.CounterpartyCount)
        {
            throw new ArgumentOutOfRangeException(nameof(matchingCounterpartyCount));
        }
        if (matchingContractCount < 0 ||
            matchingContractCount > evidence.ContractCount)
        {
            throw new ArgumentOutOfRangeException(nameof(matchingContractCount));
        }

        MatchingCounterpartyCount = matchingCounterpartyCount;
        MatchingContractCount = matchingContractCount;
    }

    internal NsiReferenceImportBatchEvidence Evidence { get; }

    internal int MatchingCounterpartyCount { get; }

    internal int MatchingContractCount { get; }

    internal bool AllImportedItemsMatch =>
        MatchingCounterpartyCount == Evidence.CounterpartyCount &&
        MatchingContractCount == Evidence.ContractCount;
}

internal static class NsiReferenceImportReconciler
{
    internal static NsiReferenceImportReconciliation Reconcile(
        NsiReferenceImportBatch batch,
        NsiReferenceProjectionReadModel projection,
        NsiReferenceImportBatchEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(evidence);

        var actualEvidence = NsiReferenceImportBatchEvidence.Capture(batch);
        if (!EvidenceEquals(actualEvidence, evidence))
        {
            throw new ArgumentException(
                "Evidence does not describe the supplied NSI import batch.",
                nameof(evidence));
        }

        var counterparties = projection.Counterparties.ToDictionary(item => item.Id);
        var contracts = projection.Contracts.ToDictionary(item => item.Id);
        return new NsiReferenceImportReconciliation(
            evidence,
            batch.Counterparties.Count(item =>
                counterparties.TryGetValue(item.Id, out var actual) &&
                CounterpartyEquals(item, actual)),
            batch.Contracts.Count(item =>
                contracts.TryGetValue(item.Id, out var actual) &&
                ContractEquals(item, actual)));
    }

    private static bool EvidenceEquals(
        NsiReferenceImportBatchEvidence left,
        NsiReferenceImportBatchEvidence right) =>
        string.Equals(left.CanonicalSha256, right.CanonicalSha256, StringComparison.Ordinal) &&
        left.CounterpartyCount == right.CounterpartyCount &&
        left.ActiveCounterpartyCount == right.ActiveCounterpartyCount &&
        left.ContractCount == right.ContractCount &&
        left.ActiveContractCount == right.ActiveContractCount;

    private static bool CounterpartyEquals(
        NsiCounterpartyImportItem left,
        NsiCounterpartyImportItem right) =>
        left.Id == right.Id &&
        string.Equals(left.Code, right.Code, StringComparison.Ordinal) &&
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
        left.IsActive == right.IsActive;

    private static bool ContractEquals(
        NsiContractImportItem left,
        NsiContractImportItem right) =>
        left.Id == right.Id &&
        left.CounterpartyId == right.CounterpartyId &&
        left.OrganizationId == right.OrganizationId &&
        string.Equals(left.Code, right.Code, StringComparison.Ordinal) &&
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
        left.ContractDate == right.ContractDate &&
        left.CurrencyId == right.CurrencyId &&
        left.IsActive == right.IsActive;
}
