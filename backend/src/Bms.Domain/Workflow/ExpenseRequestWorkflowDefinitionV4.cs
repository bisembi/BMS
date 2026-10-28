namespace Bms.Domain.Workflow;

/// <summary>
/// Code-versioned successor of the exact SQL V3 seed. Old V3 snapshots retain
/// their original semantics. New senior submissions above the threshold require
/// an independent controller before the general director, never self-approval.
/// No SQL permission rows are added or silently rewritten.
/// </summary>
public static class ExpenseRequestWorkflowDefinitionV4
{
    public const int Version = 4;

    public static bool TryCreate(IEnumerable<DocumentStatus>? statuses, IEnumerable<WorkflowCommand>? commands,
        IEnumerable<WorkflowPermission>? permissions, out ExpenseRequestWorkflowDefinition? definition)
    {
        definition = null;
        // The persisted V3 matrix must still match every reviewed field.
        if (!ExpenseRequestWorkflowDefinitionV3.TryCreate(statuses, commands, permissions, out var seed)
            || seed is null) return false;
        var rows = seed.Permissions.Where(p => p.StatusCode != "IN_APPROVAL")
            .Select(p => WorkflowPermission.Create(Version, p.StatusCode, p.StageNumber, p.RoleCode,
                p.CommandCode, p.TargetStatusCode, p.TargetStageNumber, p.ConditionCode,
                p.IsVisible, p.IsEnabled, p.IsProvisional)).ToList();
        rows.Add(Rule(1, "FIN_CONTROLLER", "APPROVE", "IN_APPROVAL", 2, "ROUTE_HAS_NEXT_STAGE"));
        rows.Add(Rule(2, "GENERAL_DIRECTOR", "APPROVE", "TO_PAY", 0, "ROUTE_LAST_NO_NSI"));
        rows.Add(Rule(2, "GENERAL_DIRECTOR", "APPROVE", "AWAITING_NSI", 0, "ROUTE_LAST_REQUIRES_NSI"));
        foreach (var (stage, role) in new[] { (1, "FIN_CONTROLLER"), (2, "GENERAL_DIRECTOR") })
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
