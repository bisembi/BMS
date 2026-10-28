using Bms.Application.Integration;

namespace Bms.Application.Abstractions;

public interface IOutboxMonitorRepository
{
    Task<IReadOnlyList<OutboxMonitorRecord>> ListAsync(
        int skip,
        int take,
        CancellationToken cancellationToken);
}
