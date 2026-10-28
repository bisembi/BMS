using Bms.Application.Authorization;
using Bms.Application.Budgeting;

namespace Bms.Application.Abstractions;

/// <summary>Report-only source; never used by admission, settlement or reservation writers.</summary>
public interface IExecutionReportReader
{
    Task<BudgetPositionSlice> ReadCurrentSliceAsync(Guid? costCenterId, Guid? cashFlowItemId,
        DateOnly? month, int skip, int take, BusinessDataReadScope scope,
        CancellationToken cancellationToken, Guid? expectedFactSnapshotId = null) => throw new NotSupportedException();
    Task<BudgetExecutionExportSlice> ReadCurrentExportAsync(Guid? costCenterId, Guid? cashFlowItemId,
        DateOnly? month, BusinessDataReadScope scope, CancellationToken cancellationToken,
        Guid? expectedFactSnapshotId = null) => throw new NotSupportedException();
    Task<BudgetPositionSlice> ReadSliceAsync(Guid? costCenterId, Guid? cashFlowItemId,
        DateOnly? month, int skip, int take, BusinessDataReadScope scope,
        CancellationToken cancellationToken, Guid? expectedFactSnapshotId = null);
    Task<BudgetExecutionExportSlice> ReadExportAsync(Guid? costCenterId, Guid? cashFlowItemId,
        DateOnly? month, BusinessDataReadScope scope, CancellationToken cancellationToken,
        Guid? expectedFactSnapshotId = null);
}
