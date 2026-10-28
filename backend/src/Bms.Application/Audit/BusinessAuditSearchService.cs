using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Application.Audit;

public sealed class BusinessAuditSearchService(
    IBusinessAuditSearchRepository repository,
    ICurrentActor currentActor)
{
    public async Task<BusinessAuditMonitorPageDto> SearchAsync(
        BusinessAuditSearchFilter filter,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        ActorIdentity.Require(currentActor);
        if (!await currentActor.CanViewBusinessAuditMonitorAsync(cancellationToken))
        {
            throw new ActorForbiddenException();
        }
        ArgumentNullException.ThrowIfNull(filter);

        var normalized = new BusinessAuditSearchFilter(
            CanonicalOptional(filter.ActorExternalId, 256, nameof(filter.ActorExternalId)),
            CanonicalOptional(filter.ObjectType, 100, nameof(filter.ObjectType)),
            CanonicalOptional(filter.ObjectId, 100, nameof(filter.ObjectId)),
            CanonicalOptional(filter.Action, 100, nameof(filter.Action)),
            CanonicalOptional(filter.CorrelationId, 64, nameof(filter.CorrelationId)),
            filter.FromUtc?.ToUniversalTime(),
            filter.ToUtc?.ToUniversalTime());
        if (normalized.FromUtc > normalized.ToUtc)
        {
            throw new ArgumentException("UTC range start must not be after range end.", nameof(filter));
        }

        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 100);
        var records = await repository.SearchAsync(normalized, skip, take + 1, cancellationToken);
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

    private static string? CanonicalOptional(string? value, int maximumLength, string parameterName)
    {
        if (value is null)
        {
            return null;
        }
        if (value.Length == 0
            || value.Length > maximumLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("Audit filter value is not canonical.", parameterName);
        }
        return value;
    }
}
