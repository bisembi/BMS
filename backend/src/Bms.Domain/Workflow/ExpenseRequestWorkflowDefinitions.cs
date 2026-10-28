namespace Bms.Domain.Workflow;

public static class ExpenseRequestWorkflowDefinitions
{
    public static bool TryCreate(int version, IEnumerable<DocumentStatus>? statuses,
        IEnumerable<WorkflowCommand>? commands, IEnumerable<WorkflowPermission>? permissions,
        out ExpenseRequestWorkflowDefinition? definition)
    {
        definition = null;
        if (PayrollAdvanceWorkflow372.IsVersion(version))
            return PayrollAdvanceWorkflow372.TryCreate(version, statuses, commands, permissions, out definition);
        if (ExpenseRequestWorkflowDefinition251.IsVersion(version))
            return ExpenseRequestWorkflowDefinition251.TryCreate(version, statuses, commands, permissions, out definition);
        return version switch
        {
            1 => ExpenseRequestWorkflowDefinitionV1.TryCreate(statuses, commands, permissions, out definition),
            2 => ExpenseRequestWorkflowDefinitionV2.TryCreate(statuses, commands, permissions, out definition),
            3 => ExpenseRequestWorkflowDefinitionV3.TryCreate(statuses, commands, permissions, out definition),
            4 => ExpenseRequestWorkflowDefinitionV4.TryCreate(statuses, commands, permissions, out definition),
            5 => ExpenseRequestWorkflowDefinitionV5.TryCreate(statuses, commands, permissions, out definition),
            _ => false,
        };
    }
}
