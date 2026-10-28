namespace Bms.Application.Budgeting;

public sealed record BudgetPositionDto(
    Guid CostCenterId,
    Guid CashFlowItemId,
    DateOnly BudgetMonth,
    decimal LimitAmountKzt,
    decimal ReservedAmountKzt,
    decimal PaidAmountKzt,
    decimal AvailableAmountKzt,
    bool IsLimitConfigured,
    bool IsOverLimit)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ExecutionPlanCostCenterDto>? PlanCostCenters { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public RequestBudgetControl343Dto? RequestControl343 { get; init; }
}

public sealed record RequestBudgetControl343Dto(decimal? AvailableBeforeKzt,decimal? AvailableAfterKzt,decimal? PositiveDeficitKzt,bool Frozen);

// Original approved keys, retained for provenance and read-only reservation drill-down.
public sealed record ExecutionPlanCostCenterDto(Guid CostCenterId, string? Code, string? Name, decimal ReservedAmountKzt);

/// <summary>
/// Totals over the whole filtered area, not over the page. Reserved, paid and
/// effective limits are complete: a key without a covering approved version
/// contributes an explicit zero. The public contract is numeric and additive;
/// an undefined limit is not a published business state.
/// </summary>
public sealed record BudgetPositionTotalsDto(
    int TotalPositions,
    int ConfiguredLimitCount,
    int UnconfiguredLimitCount,
    decimal LimitAmountKzt,
    decimal ReservedAmountKzt,
    decimal PaidAmountKzt,
    decimal AvailableAmountKzt);

public sealed record BudgetPositionPageDto(
    IReadOnlyList<BudgetPositionDto> Items,
    int Skip,
    int Take,
    int Total,
    Guid? ExpenseRequestId,
    BudgetPositionTotalsDto Totals)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ExpenseRequestVersion { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ExecutionFactSourceDto? FactSource { get; init; }
}
