using Bms.Application.Audit;

namespace Bms.Application.Abstractions;

public interface IBusinessAuditDetailRepository
{
    Task<BusinessAuditDetailRecord?> FindAsync(
        Guid auditId,
        CancellationToken cancellationToken);
}
