using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Application.Audit;

public sealed class BusinessAuditMonitorService(
    IBusinessAuditMonitorRepository repository,
    ICurrentActor currentActor)
{
    public async Task<BusinessAuditMonitorPageDto> ListAsync(
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        ActorIdentity.Require(currentActor);
        if (!await currentActor.CanViewBusinessAuditMonitorAsync(cancellationToken))
        {
            throw new ActorForbiddenException();
        }

        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 100);
        var records = await repository.ListAsync(skip, take + 1, cancellationToken);
        var hasMore = records.Count > take;
        var items = records
            .Take(take)
            .Select(record => new BusinessAuditMonitorItemDto(
                record.AuditId,
                record.OccurredAtUtc,
                record.ActorExternalId,
                record.ObjectType,
                record.ObjectId,
                record.Action,
                record.CorrelationId,
                record.HasBefore,
                record.HasAfter))
            .ToArray();
        return new BusinessAuditMonitorPageDto(items, skip, take, hasMore);
    }
}
