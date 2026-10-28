using Bms.Application.ReferenceData;

namespace Bms.Application.Abstractions;

/// <summary>
/// Replaceable source boundary for counterparty and contract master data.
/// Transport, scheduling, watermark and disappearance semantics belong to the
/// future Gate G1 adapter and are deliberately absent from this interface.
/// </summary>
public interface INsiReferenceImportSource
{
    Task<NsiReferenceImportBatch> ReadAsync(CancellationToken cancellationToken);
}
