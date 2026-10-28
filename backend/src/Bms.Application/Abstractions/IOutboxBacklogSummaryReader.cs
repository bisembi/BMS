using Bms.Application.Integration;

namespace Bms.Application.Abstractions;

public interface IOutboxBacklogSummaryReader
{
    Task<OutboxBacklogSummaryRecord> ReadAsync(CancellationToken cancellationToken);
}
