using Bms.Application.ExpenseRequests;
using System.Text.Json.Serialization;

namespace Bms.Application.Audit;

public sealed record BusinessAuditDetailRecord(
    BusinessAuditMonitorRecord Metadata,
    [property: JsonIgnore] string? BeforeJson,
    [property: JsonIgnore] string? AfterJson);

public sealed record BusinessAuditDetailDto(
    BusinessAuditMonitorItemDto Metadata,
    string Kind,
    string Title,
    string ActorLabel,
    string DetailsStatus,
    IReadOnlyList<ExpenseRequestHistoryChangeDto> Changes,
    int OmittedChanges);
