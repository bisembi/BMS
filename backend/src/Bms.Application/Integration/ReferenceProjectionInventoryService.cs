using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Application.Integration;

public sealed class ReferenceProjectionInventoryService(
    IReferenceProjectionInventoryReader reader,
    ICurrentActor currentActor,
    IClock clock)
{
    public async Task<ReferenceProjectionInventoryDto> GetAsync(
        CancellationToken cancellationToken)
    {
        ActorIdentity.Require(currentActor);
        if (!currentActor.IsBmsAdministrator
            && !currentActor.IsBmsIntegrationAdministrator)
        {
            throw new ActorForbiddenException();
        }

        var record = await reader.ReadAsync(cancellationToken);
        if (!IsValid(record))
        {
            throw new InvalidOperationException(
                "Reference projection inventory reader returned an invalid safe state.");
        }

        return new ReferenceProjectionInventoryDto(
            clock.UtcNow,
            new ReferenceProjectionCountDto(
                record.EmployeeCount,
                record.ActiveEmployeeCount),
            new ReferenceProjectionCountDto(
                record.FunctionalRoleCount,
                record.ActiveFunctionalRoleCount),
            new ReferenceProjectionCountDto(
                record.EmployeeFunctionalRoleCount,
                record.ActiveEmployeeFunctionalRoleCount),
            record.ActiveAssignmentsWithInactiveEmployeeCount,
            record.ActiveAssignmentsWithInactiveFunctionalRoleCount);
    }

    private static bool IsValid(ReferenceProjectionInventoryRecord? record) =>
        record is not null
        && IsValidPair(record.EmployeeCount, record.ActiveEmployeeCount)
        && IsValidPair(record.FunctionalRoleCount, record.ActiveFunctionalRoleCount)
        && IsValidPair(
            record.EmployeeFunctionalRoleCount,
            record.ActiveEmployeeFunctionalRoleCount)
        && record.ActiveAssignmentsWithInactiveEmployeeCount >= 0
        && record.ActiveAssignmentsWithInactiveEmployeeCount
            <= record.ActiveEmployeeFunctionalRoleCount
        && record.ActiveAssignmentsWithInactiveFunctionalRoleCount >= 0
        && record.ActiveAssignmentsWithInactiveFunctionalRoleCount
            <= record.ActiveEmployeeFunctionalRoleCount;

    private static bool IsValidPair(long totalCount, long activeCount) =>
        totalCount >= 0
        && activeCount >= 0
        && activeCount <= totalCount;
}
