namespace Bms.Application.Abstractions;

public sealed record StoredIdempotentResponse(
    string RequestHash,
    int ResponseStatusCode,
    string ResponseJson);

public interface IIdempotencyStore
{
    Task<StoredIdempotentResponse?> FindAsync(
        string scope,
        string key,
        CancellationToken cancellationToken);

    void Add(
        string scope,
        string key,
        string requestHash,
        int responseStatusCode,
        string responseJson,
        DateTimeOffset createdAtUtc);
}
