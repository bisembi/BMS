using System.Text.Json.Serialization;

namespace Bms.Application.ReferenceData;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateOrganizationCommand(
    [property: JsonRequired] string Code,
    [property: JsonRequired] string Name);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateOrganizationCommand(
    [property: JsonRequired] string Code,
    [property: JsonRequired] string Name);

public sealed record OrganizationDirectoryRecord(
    Guid Id,
    string Code,
    string Name,
    bool IsActive);

public sealed class OrganizationDirectoryNotFoundException()
    : Exception("The organization was not found.");

public sealed class OrganizationCodeConflictException()
    : Exception("The organization code is already assigned.");

public sealed class OrganizationDirectoryUnavailableException : Exception
{
    public OrganizationDirectoryUnavailableException()
        : base("The organization directory could not be resolved consistently.")
    {
    }

    public OrganizationDirectoryUnavailableException(Exception innerException)
        : base("The organization directory could not be resolved consistently.", innerException)
    {
    }
}
