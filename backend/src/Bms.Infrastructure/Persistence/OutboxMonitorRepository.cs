using Bms.Application.Abstractions;
using Bms.Application.Integration;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class OutboxMonitorRepository(BmsDbContext dbContext) : IOutboxMonitorRepository
{
    public async Task<IReadOnlyList<OutboxMonitorRecord>> ListAsync(
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        await dbContext.OutboxMessages
            .AsNoTracking()
            .OrderByDescending(message => message.OccurredAtUtc)
            .ThenByDescending(message => message.Id)
            .Skip(skip)
            .Take(take)
            .Select(message => new OutboxMonitorRecord(
                message.Id,
                message.OccurredAtUtc,
                message.MessageType,
                message.ProcessedAtUtc,
                message.RetryCount,
                message.LastError != null))
            .ToArrayAsync(cancellationToken);
}
