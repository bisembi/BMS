using Bms.Application.Integration;

namespace Bms.Application.Abstractions;

public interface IReferenceProjectionInventoryReader
{
    Task<ReferenceProjectionInventoryRecord> ReadAsync(
        CancellationToken cancellationToken);
}
