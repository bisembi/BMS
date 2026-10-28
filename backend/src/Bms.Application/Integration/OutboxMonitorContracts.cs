namespace Bms.Application.Integration;

public sealed record OutboxMonitorRecord(
    Guid MessageId,
    DateTimeOffset OccurredAtUtc,
    string MessageType,
    DateTimeOffset? ProcessedAtUtc,
    int RetryCount,
    bool HasLastError);

public sealed record OutboxMonitorItemDto(
    Guid MessageId,
    DateTimeOffset OccurredAtUtc,
    string MessageType,
    DateTimeOffset? ProcessedAtUtc,
    int RetryCount,
    bool HasLastError);

public sealed record OutboxMonitorPageDto(
    IReadOnlyList<OutboxMonitorItemDto> Items,
    int Skip,
    int Take,
    bool HasMore);
