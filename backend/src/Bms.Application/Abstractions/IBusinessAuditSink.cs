namespace Bms.Application.Abstractions;

public interface IBusinessAuditSink
{
    void Append(
        Guid id,
        DateTimeOffset occurredAtUtc,
        string actorExternalId,
        string objectType,
        string objectId,
        string action,
        string? beforeJson,
        string afterJson,
        string correlationId);
}
