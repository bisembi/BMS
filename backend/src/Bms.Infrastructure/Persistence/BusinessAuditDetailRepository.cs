using Bms.Application.Abstractions;
using Bms.Application.Audit;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class BusinessAuditDetailRepository(BmsDbContext dbContext) :
    IBusinessAuditDetailRepository
{
    public async Task<BusinessAuditDetailRecord?> FindAsync(
        Guid auditId,
        CancellationToken cancellationToken) =>
        await dbContext.BusinessAuditEntries
            .AsNoTracking()
            .Where(entry => entry.Id == auditId)
            .Select(entry => new BusinessAuditDetailRecord(
                new BusinessAuditMonitorRecord(
                    entry.Id,
                    entry.OccurredAtUtc,
                    entry.ActorExternalId,
                    entry.ObjectType,
                    entry.ObjectId,
                    entry.Action,
                    entry.CorrelationId,
                    entry.BeforeJson != null,
                    entry.AfterJson != null),
                entry.BeforeJson,
                entry.AfterJson))
            .SingleOrDefaultAsync(cancellationToken);
}
