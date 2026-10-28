using Bms.Application.Abstractions;
using Bms.Application.ExpenseRequests;
using Bms.Domain.ExpenseRequests;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// EF Core adapter that prepares, but deliberately does not save, the first
/// immutable accrual snapshot. The workflow service owns the one final save.
/// </summary>
public sealed class EfCoreAccrualSnapshotMaterializer(BmsDbContext dbContext)
    : IAccrualSnapshotMaterializer
{
    public async Task<AccrualSnapshotPreparation> PrepareAsync(
        ExpenseRequest request,
        Guid expectedVersion,
        CancellationToken cancellationToken)
    {
        if (request.Status == ExpenseRequestStatus.Returned)
            return await ReturnedAccrualSnapshotPreparation.PrepareAsync(request, expectedVersion,
                new EfCoreAccrualSnapshotMaterializationWriteSink(dbContext), cancellationToken);
        var result = await AccrualSnapshotMaterializationWriter.ApplyToTrackedAggregateAsync(
            request,
            expectedVersion,
            new EfCoreAccrualSnapshotMaterializationWriteSink(dbContext),
            cancellationToken);
        return new AccrualSnapshotPreparation(
            result.Outcome is AccrualSnapshotMaterializationWriteOutcome.Materialized
                or AccrualSnapshotMaterializationWriteOutcome.AlreadyMaterialized,
            result.RequestVersion);
    }
}
