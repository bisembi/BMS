namespace Bms.Application.Integration;

public sealed record ReferenceProjectionInventoryRecord(
    long EmployeeCount,
    long ActiveEmployeeCount,
    long FunctionalRoleCount,
    long ActiveFunctionalRoleCount,
    long EmployeeFunctionalRoleCount,
    long ActiveEmployeeFunctionalRoleCount,
    long ActiveAssignmentsWithInactiveEmployeeCount,
    long ActiveAssignmentsWithInactiveFunctionalRoleCount);

public sealed record ReferenceProjectionCountDto(
    long TotalCount,
    long ActiveCount);

public sealed record ReferenceProjectionInventoryDto(
    DateTimeOffset CheckedAtUtc,
    ReferenceProjectionCountDto Employees,
    ReferenceProjectionCountDto FunctionalRoles,
    ReferenceProjectionCountDto EmployeeFunctionalRoles,
    long ActiveAssignmentsWithInactiveEmployeeCount,
    long ActiveAssignmentsWithInactiveFunctionalRoleCount);
