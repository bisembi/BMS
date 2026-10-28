using Bms.Application.Security;
using Bms.Domain.Workflow;

namespace Bms.Application.Authorization;

/// <summary>
/// Public request-role codes and the existing SQL assignment codes that can
/// back them.  A request code is only a selector; it never creates an
/// assignment.
/// </summary>
public static class ActiveRoleCatalog
{
    public const string SystemAdministratorAssignmentCode = "BMS_Администратор";
    public const string SystemAdministratorAssignmentAlias = "BMS.Administrator";
    public const string IntegrationAdministratorAssignmentCode =
        "BMS_АдминистраторИнтеграции";
    public const string IntegrationAdministratorAssignmentAlias =
        "BMS.IntegrationAdministrator";

    private static readonly IReadOnlyList<string> businessRoleCodes =
        Array.AsReadOnly(new[]
        {
            ExpenseRequestWorkflowCodes.AccountantRoleCode,
            ExpenseRequestWorkflowCodes.AuditorRoleCode,
            ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode,
            BusinessDataReadPolicy.ChiefAccountantRoleCode,
            ExpenseRequestWorkflowCodes.CfoInitiatorRoleCode,
            ExpenseRequestWorkflowCodes.FinancialControllerRoleCode,
            ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode,
            ExpenseRequestWorkflowCodes.GeneralDirectorRoleCode,
            ExpenseRequestWorkflowCodes.NsiSpecialistRoleCode,
            ExpenseRequestWorkflowCodes.PayrollCalculatorRoleCode,
            ExpenseRequestWorkflowCodes.ResponsibleAccountantRoleCode,
            ExpenseRequestWorkflowCodes.TreasurerRoleCode,
        }.Order(StringComparer.Ordinal).ToArray());

    private static readonly IReadOnlyList<string> publicRoleCodes =
        Array.AsReadOnly(businessRoleCodes
            .Concat(new[]
            {
                BmsAccessProfileCodes.IntegrationAdministrator,
                BmsAccessProfileCodes.SystemAdministrator,
            })
            .Order(StringComparer.Ordinal)
            .ToArray());

    public static IReadOnlyList<string> BusinessRoleCodes => businessRoleCodes;

    public static IReadOnlyList<string> PublicRoleCodes => publicRoleCodes;

    /// <summary>Raw SQL role codes, including technical aliases. Never classify display names.</summary>
    public static bool IsTechnicalAssignmentCode(string? code) => code is not null &&
        new[] { BmsAccessProfileCodes.SystemAdministrator, BmsAccessProfileCodes.IntegrationAdministrator,
            SystemAdministratorAssignmentCode, SystemAdministratorAssignmentAlias,
            IntegrationAdministratorAssignmentCode, IntegrationAdministratorAssignmentAlias }
        .Contains(code, StringComparer.OrdinalIgnoreCase);

    public static bool IsBusinessRole(string? roleCode) =>
        roleCode is not null && businessRoleCodes.Contains(roleCode, StringComparer.Ordinal);

    public static bool IsPublicRole(string? roleCode) =>
        roleCode is not null && publicRoleCodes.Contains(roleCode, StringComparer.Ordinal);

    public static bool TryMapAssignmentCode(string assignmentCode, out string publicRoleCode)
    {
        ArgumentNullException.ThrowIfNull(assignmentCode);

        if (IsBusinessRole(assignmentCode))
        {
            publicRoleCode = assignmentCode;
            return true;
        }

        if (string.Equals(
                assignmentCode,
                SystemAdministratorAssignmentCode,
                StringComparison.Ordinal)
            || string.Equals(
                assignmentCode,
                SystemAdministratorAssignmentAlias,
                StringComparison.Ordinal))
        {
            publicRoleCode = BmsAccessProfileCodes.SystemAdministrator;
            return true;
        }

        if (string.Equals(
                assignmentCode,
                IntegrationAdministratorAssignmentCode,
                StringComparison.Ordinal)
            || string.Equals(
                assignmentCode,
                IntegrationAdministratorAssignmentAlias,
                StringComparison.Ordinal))
        {
            publicRoleCode = BmsAccessProfileCodes.IntegrationAdministrator;
            return true;
        }

        publicRoleCode = string.Empty;
        return false;
    }

    public static string? FindActiveBackingAssignment(
        string selectedPublicRoleCode,
        IReadOnlyList<string> activeAssignmentCodes)
    {
        ArgumentNullException.ThrowIfNull(selectedPublicRoleCode);
        ArgumentNullException.ThrowIfNull(activeAssignmentCodes);

        if (!IsPublicRole(selectedPublicRoleCode))
        {
            return null;
        }

        return activeAssignmentCodes
            .Where(code => TryMapAssignmentCode(code, out var mapped)
                && string.Equals(mapped, selectedPublicRoleCode, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
