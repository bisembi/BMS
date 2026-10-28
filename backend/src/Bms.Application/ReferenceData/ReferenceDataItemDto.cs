using System.Text.Json.Serialization;

namespace Bms.Application.ReferenceData;

public sealed record ReferenceDataItemDto(
    Guid Id,
    string Code,
    string Name,
    Guid? ParentId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Bin = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NumericCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ActivityType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? CostCenterId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ParentName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? GroupingName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? OwnerNames = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FlowDirection = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? DisplayOrder = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CurrencyCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? SourceNames = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ReferenceCodeItemDto>? RelatedCashFlowItems = null);

public sealed record ReferenceDataCatalogDto(
    string Catalog,
    string Source,
    DateTimeOffset? RefreshedAtUtc,
    IReadOnlyList<ReferenceDataItemDto> Items);

/// One item in a windowed catalogue response. A parent can be outside the
/// current page (and groups are excluded by default), so its public label
/// travels with the child rather than requiring the client to download the
/// whole hierarchy.
public sealed record ReferenceDataPageItemDto(
    Guid Id,
    string Code,
    string Name,
    Guid? ParentId,
    string? ParentCode,
    string? ParentName,
    string? ActivityType = null,
    bool FormsManagerialAccrual = false,
    string? AccrualExpenseItemName = null,
    Guid? CostCenterId = null,
    string? CostCenterName = null,
    string? FlowDirection = null,
    int? DisplayOrder = null);

/// One page of a catalogue, with the size of the whole filter it came from.
///
/// <c>Total</c> counts every row the same filter selects, not the rows in
/// <c>Items</c>: a caller needs it to know whether another page exists.
public sealed record ReferenceDataPageDto(
    IReadOnlyList<ReferenceDataPageItemDto> Items,
    int Total,
    int Skip,
    int Take);

/// What a repository returns for one window: the page and the size of the
/// filter. Skip and take are the caller's own, so the repository does not
/// repeat them back.
public sealed record ReferenceDataItemWindow(
    IReadOnlyList<ReferenceDataPageItemDto> Items,
    int Total);

public sealed record ReferenceCodeItemDto(
    string Code,
    string Name);

public sealed record ReferenceCodeCatalogDto(
    string Catalog,
    string Source,
    DateTimeOffset? RefreshedAtUtc,
    IReadOnlyList<ReferenceCodeItemDto> Items);
