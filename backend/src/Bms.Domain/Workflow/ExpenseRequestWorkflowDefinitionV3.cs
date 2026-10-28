using System.Text.Json;

namespace Bms.Domain.Workflow;

/// <summary>
/// A financial director's direct submission of their own CFO's request is
/// their financial approval. V1/V2 and already captured routes are unchanged.
/// </summary>
public static class ExpenseRequestWorkflowDefinitionV3
{
    public const int Version = 3;
    public const string RequiresGeneralDirector = "SENIOR_GD_REQUIRED";
    public const string RequiresNsi = "SENIOR_NSI_REQUIRED";
    public const string ReadyForPayment = "SENIOR_READY_FOR_PAYMENT";

    public static ExpenseRequestWorkflowDefinitionRows CreateExpectedRows()
    {
        var prior = ExpenseRequestWorkflowDefinitionV1.CreateExpectedRows();
        var permissions = prior.Permissions
            .Where(p => p.StatusCode != "IN_APPROVAL" && p.CommandCode != "SUBMIT")
            .Select(p => WorkflowPermission.Create(Version, p.StatusCode, p.StageNumber,
                p.RoleCode, p.CommandCode, p.CommandCode == "CONFIRM_NSI" ? "TO_PAY" : p.TargetStatusCode,
                p.TargetStageNumber, p.ConditionCode, p.IsVisible, p.IsEnabled, p.IsProvisional)).ToList();
        foreach (var status in new[] { "DRAFT", "RETURNED" })
        {
            permissions.Add(Rule(status, 0, WorkflowPermission.InitiatorRole, "SUBMIT", "IN_APPROVAL", 1, RequiresGeneralDirector));
            permissions.Add(Rule(status, 0, WorkflowPermission.InitiatorRole, "SUBMIT", "AWAITING_NSI", 0, RequiresNsi));
            permissions.Add(Rule(status, 0, WorkflowPermission.InitiatorRole, "SUBMIT", "TO_PAY", 0, ReadyForPayment));
        }
        permissions.AddRange(new[]
        {
            Rule("IN_APPROVAL", 1, "GENERAL_DIRECTOR", "APPROVE", "TO_PAY", 0, "ROUTE_LAST_NO_NSI"),
            Rule("IN_APPROVAL", 1, "GENERAL_DIRECTOR", "APPROVE", "AWAITING_NSI", 0, "ROUTE_LAST_REQUIRES_NSI"),
            Rule("IN_APPROVAL", 1, "GENERAL_DIRECTOR", "RETURN", "RETURNED", 0),
            Rule("IN_APPROVAL", 1, "GENERAL_DIRECTOR", "REJECT", "REJECTED", 0),
            Rule("IN_APPROVAL", 1, WorkflowPermission.InitiatorRole, "CANCEL", "CANCELLED", 0),
        });
        return new(prior.Statuses, prior.Commands, permissions.AsReadOnly());
    }

    public static bool TryCreate(IEnumerable<DocumentStatus>? statuses, IEnumerable<WorkflowCommand>? commands,
        IEnumerable<WorkflowPermission>? permissions, out ExpenseRequestWorkflowDefinition? definition)
    {
        definition = null;
        try
        {
            if (!ExpenseRequestWorkflowDefinitionV1.TryCreate(statuses, commands,
                ExpenseRequestWorkflowDefinitionV1.CreateExpectedRows().Permissions, out var verified)
                || verified is null || permissions is null) return false;
            static string Key(WorkflowPermission p) => JsonSerializer.Serialize(p);
            var actual = permissions.ToArray();
            if (actual.Any(p => p is null) || !actual.Select(Key).Order(StringComparer.Ordinal)
                .SequenceEqual(CreateExpectedRows().Permissions.Select(Key).Order(StringComparer.Ordinal), StringComparer.Ordinal)) return false;
            definition = new(verified.Statuses, verified.Commands, Array.AsReadOnly(actual), Version);
            return true;
        }
        catch { return false; }
    }

    public static string SubmissionCondition(decimal deficit, bool requiresNsi)
    {
        // Use the same monetary validation and strict threshold as snapshots.
        var route = ApprovalRoutePlanner.Plan(deficit, Version);
        return route.Count > 0 ? RequiresGeneralDirector : requiresNsi ? RequiresNsi : ReadyForPayment;
    }

    private static WorkflowPermission Rule(string status, int stage, string role, string command,
        string target, int nextStage, string condition = "") =>
        WorkflowPermission.Create(Version, status, stage, role, command, target, nextStage, condition,
            isVisible: true, isEnabled: true, isProvisional: false);
}
