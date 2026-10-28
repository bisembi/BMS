using Bms.Domain.Common;

namespace Bms.Domain.Workflow;

/// <summary>Context used for both command discovery and server validation.</summary>
public sealed class WorkflowEvaluationContext
{
    private WorkflowEvaluationContext(
        string statusCode,
        int definitionVersion,
        int stageNumber,
        IReadOnlySet<string> effectiveRoles,
        bool isBmsAdministrator,
        bool isInitiatorOfDocument,
        IReadOnlySet<string> satisfiedConditions)
    {
        StatusCode = statusCode;
        DefinitionVersion = definitionVersion;
        StageNumber = stageNumber;
        EffectiveRoles = effectiveRoles;
        IsBmsAdministrator = isBmsAdministrator;
        IsInitiatorOfDocument = isInitiatorOfDocument;
        SatisfiedConditions = satisfiedConditions;
    }

    public string StatusCode { get; }

    public int DefinitionVersion { get; }

    public int StageNumber { get; }

    public IReadOnlySet<string> EffectiveRoles { get; }

    public bool IsBmsAdministrator { get; }

    public bool IsInitiatorOfDocument { get; }

    public IReadOnlySet<string> SatisfiedConditions { get; }

    public static WorkflowEvaluationContext Create(
        string statusCode,
        int definitionVersion,
        int stageNumber,
        IEnumerable<string> functionalRoles,
        bool isBmsAdministrator,
        bool isInitiatorOfDocument,
        IEnumerable<string>? satisfiedConditions = null,
        bool canExecuteInitiatorCommands = true)
    {
        ArgumentNullException.ThrowIfNull(functionalRoles);
        if (string.IsNullOrWhiteSpace(statusCode))
        {
            throw new DomainRuleViolationException("Current status code is required.");
        }

        if (definitionVersion <= 0)
        {
            throw new DomainRuleViolationException("Workflow definition version must be positive.");
        }

        if (stageNumber < 0)
        {
            throw new DomainRuleViolationException("Workflow stage number cannot be negative.");
        }

        var roles = NormalizeSet(functionalRoles);
        if (roles.Contains(WorkflowPermission.InitiatorRole))
        {
            throw new DomainRuleViolationException(
                $"Reserved workflow subject '{WorkflowPermission.InitiatorRole}' cannot be supplied as a functional role.");
        }

        // Authorship is immutable; initiator-only command authority may be
        // narrowed by the currently selected functional role independently.
        if (isInitiatorOfDocument && canExecuteInitiatorCommands)
        {
            roles.Add(WorkflowPermission.InitiatorRole);
        }

        return new WorkflowEvaluationContext(
            statusCode.Trim(),
            definitionVersion,
            stageNumber,
            roles,
            isBmsAdministrator,
            isInitiatorOfDocument,
            NormalizeSet(satisfiedConditions ?? []));
    }

    private static HashSet<string> NormalizeSet(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.Ordinal);
}

/// <summary>A visible command and its current enabled state.</summary>
public sealed record AvailableCommand(
    string Code,
    string Name,
    bool RequiresReason,
    bool IsEnabled,
    int SortOrder);

/// <summary>Deterministic result of one approved transition rule.</summary>
public sealed record TransitionPlan(
    AvailableCommand Command,
    DocumentStatus NextStatus,
    int DefinitionVersion,
    int SourceStageNumber,
    int TargetStageNumber);

/// <summary>
/// Fail-closed resolver for the versioned workflow matrix.
///
/// No provisional status, command or rule can be shown or executed. The BMS
/// administrator is read-only, and an approval action is unavailable to the
/// request initiator. These are domain invariants, not configurable grants.
/// </summary>
public static class WorkflowResolver
{
    public static IReadOnlyList<AvailableCommand> AvailableCommands(
        WorkflowEvaluationContext context,
        IEnumerable<WorkflowPermission> matrix,
        IEnumerable<WorkflowCommand> commands,
        IEnumerable<DocumentStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(statuses);

        if (context.IsBmsAdministrator)
        {
            return [];
        }

        var approvedStatuses = statuses
            .Where(status => !status.IsProvisional)
            .ToDictionary(status => status.Code, StringComparer.Ordinal);
        if (!approvedStatuses.ContainsKey(context.StatusCode))
        {
            return [];
        }

        var approvedCommands = commands
            .Where(command => !command.IsProvisional)
            .ToDictionary(command => command.Code, StringComparer.Ordinal);
        var result = new Dictionary<string, AvailableCommand>(StringComparer.Ordinal);
        var enabledTargets = new Dictionary<string, HashSet<WorkflowTarget>>(StringComparer.Ordinal);

        foreach (var rule in MatchingVisibleRules(context, matrix))
        {
            if (!approvedCommands.TryGetValue(rule.CommandCode, out var command))
            {
                continue;
            }

            if (command.IsApprovalAction && context.IsInitiatorOfDocument && !IsTreasuryReturn(context, command.Code))
            {
                continue;
            }

            var conditionSatisfied = string.IsNullOrEmpty(rule.ConditionCode)
                || context.SatisfiedConditions.Contains(rule.ConditionCode);
            var targetConfigured = rule.TargetStatusCode is null
                || approvedStatuses.ContainsKey(rule.TargetStatusCode);
            var isEnabled = rule.IsEnabled && conditionSatisfied && targetConfigured;
            if (isEnabled)
            {
                if (!enabledTargets.TryGetValue(command.Code, out var commandTargets))
                {
                    commandTargets = [];
                    enabledTargets.Add(command.Code, commandTargets);
                }

                commandTargets.Add(new WorkflowTarget(
                    rule.TargetStatusCode ?? context.StatusCode,
                    rule.TargetStageNumber));
            }

            if (result.TryGetValue(command.Code, out var existing))
            {
                result[command.Code] = existing with
                {
                    IsEnabled = existing.IsEnabled || isEnabled
                };
                continue;
            }

            result[command.Code] = new AvailableCommand(
                command.Code,
                command.Name,
                command.RequiresReason,
                isEnabled,
                command.SortOrder);
        }

        foreach (var commandCode in result.Keys.ToArray())
        {
            if (enabledTargets.TryGetValue(commandCode, out var targets) && targets.Count > 1)
            {
                result[commandCode] = result[commandCode] with { IsEnabled = false };
            }
        }

        return result.Values
            .OrderBy(command => command.SortOrder)
            .ThenBy(command => command.Code, StringComparer.Ordinal)
            .ToArray();
    }

    public static TransitionPlan PlanTransition(
        WorkflowEvaluationContext context,
        string commandCode,
        string? reason,
        IEnumerable<WorkflowPermission> matrix,
        IReadOnlyCollection<WorkflowCommand> commands,
        IReadOnlyCollection<DocumentStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandCode);
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(statuses);

        if (context.IsBmsAdministrator)
        {
            throw new DomainRuleViolationException(
                "The BMS administrator is read-only and cannot execute workflow commands.");
        }

        var ruleSnapshot = matrix.ToArray();

        var command = commands.FirstOrDefault(candidate =>
            !candidate.IsProvisional
            && string.Equals(candidate.Code, commandCode, StringComparison.Ordinal));
        if (command is null)
        {
            throw NotAllowed(commandCode, context.StatusCode);
        }

        if (command.IsApprovalAction && context.IsInitiatorOfDocument && !IsTreasuryReturn(context, command.Code))
        {
            throw new DomainRuleViolationException(
                "The initiator cannot approve their own expense request.");
        }

        var executableRules = MatchingVisibleRules(context, ruleSnapshot)
            .Where(rule => rule.IsEnabled)
            .Where(rule => string.Equals(rule.CommandCode, commandCode, StringComparison.Ordinal))
            .Where(rule => string.IsNullOrEmpty(rule.ConditionCode)
                || context.SatisfiedConditions.Contains(rule.ConditionCode))
            .ToArray();

        var exactTargets = executableRules
            .Select(rule => new WorkflowTarget(
                rule.TargetStatusCode ?? context.StatusCode,
                rule.TargetStageNumber))
            .Distinct()
            .ToArray();
        if (exactTargets.Length != 1)
        {
            if (exactTargets.Length == 0)
            {
                throw NotAllowed(commandCode, context.StatusCode);
            }

            throw new DomainRuleViolationException(
                $"Workflow command '{commandCode}' has an ambiguous target status or stage.");
        }

        var available = AvailableCommands(context, ruleSnapshot, commands, statuses)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Code, commandCode, StringComparison.Ordinal));
        if (available is null || !available.IsEnabled)
        {
            throw NotAllowed(commandCode, context.StatusCode);
        }

        if (command.RequiresReason && string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainRuleViolationException($"Command '{command.Name}' requires a reason.");
        }

        var nextStatus = statuses.FirstOrDefault(status =>
            !status.IsProvisional
            && string.Equals(status.Code, exactTargets[0].StatusCode, StringComparison.Ordinal));
        if (nextStatus is null)
        {
            throw new DomainRuleViolationException(
                $"Approved target status '{exactTargets[0].StatusCode}' is not configured.");
        }

        return new TransitionPlan(
            available,
            nextStatus,
            context.DefinitionVersion,
            context.StageNumber,
            exactTargets[0].StageNumber);
    }

    private static IEnumerable<WorkflowPermission> MatchingVisibleRules(
        WorkflowEvaluationContext context,
        IEnumerable<WorkflowPermission> matrix) =>
        matrix.Where(rule =>
            !rule.IsProvisional
            && rule.IsVisible
            && rule.DefinitionVersion == context.DefinitionVersion
            && rule.StageNumber == context.StageNumber
            && string.Equals(rule.StatusCode, context.StatusCode, StringComparison.Ordinal)
            && context.EffectiveRoles.Contains(rule.RoleCode));

    private static bool IsTreasuryReturn(WorkflowEvaluationContext context, string commandCode) =>
        commandCode == ExpenseRequestWorkflowCodes.ReturnCommandCode
        && context.StatusCode is "APPROVED" or "TO_PAY"
        && context.EffectiveRoles.Contains(ExpenseRequestWorkflowCodes.TreasurerRoleCode);

    private static DomainRuleViolationException NotAllowed(string commandCode, string statusCode) =>
        new($"Command '{commandCode}' is not allowed in status '{statusCode}' for the current context.");

    private sealed record WorkflowTarget(string StatusCode, int StageNumber);
}
