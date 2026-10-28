using Bms.Application.Abstractions;
using Bms.Application.ReferenceData;

namespace Bms.Infrastructure.Integration;

/// <summary>
/// Explicitly configured training source for Gate S2 tests. It has no default
/// fixtures, scheduling, database writer or DI registration and therefore
/// cannot activate itself in a production host.
/// </summary>
public sealed class TrainingNsiReferenceImportSource : INsiReferenceImportSource
{
    private readonly NsiReferenceImportBatch batch;

    public TrainingNsiReferenceImportSource(NsiReferenceImportBatch batch)
    {
        this.batch = batch ?? throw new ArgumentNullException(nameof(batch));
    }

    public Task<NsiReferenceImportBatch> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(batch);
    }
}
