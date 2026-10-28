using System.Text.Json.Serialization;

namespace Bms.Application.ReferenceData;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateCashFlowAccrualSettingsCommand(
    [property: JsonRequired] Guid ExpectedVersion,
    [property: JsonRequired] bool FormsManagerialAccrual,
    [property: JsonRequired] Guid? ExpenseItemId,
    Guid? CostCenterId = null,
    bool CostCenterIdSpecified = false,
    string? ActivityType = null,
    string? GroupName = null,
    bool ClassificationSpecified = false,
    string? Name = null,
    bool NameSpecified = false,
    string? FlowDirection = null,
    bool FlowDirectionSpecified = false);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CashFlowAccrualSettingsDto(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] string Code,
    [property: JsonRequired] string Name,
    [property: JsonRequired] bool IsActive,
    [property: JsonRequired] bool IsGroup,
    [property: JsonRequired] bool FormsManagerialAccrual,
    [property: JsonRequired] ReferenceDataItemDto? ExpenseItem,
    [property: JsonRequired] Guid Version,
    [property: JsonRequired] bool CanEdit,
    ReferenceDataItemDto? CostCenter = null,
    string? ActivityType = null,
    string? GroupName = null,
    string? FlowDirection = null);

public sealed record CashFlowAccrualSettingsSnapshot(
    Guid Id, string Code, string Name, bool IsActive, bool IsGroup,
    bool FormsManagerialAccrual, Guid? ExpenseItemId,
    ReferenceDataItemDto? ExpenseItem, Guid Version,
    Guid? CostCenterId = null, ReferenceDataItemDto? CostCenter = null,
    string? ActivityType = null, Guid? ParentId = null, string? GroupName = null,
    string? FlowDirection = null);

public sealed record CashFlowAccrualSettingsCommandResult(
    CashFlowAccrualSettingsDto Settings, bool Changed, bool IsReplay);

public sealed class CashFlowAccrualSettingsNotFoundException : Exception;
public sealed class CashFlowAccrualSettingsVersionConflictException : Exception
{
    public CashFlowAccrualSettingsVersionConflictException() { }
    public CashFlowAccrualSettingsVersionConflictException(Exception innerException)
        : base("The accrual settings version changed.", innerException) { }
}
public sealed class CashFlowAccrualSettingsUnavailableException : Exception
{
    public CashFlowAccrualSettingsUnavailableException()
        : base("The cash-flow accrual settings are unavailable.") { }

    public CashFlowAccrualSettingsUnavailableException(Exception innerException)
        : base("The cash-flow accrual settings are unavailable.", innerException) { }
}
