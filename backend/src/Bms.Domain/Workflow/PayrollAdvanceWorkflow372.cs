using Bms.Domain.Common;

namespace Bms.Domain.Workflow;

/// <summary>Closed advance routes. Bits encode the owner's required step and
/// the author's self-skipped finance/payroll steps, never the budget deficit.</summary>
public static class PayrollAdvanceWorkflow372
{
    public const string CalculatorRole = "PAYROLL_CALCULATOR";
    public static bool IsVersion(int version) => version is >= 14 and <= 21;
    public static int Version(bool ownerRequired, bool authorIsDirector, bool authorIsCalculator) =>
        14 + (ownerRequired ? 1 : 0) + (authorIsDirector ? 2 : 0) + (authorIsCalculator ? 4 : 0);
    public static IReadOnlyList<ApprovalRouteStage> Plan(int version)
    {
        if (!IsVersion(version)) throw new DomainRuleViolationException("Unknown advance route.");
        var bits = version - 14;
        var roles = new List<string>();
        if ((bits & 1) != 0) roles.Add("BUDGET_OWNER");
        if ((bits & 2) == 0) roles.Add("FIN_DIRECTOR");
        if ((bits & 4) == 0) roles.Add(CalculatorRole);
        return roles.Select((role, i) => new ApprovalRouteStage(i + 1, role)).ToArray();
    }
    public static bool TryCreate(int version, IEnumerable<DocumentStatus>? statuses, IEnumerable<WorkflowCommand>? commands,
        IEnumerable<WorkflowPermission>? permissions, out ExpenseRequestWorkflowDefinition? definition)
    {
        definition = null;
        if (!IsVersion(version) || !ExpenseRequestWorkflowDefinitionV2.TryCreate(statuses, commands, permissions, out var seed)
            || seed is null) return false;
        var rows = seed.Permissions.Where(p => p.StatusCode != "IN_APPROVAL" && p.CommandCode != "SUBMIT")
            .Select(p => WorkflowPermission.Create(version, p.StatusCode, p.StageNumber, p.RoleCode, p.CommandCode,
                p.TargetStatusCode, p.TargetStageNumber, p.ConditionCode, p.IsVisible, p.IsEnabled, p.IsProvisional)).ToList();
        var stages = Plan(version);
        foreach (var status in new[] { "DRAFT", "RETURNED" })
            rows.Add(Rule(status, 0, WorkflowPermission.InitiatorRole, "SUBMIT", stages.Count > 0 ? "IN_APPROVAL" : "APPROVED",
                stages.Count > 0 ? 1 : 0, "SUBMISSION_READY"));
        foreach (var stage in stages)
        {
            rows.Add(Rule("IN_APPROVAL", stage.StageNumber, stage.RoleCode, "APPROVE",
                stage.StageNumber < stages.Count ? "IN_APPROVAL" : "APPROVED",
                stage.StageNumber < stages.Count ? stage.StageNumber + 1 : 0,
                stage.StageNumber < stages.Count ? "ROUTE_HAS_NEXT_STAGE" : "ROUTE_LAST_NO_NSI"));
            foreach (var (command, target) in new[] { ("RETURN", "RETURNED"), ("REJECT", "REJECTED") })
                rows.Add(Rule("IN_APPROVAL", stage.StageNumber, stage.RoleCode, command, target, 0));
            rows.Add(Rule("IN_APPROVAL", stage.StageNumber, WorkflowPermission.InitiatorRole, "CANCEL", "CANCELLED", 0));
        }
        definition = ExpenseRequestWorkflowDefinition251.WithTreasuryRules(new(seed.Statuses, seed.Commands, rows.AsReadOnly(), version));
        return true;
        WorkflowPermission Rule(string status, int stage, string role, string command, string target, int next, string condition = "") =>
            WorkflowPermission.Create(version, status, stage, role, command, target, next, condition, isProvisional: false);
    }
}
