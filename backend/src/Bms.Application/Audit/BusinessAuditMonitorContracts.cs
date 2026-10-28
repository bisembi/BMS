namespace Bms.Application.Audit;

public sealed record BusinessAuditMonitorRecord(
    Guid AuditId,
    DateTimeOffset OccurredAtUtc,
    string ActorExternalId,
    string ObjectType,
    string ObjectId,
    string Action,
    string CorrelationId,
    bool HasBefore,
    bool HasAfter);

public sealed record BusinessAuditMonitorItemDto(
    Guid AuditId,
    DateTimeOffset OccurredAtUtc,
    string ActorExternalId,
    string ObjectType,
    string ObjectId,
    string Action,
    string CorrelationId,
    bool HasBefore,
    bool HasAfter);

public sealed record BusinessAuditMonitorPageDto(
    IReadOnlyList<BusinessAuditMonitorItemDto> Items,
    int Skip,
    int Take,
    bool HasMore);
