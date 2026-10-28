using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Application.Integration;

public sealed class OutboxMonitorService(
    IOutboxMonitorRepository repository,
    ICurrentActor currentActor)
{
    public async Task<OutboxMonitorPageDto> ListAsync(
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        ActorIdentity.Require(currentActor);
        if (!currentActor.IsBmsAdministrator
            && !currentActor.IsBmsIntegrationAdministrator)
        {
            throw new ActorForbiddenException();
        }

        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 100);
        var records = await repository.ListAsync(skip, take + 1, cancellationToken);
        var hasMore = records.Count > take;
        var items = records
            .Take(take)
            .Select(record => new OutboxMonitorItemDto(
                record.MessageId,
                record.OccurredAtUtc,
                record.MessageType,
                record.ProcessedAtUtc,
                record.RetryCount,
                record.HasLastError))
            .ToArray();
        return new OutboxMonitorPageDto(items, skip, take, hasMore);
    }
}
