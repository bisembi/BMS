using Bms.Domain.Workflow;

namespace Bms.Application.Abstractions;

/// <summary>Reads the three immutable-in-practice rows of the approved v1 workflow.</summary>
public interface IExpenseRequestWorkflowDefinitionReader
{
    Task<ExpenseRequestWorkflowDefinitionSource> ReadAsync(CancellationToken cancellationToken);
    Task<ExpenseRequestWorkflowDefinitionSource> ReadVersionAsync(int version, CancellationToken token) => ReadAsync(token);
}

public sealed record ExpenseRequestWorkflowDefinitionSource(
    IReadOnlyList<DocumentStatus> Statuses,
    IReadOnlyList<WorkflowCommand> Commands,
    IReadOnlyList<WorkflowPermission> Permissions);
