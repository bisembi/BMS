using Bms.Application.Abstractions;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

internal static class AccrualSnapshotMaterializationPlanOrchestrator
{
    internal static async Task<AccrualSnapshotMaterializationPlan?> BuildAsync(
        ExpenseRequest request,
        IAccrualSnapshotSourceResolver resolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(resolver);

        var batch = AccrualSnapshotMaterializationBatch.Create(request);
        var resolution = await resolver.ResolveAsync(batch.SourceLines, cancellationToken);
        return batch.TryBuildPlan(resolution);
    }
}
