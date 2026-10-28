using Bms.Application.ReferenceData;

namespace Bms.Application.Abstractions;

public interface IBudgetOwnerCostCenterAssignmentDirectoryWriter
{
    Task<BudgetOwnerCostCenterAssignmentDirectoryItemDto> CreateAsync(
        BudgetOwnerCostCenterAssignmentWriteCommand command,
        CancellationToken cancellationToken);

    Task<BudgetOwnerCostCenterAssignmentDirectoryItemDto> UpdateAsync(
        string employeeCode,
        string costCenterCode,
        BudgetOwnerCostCenterAssignmentWriteCommand command,
        CancellationToken cancellationToken);

    Task<BudgetOwnerCostCenterAssignmentDirectoryItemDto> DeleteAsync(
        string employeeCode,
        string costCenterCode,
        CancellationToken cancellationToken);
}
