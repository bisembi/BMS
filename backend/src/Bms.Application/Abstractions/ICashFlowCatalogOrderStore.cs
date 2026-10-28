using Bms.Application.ReferenceData;

namespace Bms.Application.Abstractions;

public interface ICashFlowCatalogOrderStore
{
    Task<CashFlowCatalogOrderData> ReadAsync(
        bool forUpdate,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, int?>> ReadDisplayOrdersAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);

    Task StageDisplayOrdersAsync(
        IReadOnlyList<CashFlowCatalogDisplayOrderChange> changes,
        CancellationToken cancellationToken);
}
