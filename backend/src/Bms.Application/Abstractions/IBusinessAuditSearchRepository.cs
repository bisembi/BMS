using Bms.Application.Audit;

namespace Bms.Application.Abstractions;

public interface IBusinessAuditSearchRepository
{
    Task<IReadOnlyList<BusinessAuditMonitorRecord>> SearchAsync(
        BusinessAuditSearchFilter filter,
        int skip,
        int take,
        CancellationToken cancellationToken);
}
