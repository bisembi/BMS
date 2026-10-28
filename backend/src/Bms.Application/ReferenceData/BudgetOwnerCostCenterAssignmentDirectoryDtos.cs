namespace Bms.Application.ReferenceData;

/// <summary>
/// Deliberately identifier-free row of the budget-owner directory. Employee
/// and cost-centre database identifiers do not cross the public API boundary.
/// </summary>
public sealed record BudgetOwnerCostCenterAssignmentDirectoryItemDto(
    string EmployeeCode,
    string EmployeeName,
    string CostCenterCode,
    string CostCenterName);

public sealed record BudgetOwnerCostCenterAssignmentDirectoryDto(
    string Catalog,
    string Source,
    DateTimeOffset? RefreshedAtUtc,
    IReadOnlyList<BudgetOwnerCostCenterAssignmentDirectoryItemDto> Items);
