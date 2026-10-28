namespace Bms.Application.Authorization;

public sealed record EmployeeFunctionalRoleProjectionRow(
    Guid EmployeeId,
    bool EmployeeIsActive,
    Guid? FunctionalRoleId,
    bool? AssignmentIsActive,
    string? RoleCode,
    bool? RoleIsActive);

public sealed class EmployeeFunctionalRoleResolution
{
    private readonly IReadOnlyList<string> activeRoleCodes;

    internal EmployeeFunctionalRoleResolution(
        Guid employeeId,
        bool employeeExists,
        bool employeeIsActive,
        IReadOnlyList<string> activeRoleCodes)
    {
        EmployeeId = employeeId;
        EmployeeExists = employeeExists;
        EmployeeIsActive = employeeIsActive;
        this.activeRoleCodes = activeRoleCodes
            ?? throw new ArgumentNullException(nameof(activeRoleCodes));
    }

    public Guid EmployeeId { get; }

    public bool EmployeeExists { get; }

    public bool EmployeeIsActive { get; }

    public IReadOnlyList<string> ActiveRoleCodes => activeRoleCodes;

    public bool HasExactRole(string roleCode)
    {
        EmployeeFunctionalRoleResolver.ValidateRoleCode(roleCode);
        return EmployeeExists
            && EmployeeIsActive
            && activeRoleCodes.Contains(roleCode, StringComparer.Ordinal);
    }
}

public sealed class EmployeeFunctionalRoleResolutionUnavailableException : Exception
{
    private const string SafeMessage =
        "The employee functional-role assignment could not be resolved consistently.";

    internal EmployeeFunctionalRoleResolutionUnavailableException()
        : base(SafeMessage)
    {
    }

    internal EmployeeFunctionalRoleResolutionUnavailableException(Exception innerException)
        : base(
            SafeMessage,
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}
