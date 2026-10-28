using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Bms.Application.Authorization;

namespace Bms.Application.Audit;

public sealed class BusinessAuditDetailService(
    IBusinessAuditDetailRepository repository,
    ICurrentActor currentActor,
    IBusinessTransactionRunner transactions)
{
    private const int MaximumSnapshotCharacters = 1_000_000;

    public Task<BusinessAuditDetailDto?> GetAsync(Guid auditId, CancellationToken cancellationToken) =>
        transactions.ExecuteAsync(token => ReadCoreAsync(auditId, token), cancellationToken);

    private async Task<BusinessAuditDetailDto?> ReadCoreAsync(Guid auditId, CancellationToken cancellationToken)
    {
        await RequireReadAuthorityAsync(cancellationToken);
        if (auditId == Guid.Empty)
        {
            throw new ArgumentException("Audit id must not be empty.", nameof(auditId));
        }

        var record = await repository.FindAsync(auditId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var scope = await currentActor.ResolveBusinessDataReadScopeAsync(cancellationToken);
        var readable = !string.Equals(record.Metadata.ObjectType, "ExpenseRequest", StringComparison.Ordinal)
            || (BusinessSnapshotReadScope.CanRead(scope, record.BeforeJson)
                && BusinessSnapshotReadScope.CanRead(scope, record.AfterJson));
        var details = readable ? ProjectDetails(record) : UnavailableDetails(record.Metadata.OccurredAtUtc);
        return new BusinessAuditDetailDto(
            ToItem(record.Metadata),
            details.Kind,
            details.Title,
            details.ActorLabel,
            details.DetailsStatus,
            details.Changes,
            details.OmittedChanges);
    }

    private async Task RequireReadAuthorityAsync(CancellationToken cancellationToken)
    {
        ActorIdentity.Require(currentActor);
        if (!await currentActor.CanViewBusinessAuditMonitorAsync(cancellationToken))
        {
            throw new ActorForbiddenException();
        }
    }

    private static BusinessAuditMonitorItemDto ToItem(BusinessAuditMonitorRecord record) => new(
        record.AuditId,
        record.OccurredAtUtc,
        record.ActorExternalId,
        record.ObjectType,
        record.ObjectId,
        record.Action,
        record.CorrelationId,
        record.HasBefore,
        record.HasAfter);

    private static ExpenseRequestHistoryEventDto ProjectDetails(BusinessAuditDetailRecord record)
    {
        if (!string.Equals(record.Metadata.ObjectType, "ExpenseRequest", StringComparison.Ordinal)
            || !Guid.TryParseExact(record.Metadata.ObjectId, "D", out var objectId)
            || objectId == Guid.Empty
            || string.IsNullOrWhiteSpace(record.AfterJson)
            || record.AfterJson.Length > MaximumSnapshotCharacters
            || record.BeforeJson?.Length > MaximumSnapshotCharacters)
        {
            return UnavailableDetails(record.Metadata.OccurredAtUtc);
        }

        return ExpenseRequestHistoryProjector.Project(new ExpenseRequestAuditSnapshotRow(
            record.Metadata.OccurredAtUtc,
            record.Metadata.ActorExternalId,
            record.Metadata.Action,
            record.BeforeJson,
            record.AfterJson), objectId);
    }

    private static ExpenseRequestHistoryEventDto UnavailableDetails(DateTimeOffset occurredAtUtc) =>
        new(
            occurredAtUtc,
            "unsupported",
            "Событие аудита",
            "Пользователь BMS",
            "unavailable",
            [],
            0);
}
