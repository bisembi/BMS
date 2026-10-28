namespace Bms.Application.Authorization;

/// <summary>
/// Immutable-for-consumers request scope containing the server-resolved role
/// choices and exactly one selected effective role.
/// </summary>
public interface IActiveRoleSelection
{
    bool IsInitialized { get; }

    string? ActorExternalId { get; }

    Guid? EmployeeId { get; }

    string? ActiveRoleCode { get; }

    IReadOnlyList<string> AvailableRoleCodes { get; }
    IReadOnlyList<string> OwnRoleCodes471 => AvailableRoleCodes;

    bool HasAvailableRole(string roleCode);
    StaffActingRole471? Substitution471 => null;
}

/// <summary>Write side used only by the request authentication boundaries.</summary>
public interface IActiveRoleSelectionInitializer : IActiveRoleSelection
{
    void InitializeAssignments(
        string actorExternalId,
        Guid? employeeId,
        IReadOnlyList<string> activeAssignmentCodes);

    void Select(string? publicRoleCode);
    void SelectSubstitution471(StaffActingRole471 evidence) => throw new InvalidOperationException("Substitution selection is not supported.");
}
