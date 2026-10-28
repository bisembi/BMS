using Bms.Application.Authorization;

namespace Bms.Application.Abstractions;

public interface IEmployeeFunctionalRoleAssignmentReader
{
    Task<IReadOnlyList<EmployeeFunctionalRoleProjectionRow>> ReadAsync(
        Guid employeeId,
        CancellationToken cancellationToken);
}
