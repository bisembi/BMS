using Bms.Domain.Common;

namespace Bms.Domain.Workflow;

/// <summary>
/// Builds the approval roles required by the KZT budget deficit.
/// The result is independent of workflow statuses, commands and employee assignments.
/// </summary>
public static class ApprovalRoutePlanner
{
    public const string FinancialControllerRoleCode =
        ExpenseRequestWorkflowCodes.FinancialControllerRoleCode;
    public const string FinancialDirectorRoleCode =
        ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode;
    public const string GeneralDirectorRoleCode =
        ExpenseRequestWorkflowCodes.GeneralDirectorRoleCode;

    public const decimal GeneralDirectorThresholdKzt = 100_000m;

    public static IReadOnlyList<ApprovalRouteStage> Plan(decimal deficitKzt)
    {
        ValidateDeficit(deficitKzt);

        var stages = new List<ApprovalRouteStage>(capacity: 3)
        {
            new(1, FinancialControllerRoleCode),
        };

        if (deficitKzt > 0m)
        {
            stages.Add(new ApprovalRouteStage(2, FinancialDirectorRoleCode));
        }

        if (deficitKzt > GeneralDirectorThresholdKzt)
        {
            stages.Add(new ApprovalRouteStage(3, GeneralDirectorRoleCode));
        }

        return stages.AsReadOnly();
    }

    public static IReadOnlyList<ApprovalRouteStage> Plan(decimal deficitKzt, int definitionVersion)
    {
        var financial = Plan(deficitKzt);
        if (PayrollAdvanceWorkflow372.IsVersion(definitionVersion)) return PayrollAdvanceWorkflow372.Plan(definitionVersion);
        if (ExpenseRequestWorkflowDefinition251.IsVersion(definitionVersion))
            return ExpenseRequestWorkflowDefinition251.Plan(deficitKzt, definitionVersion);
        return definitionVersion switch
        {
            1 => financial,
            2 => Array.AsReadOnly(new[] { new ApprovalRouteStage(1, ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode) }
                .Concat(financial.Select(s => new ApprovalRouteStage(s.StageNumber + 1, s.RoleCode))).ToArray()),
            3 => Array.AsReadOnly(deficitKzt > GeneralDirectorThresholdKzt
                ? new[] { new ApprovalRouteStage(1, GeneralDirectorRoleCode) }
                : Array.Empty<ApprovalRouteStage>()),
            4 => Array.AsReadOnly(deficitKzt > GeneralDirectorThresholdKzt
                ? new[] { new ApprovalRouteStage(1, FinancialControllerRoleCode), new ApprovalRouteStage(2, GeneralDirectorRoleCode) }
                : Array.Empty<ApprovalRouteStage>()),
            5 when deficitKzt <= GeneralDirectorThresholdKzt => Array.AsReadOnly(new[] {
                new ApprovalRouteStage(1, ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode),
                new ApprovalRouteStage(2, FinancialControllerRoleCode) }),
            _ => throw new DomainRuleViolationException("Unknown approval route version.")
        };
    }

    private static void ValidateDeficit(decimal deficitKzt)
    {
        MoneyAmountPolicy.EnsureNonNegative(deficitKzt, "KZT deficit");
    }
}

public sealed record ApprovalRouteStage(int StageNumber, string RoleCode);
