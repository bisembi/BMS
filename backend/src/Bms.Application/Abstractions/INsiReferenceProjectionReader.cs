using Bms.Application.ReferenceData;

namespace Bms.Application.Abstractions;

/// <summary>
/// Reads the complete current shape needed by the internal NSI import plan.
/// Transaction ownership, source reads and writes are deliberately outside
/// this boundary.
/// </summary>
public interface INsiReferenceProjectionReader
{
    Task<NsiReferenceProjectionReadModel> ReadAsync(CancellationToken cancellationToken);
}
