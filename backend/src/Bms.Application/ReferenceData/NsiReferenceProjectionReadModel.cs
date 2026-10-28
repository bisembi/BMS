using System.Collections.ObjectModel;

namespace Bms.Application.ReferenceData;

/// <summary>
/// Immutable transport-free current projection returned by a repository
/// boundary. It contains no watermark, source identity or write semantics.
/// </summary>
public sealed class NsiReferenceProjectionReadModel
{
    private NsiReferenceProjectionReadModel(
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

    public IReadOnlyList<NsiCounterpartyImportItem> Counterparties { get; }

    public IReadOnlyList<NsiContractImportItem> Contracts { get; }

    public IReadOnlyList<Guid> OrganizationIds { get; }

    public IReadOnlyList<Guid> CurrencyIds { get; }

    public static NsiReferenceProjectionReadModel Create(
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
        return new NsiReferenceProjectionReadModel(
            projection.Counterparties,
            projection.Contracts,
            SnapshotIdentifiers(organizationIds, nameof(organizationIds)),
            SnapshotIdentifiers(currencyIds, nameof(currencyIds)));
    }

    private static IReadOnlyList<Guid> SnapshotIdentifiers(
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

public sealed class NsiReferenceProjectionUnavailableException : Exception
{
    private const string SafeMessage = "A consistent current NSI projection is unavailable.";

    public NsiReferenceProjectionUnavailableException()
        : base(SafeMessage)
    {
    }

    public NsiReferenceProjectionUnavailableException(Exception innerException)
        : base(
            SafeMessage,
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}
