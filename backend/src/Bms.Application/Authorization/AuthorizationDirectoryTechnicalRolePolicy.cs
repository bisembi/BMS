using Bms.Application.Common;
using Bms.Application.Security;
using Bms.Domain.Common;

namespace Bms.Application.Authorization;

public static class AuthorizationDirectoryTechnicalRolePolicy
{
    public static bool CanOfferRole(string code, bool isBmsAdministrator) =>
        !string.Equals(code, BmsAccessProfileCodes.SystemAdministrator, StringComparison.OrdinalIgnoreCase)
        && isBmsAdministrator;

    public static void RequireAssignmentPermission(string code, bool isBmsAdministrator)
    {
        // Preserve the existing Windows/AD-only literal-role boundary, even for admin.
        if (string.Equals(code, BmsAccessProfileCodes.SystemAdministrator, StringComparison.OrdinalIgnoreCase))
            throw new DomainRuleViolationException("SYSTEM_ADMIN is assigned only by the Windows/AD administrator boundary.");
        if (!isBmsAdministrator)
            throw new ActorForbiddenException();
    }

    public static void RequireMappingPermission(IEnumerable<string> activeAssignmentCodes, bool isBmsAdministrator)
    {
        if (!isBmsAdministrator && activeAssignmentCodes.Any())
            throw new ActorForbiddenException();
    }
}
