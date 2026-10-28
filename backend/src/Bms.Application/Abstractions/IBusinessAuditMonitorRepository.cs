using Bms.Application.Audit;

namespace Bms.Application.Abstractions;

public interface IBusinessAuditMonitorRepository
{
    Task<IReadOnlyList<BusinessAuditMonitorRecord>> ListAsync(
        int skip,
        int take,
        CancellationToken cancellationToken);
}
