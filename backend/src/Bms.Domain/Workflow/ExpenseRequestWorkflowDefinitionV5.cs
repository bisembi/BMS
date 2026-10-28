namespace Bms.Domain.Workflow;

/// <summary>
/// Direct FD author, another CFO, deficit up to and including 100000 KZT:
/// independent CFO owner -> controller -> payment (or NSI). Never self-approval.
/// Derived only from the exact SQL V2 seed; captured V1-V4 routes are unchanged.
/// </summary>
public static class ExpenseRequestWorkflowDefinitionV5
{
    public const int Version = 5;

    public static bool TryCreate(IEnumerable<DocumentStatus>? statuses, IEnumerable<WorkflowCommand>? commands,
        IEnumerable<WorkflowPermission>? permissions, out ExpenseRequestWorkflowDefinition? definition)
    {
        definition = null;
        if (!ExpenseRequestWorkflowDefinitionV2.TryCreate(statuses, commands, permissions, out var seed)
            || seed is null) return false;
        var rows = seed.Permissions.Where(p => p.StatusCode != "IN_APPROVAL")
            .Select(p => WorkflowPermission.Create(Version, p.StatusCode, p.StageNumber, p.RoleCode,
                p.CommandCode, p.CommandCode == "CONFIRM_NSI" ? "TO_PAY" : p.TargetStatusCode,
                p.TargetStageNumber, p.ConditionCode, p.IsVisible, p.IsEnabled, p.IsProvisional)).ToList();
        rows.Add(Rule(1, "BUDGET_OWNER", "APPROVE", "IN_APPROVAL", 2, "ROUTE_HAS_NEXT_STAGE"));
        rows.Add(Rule(2, "FIN_CONTROLLER", "APPROVE", "TO_PAY", 0, "ROUTE_LAST_NO_NSI"));
        rows.Add(Rule(2, "FIN_CONTROLLER", "APPROVE", "AWAITING_NSI", 0, "ROUTE_LAST_REQUIRES_NSI"));
        foreach (var (stage, role) in new[] { (1, "BUDGET_OWNER"), (2, "FIN_CONTROLLER") })
        {
            rows.Add(Rule(stage, role, "RETURN", "RETURNED", 0));
            rows.Add(Rule(stage, role, "REJECT", "REJECTED", 0));
            rows.Add(Rule(stage, WorkflowPermission.InitiatorRole, "CANCEL", "CANCELLED", 0));
        }
        definition = new(seed.Statuses, seed.Commands, rows.AsReadOnly(), Version);
        return true;
    }

    private static WorkflowPermission Rule(int stage, string role, string command, string target, int next, string condition = "") =>
        WorkflowPermission.Create(Version, "IN_APPROVAL", stage, role, command, target, next, condition,
            isVisible: true, isEnabled: true, isProvisional: false);
}
