namespace Bms.Application.Abstractions;

public interface IOutboxSink
{
    void Enqueue(
        Guid id,
        DateTimeOffset occurredAtUtc,
        string messageType,
        string payloadJson,
        string correlationId);
}
