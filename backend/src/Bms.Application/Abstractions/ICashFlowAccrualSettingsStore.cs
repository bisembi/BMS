using Bms.Application.ReferenceData;
using Bms.Domain.ReferenceData;

namespace Bms.Application.Abstractions;

public interface ICashFlowAccrualSettingsStore
{
    Task StageFlowDirectionAsync(CashFlowAccrualSettingsSnapshot before, Guid newVersion,
        string? flowDirection, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This store cannot change the catalog direction.");
    Task<CashFlowAccrualSettingsSnapshot?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<ReferenceDataItemDto?> FindExpenseItemAsync(Guid id, CancellationToken cancellationToken);
    Task<ReferenceDataItemDto?> FindCostCenterAsync(Guid id, CancellationToken cancellationToken);
    Task StageChangeAsync(CashFlowAccrualSettingsSnapshot before, CashFlowAccrualSettings settings,
        Guid newVersion, CancellationToken cancellationToken);
    Task StageNameAsync(CashFlowAccrualSettingsSnapshot before, Guid newVersion,
        string name, CancellationToken cancellationToken);
    Task<ReferenceDataItemDto?> StageClassificationAsync(CashFlowAccrualSettingsSnapshot before,
        Guid newVersion, string activityType, string? groupName, CancellationToken cancellationToken);
}
