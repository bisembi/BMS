using System.Text.Json.Serialization;

namespace Bms.Application.ReferenceData;

/// <summary>
/// Common editor payload for the shared directories.  Optional fields are
/// used only by the directories that need them (hierarchy, contract scope or
/// ISO numeric currency code); the service rejects a missing required field.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReferenceDataMaintenanceCommand(
    string Code = "",
    [property: JsonRequired] string Name = "",
    string? ParentName = null,
    string? ActivityType = null,
    Guid? ParentId = null,
    bool ParentIdSpecified = false,
    bool? IsGroup = null,
    Guid? CounterpartyId = null,
    Guid? OrganizationId = null,
    DateOnly? ContractDate = null,
    Guid? CurrencyId = null,
    string? NumericCode = null,
    string? DirectoryId = null,
    string? Bin = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FlowDirection = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool FlowDirectionSpecified = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool HistoricalForActuals = false);

public sealed record ReferenceDataMaintenanceItem(
    Guid Id,
    string DirectoryId,
    string Code,
    string Name,
    Guid? ParentId = null,
    bool IsActive = true,
    bool? IsGroup = null,
    Guid? CounterpartyId = null,
    Guid? OrganizationId = null,
    DateOnly? ContractDate = null,
    Guid? CurrencyId = null,
    string? NumericCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Bin = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ActivityType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FlowDirection = null);

public sealed class ReferenceDataMaintenanceNotFoundException()
    : Exception("The reference-data item was not found.");

public sealed class ReferenceDataMaintenanceConflictException()
    : Exception("The reference-data item conflicts with an existing item or dependent data.");

public sealed class ReferenceDataMaintenanceUnavailableException : Exception
{
    public ReferenceDataMaintenanceUnavailableException()
        : base("The reference-data directory could not be changed consistently.") { }

    public ReferenceDataMaintenanceUnavailableException(Exception innerException)
        : base("The reference-data directory could not be changed consistently.", innerException) { }
}
