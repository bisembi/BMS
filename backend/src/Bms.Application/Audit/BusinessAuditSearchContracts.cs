namespace Bms.Application.Audit;

public sealed record BusinessAuditSearchFilter(
    string? ActorExternalId,
    string? ObjectType,
    string? ObjectId,
    string? Action,
    string? CorrelationId,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc);
