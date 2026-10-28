using Bms.Application.Audit;

namespace Bms.Application.Abstractions;

public interface IChangeJournalReader
{
    Task<ChangeJournalReadPage> ReadAsync(
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? category,
        int skip, int take, CancellationToken cancellationToken);
}
