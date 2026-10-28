using Bms.Application.Abstractions;
using Bms.Application.Audit;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class BusinessAuditSearchRepository(BmsDbContext dbContext) :
    IBusinessAuditSearchRepository
{
    public async Task<IReadOnlyList<BusinessAuditMonitorRecord>> SearchAsync(
        BusinessAuditSearchFilter filter,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = dbContext.BusinessAuditEntries.AsNoTracking();
        if (filter.ActorExternalId is not null)
        {
            query = query.Where(entry => entry.ActorExternalId == filter.ActorExternalId);
        }
        if (filter.ObjectType is not null)
        {
            query = query.Where(entry => entry.ObjectType == filter.ObjectType);
        }
        if (filter.ObjectId is not null)
        {
            query = query.Where(entry => entry.ObjectId == filter.ObjectId);
        }
        if (filter.Action is not null)
        {
            query = query.Where(entry => entry.Action == filter.Action);
        }
        if (filter.CorrelationId is not null)
        {
            query = query.Where(entry => entry.CorrelationId == filter.CorrelationId);
        }
        if (filter.FromUtc.HasValue)
        {
            query = query.Where(entry => entry.OccurredAtUtc >= filter.FromUtc.Value);
        }
        if (filter.ToUtc.HasValue)
        {
            query = query.Where(entry => entry.OccurredAtUtc <= filter.ToUtc.Value);
        }

        return await query
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
}
