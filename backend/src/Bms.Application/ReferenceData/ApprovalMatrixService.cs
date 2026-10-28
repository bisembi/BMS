using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Workflow;

namespace Bms.Application.ReferenceData;

/// <summary>
/// Reads the same exact SQL registry verified by workflow execution. A missing,
/// provisional or drifted definition never falls back to a compiled fixture.
/// All three registry reads and the public projection belong to one attempt.
/// </summary>
public sealed class ApprovalMatrixService(
    IExpenseRequestWorkflowDefinitionReader reader,
    IBusinessTransactionRunner transactionRunner,
    ICurrentActor currentActor)
{
    public Task<ApprovalMatrixDto> GetAsync(CancellationToken cancellationToken)
        => GetVersionAsync(1, cancellationToken);

    public Task<ApprovalMatrixDto> GetVersionAsync(int version, CancellationToken cancellationToken)
    {
        ActorIdentity.RequireBusinessDataAccess(currentActor);
        cancellationToken.ThrowIfCancellationRequested();
        return transactionRunner.ExecuteAsync(
            async attemptCancellationToken =>
            {
                var source = await reader.ReadVersionAsync(version, attemptCancellationToken);
                attemptCancellationToken.ThrowIfCancellationRequested();
                ExpenseRequestWorkflowDefinition? definition = null;
                var valid = ExpenseRequestWorkflowDefinitions.TryCreate(version, source.Statuses,
                    source.Commands, source.Permissions, out definition);
                if (!valid || definition is null)
                {
                    throw new ApprovalMatrixUnavailableException();
                }

                return Project(definition);
            },
            cancellationToken);
    }

    private static ApprovalMatrixDto Project(ExpenseRequestWorkflowDefinition definition)
    {
        var statuses = definition.Statuses.ToDictionary(status => status.Code, StringComparer.Ordinal);
        var commands = definition.Commands.ToDictionary(command => command.Code, StringComparer.Ordinal);
        var transitions = definition.Permissions
            .OrderBy(rule => statuses[rule.StatusCode].SortOrder)
            .ThenBy(rule => rule.StageNumber)
            .ThenBy(rule => commands[rule.CommandCode].SortOrder)
            .ThenBy(rule => rule.RoleCode, StringComparer.Ordinal)
            .ThenBy(rule => rule.ConditionCode, StringComparer.Ordinal)
            .Select(rule => new ApprovalMatrixTransitionDto(
                rule.StatusCode,
                rule.StageNumber,
                rule.RoleCode == WorkflowPermission.InitiatorRole ? "INITIATOR" : rule.RoleCode,
                rule.CommandCode,
                rule.TargetStatusCode ?? throw new ApprovalMatrixUnavailableException(),
                rule.TargetStageNumber,
                rule.ConditionCode,
                commands[rule.CommandCode].RequiresReason))
            .ToArray();

        var threshold = ApprovalRoutePlanner.GeneralDirectorThresholdKzt;
        return new ApprovalMatrixDto(
            definition.Version,
            threshold,
            Array.AsReadOnly(definition.Statuses
                .Select(status => new ApprovalMatrixStatusDto(status.Code, status.Name)).ToArray()),
            Array.AsReadOnly<ApprovalMatrixRouteDto>(
            [
                Route("noDeficit", 0m, definition.Version),
                Route("withinThreshold", threshold, definition.Version),
                Route("aboveThreshold", threshold + 0.01m, definition.Version),
            ]),
            Array.AsReadOnly(transitions));
    }

    private static ApprovalMatrixRouteDto Route(string deficitBand, decimal deficitKzt, int version) => new(
        deficitBand,
        Array.AsReadOnly(ApprovalRoutePlanner.Plan(deficitKzt, version)
            .Select(stage => new ApprovalMatrixStageDto(stage.StageNumber, stage.RoleCode)).ToArray()));
}
