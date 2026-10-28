using System.Text.Json.Serialization;

namespace Bms.Application.ReferenceData;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MoveCashFlowCatalogOrderCommand(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] Guid SourceId,
    [property: JsonRequired] Guid TargetId,
    [property: JsonRequired] string Placement);

public sealed record CashFlowCatalogOrderItemDto(
    Guid Id,
    string Code,
    string Name,
    Guid? ParentId,
    string? ParentCode,
    string? ParentName,
    string ActivityType,
    bool FormsManagerialAccrual,
    string? AccrualExpenseItemName,
    Guid? CostCenterId,
    string? CostCenterName,
    string? FlowDirection,
    string GroupKey,
    int? DisplayOrder);

public sealed record CashFlowCatalogOrderSnapshotDto(
    string Version,
    bool CanEdit,
    IReadOnlyList<CashFlowCatalogOrderItemDto> Items);

public sealed record CashFlowCatalogOrderRow(
    Guid Id,
    string Code,
    string Name,
    Guid? ParentId,
    string? ParentCode,
    string? ParentName,
    string ActivityType,
    string? FlowDirection,
    bool FormsManagerialAccrual,
    Guid? AccrualExpenseItemId,
    string? AccrualExpenseItemName,
    Guid? CostCenterId,
    string? CostCenterName,
    int? DisplayOrder);

public sealed record CashFlowCatalogOrderData(
    bool SupportsDisplayOrder,
    IReadOnlyList<CashFlowCatalogOrderRow> Rows);

public sealed record CashFlowCatalogDisplayOrderChange(Guid Id, int DisplayOrder);

public sealed record CashFlowCatalogOrderCommandResult(
    CashFlowCatalogOrderSnapshotDto Snapshot,
    bool Changed,
    bool IsReplay);

public sealed class CashFlowCatalogOrderNotFoundException : Exception;
public sealed class CashFlowCatalogOrderVersionConflictException : Exception
{
    public CashFlowCatalogOrderVersionConflictException() { }
    public CashFlowCatalogOrderVersionConflictException(string message, Exception innerException)
        : base(message, innerException) { }
}
public sealed class CashFlowCatalogOrderUnavailableException : Exception
{
    public CashFlowCatalogOrderUnavailableException()
        : base("The cash-flow catalogue order is unavailable.") { }

    public CashFlowCatalogOrderUnavailableException(Exception innerException)
        : base("The cash-flow catalogue order is unavailable.", innerException) { }
}
