using Bms.Application.Common;

namespace Bms.Application.Authorization;

public sealed class ActiveRoleSelection : IActiveRoleSelectionInitializer
{
    private IReadOnlyList<string> availableRoleCodes = Array.Empty<string>();
    private bool selected;

    public bool IsInitialized { get; private set; }

    public string? ActorExternalId { get; private set; }

    public Guid? EmployeeId { get; private set; }

    public string? ActiveRoleCode { get; private set; }
    public IReadOnlyList<string> OwnRoleCodes471 { get; private set; } = Array.Empty<string>();
    public StaffActingRole471? Substitution471 { get; private set; }

    public void SelectSubstitution471(StaffActingRole471 evidence)
    {
        if(!IsInitialized||selected||ActorExternalId!=evidence.DeputyActorExternalId||EmployeeId!=evidence.DeputyEmployeeId
            ||evidence.DocumentId==Guid.Empty||evidence.PrincipalEmployeeId==evidence.DeputyEmployeeId
            ||!ActiveRoleCatalog.IsBusinessRole(evidence.RoleCode))throw new ArgumentException("Invalid substitution selection.");
        Substitution471=evidence;EmployeeId=evidence.PrincipalEmployeeId;ActiveRoleCode=evidence.RoleCode;
        availableRoleCodes=Array.AsReadOnly(availableRoleCodes.Append(evidence.RoleCode).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        selected=true;
    }

    public IReadOnlyList<string> AvailableRoleCodes => availableRoleCodes;

    public bool HasAvailableRole(string roleCode)
    {
        ArgumentNullException.ThrowIfNull(roleCode);
        return IsInitialized
            && availableRoleCodes.Contains(roleCode, StringComparer.Ordinal);
    }

    public void InitializeAssignments(
        string actorExternalId,
        Guid? employeeId,
        IReadOnlyList<string> activeAssignmentCodes)
    {
        if (IsInitialized)
        {
            throw new InvalidOperationException(
                "The active-role request scope has already been initialized.");
        }
        if (!ActorIdentity.IsCanonical(actorExternalId)
            || employeeId == Guid.Empty)
        {
            throw new ArgumentException("A canonical actor mapping is required.");
        }
        ArgumentNullException.ThrowIfNull(activeAssignmentCodes);

        var publicCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assignmentCode in activeAssignmentCodes)
        {
            EmployeeFunctionalRoleResolver.ValidateRoleCode(assignmentCode);
            if (ActiveRoleCatalog.TryMapAssignmentCode(assignmentCode, out var publicCode))
            {
                publicCodes.Add(publicCode);
            }
        }

        ActorExternalId = actorExternalId;
        EmployeeId = employeeId;
        availableRoleCodes = Array.AsReadOnly(
            publicCodes.Contains(Bms.Application.Security.BmsAccessProfileCodes.IntegrationAdministrator)
                ? new[] { Bms.Application.Security.BmsAccessProfileCodes.IntegrationAdministrator }
                : publicCodes.Order(StringComparer.Ordinal).ToArray());
        OwnRoleCodes471 = availableRoleCodes;
        IsInitialized = true;
    }

    public void Select(string? publicRoleCode)
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException(
                "The active-role request scope has not been initialized.");
        }
        if (selected)
        {
            throw new InvalidOperationException(
                "The active role has already been selected for this request.");
        }
        if (publicRoleCode is not null
            && (!ActiveRoleCatalog.IsPublicRole(publicRoleCode)
                || !HasAvailableRole(publicRoleCode)))
        {
            throw new ArgumentOutOfRangeException(nameof(publicRoleCode));
        }

        ActiveRoleCode = publicRoleCode;
        selected = true;
    }
}
