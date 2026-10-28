using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Application.Integration;

public sealed class OutboxBacklogSummaryService(
    IOutboxBacklogSummaryReader reader,
    ICurrentActor currentActor,
    IClock clock)
{
    public async Task<OutboxBacklogSummaryDto> GetAsync(CancellationToken cancellationToken)
    {
        ActorIdentity.Require(currentActor);
        if (!currentActor.IsBmsAdministrator
            && !currentActor.IsBmsIntegrationAdministrator)
        {
            throw new ActorForbiddenException();
        }

        var record = await reader.ReadAsync(cancellationToken);
        if (!IsValid(record))
        {
            throw new InvalidOperationException(
                "Outbox backlog summary reader returned an invalid safe state.");
        }

        return new OutboxBacklogSummaryDto(
            clock.UtcNow,
            record.TotalCount,
            record.PendingCount,
            record.ProcessedCount,
            record.RetriedPendingCount,
            record.PendingWithLastErrorCount,
            record.MaxPendingRetryCount,
            record.OldestPendingOccurredAtUtc);
    }

    private static bool IsValid(OutboxBacklogSummaryRecord? record) =>
        record is not null
        && record.TotalCount >= 0
        && record.PendingCount >= 0
        && record.PendingCount <= record.TotalCount
        && record.ProcessedCount >= 0
        && record.ProcessedCount == record.TotalCount - record.PendingCount
        && record.RetriedPendingCount >= 0
        && record.RetriedPendingCount <= record.PendingCount
        && record.PendingWithLastErrorCount >= 0
        && record.PendingWithLastErrorCount <= record.PendingCount
        && record.MaxPendingRetryCount >= 0
        && ((record.PendingCount == 0
                && record.RetriedPendingCount == 0
                && record.PendingWithLastErrorCount == 0
                && record.MaxPendingRetryCount == 0
                && record.OldestPendingOccurredAtUtc is null)
            || (record.PendingCount > 0
                && record.OldestPendingOccurredAtUtc is not null
                && ((record.RetriedPendingCount == 0 && record.MaxPendingRetryCount == 0)
                    || (record.RetriedPendingCount > 0 && record.MaxPendingRetryCount > 0))));
}
