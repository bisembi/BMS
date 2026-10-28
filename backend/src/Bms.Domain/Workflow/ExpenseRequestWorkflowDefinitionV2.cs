using System.Text.Json;

namespace Bms.Domain.Workflow;

/// <summary>Closed initiator route; V1 rows are not rewritten or reinterpreted.</summary>
public static class ExpenseRequestWorkflowDefinitionV2
{
    public const int Version = 2;
    public static ExpenseRequestWorkflowDefinitionRows CreateExpectedRows()
    {
        var prior = ExpenseRequestWorkflowDefinitionV1.CreateExpectedRows();
        var permissions = prior.Permissions.Select(p => WorkflowPermission.Create(Version, p.StatusCode,
            p.StatusCode == "IN_APPROVAL" ? p.StageNumber + 1 : p.StageNumber, p.RoleCode, p.CommandCode,
            p.TargetStatusCode, p.StatusCode == "IN_APPROVAL" && p.TargetStageNumber > 0 ? p.TargetStageNumber + 1 : p.TargetStageNumber,
            p.ConditionCode, p.IsVisible, p.IsEnabled, p.IsProvisional)).ToList();
        permissions.AddRange(new[]
        {
            WorkflowPermission.Create(Version, "IN_APPROVAL", 1, "BUDGET_OWNER", "APPROVE", "IN_APPROVAL", 2,
                "ROUTE_HAS_NEXT_STAGE", isProvisional: false),
            WorkflowPermission.Create(Version, "IN_APPROVAL", 1, "BUDGET_OWNER", "RETURN", "RETURNED", 0, isProvisional: false),
            WorkflowPermission.Create(Version, "IN_APPROVAL", 1, "BUDGET_OWNER", "REJECT", "REJECTED", 0, isProvisional: false),
            WorkflowPermission.Create(Version, "IN_APPROVAL", 1, WorkflowPermission.InitiatorRole, "CANCEL", "CANCELLED", 0, isProvisional: false)
        });
        return new(prior.Statuses, prior.Commands, permissions.AsReadOnly());
    }

    public static bool TryCreate(IEnumerable<DocumentStatus>? statuses, IEnumerable<WorkflowCommand>? commands,
        IEnumerable<WorkflowPermission>? permissions, out ExpenseRequestWorkflowDefinition? definition)
    {
        definition = null;
        try
        {
            var expected = CreateExpectedRows();
            if (!ExpenseRequestWorkflowDefinitionV1.TryCreate(statuses, commands,
                ExpenseRequestWorkflowDefinitionV1.CreateExpectedRows().Permissions, out var verified) || verified is null
                || permissions is null) return false;
            static string Key(WorkflowPermission p) => JsonSerializer.Serialize(p);
            var actual = permissions.ToArray();
            if (actual.Any(p => p is null) || !actual.Select(Key).Order(StringComparer.Ordinal)
                .SequenceEqual(expected.Permissions.Select(Key).Order(StringComparer.Ordinal), StringComparer.Ordinal)) return false;
            definition = new ExpenseRequestWorkflowDefinition(verified.Statuses, verified.Commands,
                Array.AsReadOnly(actual), Version);
            return true;
        }
        catch { return false; }
    }
}
