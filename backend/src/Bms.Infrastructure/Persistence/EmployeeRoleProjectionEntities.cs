namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Structure-only projection of the BMS employee catalogue.
/// </summary>
public sealed class EmployeeReference
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Imported descriptive username from a source system.
    /// It is not an SSO NameIdentifier and is never used for authentication,
    /// authorisation or actor resolution.
    /// </summary>
    public string? SourceDatabaseUserName { get; set; }

    public bool IsActive { get; set; }
}

/// <summary>
/// Structure-only projection of the BMS functional-role catalogue.
/// </summary>
public sealed class FunctionalRoleReference
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

/// <summary>
/// Structure-only projection of the non-periodic information register
/// BMS_РолиСотрудников: two dimensions and the Активна resource, with no
/// validity period (ТЗ v2.9 META-DEF-03).
/// </summary>
public sealed class EmployeeFunctionalRole
{
    public Guid EmployeeId { get; set; }
    public Guid FunctionalRoleId { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Authoritative, case-preserving link from the canonical SSO actor identifier
/// to an imported employee. It is deliberately separate from the descriptive
/// descriptive <see cref="EmployeeReference.SourceDatabaseUserName"/> value.
/// </summary>
public sealed class EmployeeActorMapping
{
    public string ActorExternalId { get; set; } = string.Empty;
    public Guid EmployeeId { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Read-only source projection of the business assignment that lets an active
/// budget owner initiate expense requests for a particular non-group cost
/// centre. It is deliberately distinct from ActorCostCenterAccess, which only
/// governs data scope and never grants this business authority.
/// </summary>
public sealed class BudgetOwnerCostCenterAssignment
{
    public Guid EmployeeId { get; set; }
    public Guid CostCenterId { get; set; }
    public bool IsActive { get; set; }
}
