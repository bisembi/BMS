using Bms.Application.Abstractions;
using Bms.Application.Audit;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class BusinessAuditMonitorRepository(BmsDbContext dbContext) :
    IBusinessAuditMonitorRepository
{
    public async Task<IReadOnlyList<BusinessAuditMonitorRecord>> ListAsync(
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        await dbContext.BusinessAuditEntries
            .AsNoTracking()
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Skip(skip)
            .Take(take)
            .Select(entry => new BusinessAuditMonitorRecord(
                entry.Id,
                entry.OccurredAtUtc,
                entry.ActorExternalId,
                entry.ObjectType,
                entry.ObjectId,
                entry.Action,
                entry.CorrelationId,
                entry.BeforeJson != null,
                entry.AfterJson != null))
            .ToArrayAsync(cancellationToken);
}
