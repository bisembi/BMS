using Bms.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// SQL reader for the workflow registry. It deliberately returns the raw
/// rows; the application layer accepts them only after the exact v1 verifier
/// has compared every field and transition.
/// </summary>
public sealed class ExpenseRequestWorkflowDefinitionReader(BmsDbContext dbContext)
    : IExpenseRequestWorkflowDefinitionReader
{
    public Task<ExpenseRequestWorkflowDefinitionSource> ReadAsync(CancellationToken cancellationToken) =>
        ReadVersionAsync(1, cancellationToken);

    public async Task<ExpenseRequestWorkflowDefinitionSource> ReadVersionAsync(int version,
        CancellationToken cancellationToken)
    {
        // V4 is a closed code-derived successor, gated by the unchanged SQL V3 seed.
        var seedVersion = version switch { 4 => 3, >= 5 and <= 21 => 2, _ => version };
        var statuses = await dbContext.DocumentStatuses
            .AsNoTracking()
            .OrderBy(status => status.Code)
            .ToArrayAsync(cancellationToken);
        var commands = await dbContext.WorkflowCommands
            .AsNoTracking()
            .OrderBy(command => command.Code)
            .ToArrayAsync(cancellationToken);
        var permissions = await dbContext.WorkflowPermissions
            .AsNoTracking()
            .Where(permission => permission.DefinitionVersion == seedVersion)
            .OrderBy(permission => permission.DefinitionVersion)
            .ThenBy(permission => permission.StatusCode)
            .ThenBy(permission => permission.StageNumber)
            .ThenBy(permission => permission.RoleCode)
            .ThenBy(permission => permission.CommandCode)
            .ThenBy(permission => permission.ConditionCode)
            .ToArrayAsync(cancellationToken);
        return new ExpenseRequestWorkflowDefinitionSource(
            Array.AsReadOnly(statuses),
            Array.AsReadOnly(commands),
            Array.AsReadOnly(permissions));
    }
}
