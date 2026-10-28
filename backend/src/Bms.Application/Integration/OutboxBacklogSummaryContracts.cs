namespace Bms.Application.Integration;

public sealed record OutboxBacklogSummaryRecord(
    long TotalCount,
    long PendingCount,
    long ProcessedCount,
    long RetriedPendingCount,
    long PendingWithLastErrorCount,
    int MaxPendingRetryCount,
    DateTimeOffset? OldestPendingOccurredAtUtc);

public sealed record OutboxBacklogSummaryDto(
    DateTimeOffset CheckedAtUtc,
    long TotalCount,
    long PendingCount,
    long ProcessedCount,
    long RetriedPendingCount,
    long PendingWithLastErrorCount,
    int MaxPendingRetryCount,
    DateTimeOffset? OldestPendingOccurredAtUtc);
