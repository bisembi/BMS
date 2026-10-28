using System.Text.Json.Serialization;

namespace Bms.Application.Security;

/// <param name="CsrfToken">
/// The antiforgery request token, present only while the protection is
/// switched on. It is omitted from the payload rather than serialized as
/// null, so a deployment that has not switched the protection on keeps the
/// session contract it had before this field existed.
/// </param>
public sealed record CurrentSessionDto(
    string ActorExternalId,
    string ActiveProfileCode,
    IReadOnlyList<string> AvailableProfileCodes,
    bool IsBmsAdministrator,
    bool IsBmsIntegrationAdministrator,
    bool CanAccessBusinessData,
    bool HasFullBusinessDataAccess,
    bool CanManageReferenceData,
    bool CanCreateExpenseRequest,
    bool CanViewBusinessAuditMonitor,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CsrfToken = null,
    /// <summary>
    /// Display-only employee name resolved from the active AD-to-employee
    /// mapping. The canonical actor identifier remains the authorization key.
    /// </summary>
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ActorDisplayName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? CanReadBudgetReports = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? CanReadAllBusinessData = null,
    string? ActiveRoleCode = null,
    IReadOnlyList<string>? AvailableRoleCodes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<Bms.Application.Authorization.StaffRoleChoice471>? StaffSubstitutionRoles471 = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? ActiveSubstitutionId471 = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? OwnRoleCodes471 = null);
