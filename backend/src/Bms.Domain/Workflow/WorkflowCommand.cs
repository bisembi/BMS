using Bms.Domain.Common;

namespace Bms.Domain.Workflow;

/// <summary>
/// Route command of an expense request. A command has no global target: the
/// exact target status, route stage, definition version and condition belong to
/// <see cref="WorkflowPermission"/>.
/// </summary>
public sealed class WorkflowCommand
{
    private WorkflowCommand()
    {
    }

    private WorkflowCommand(
        string code,
        string name,
        bool requiresReason,
        bool isApprovalAction,
        int sortOrder,
        bool isProvisional)
    {
        Code = code;
        Name = name;
        RequiresReason = requiresReason;
        IsApprovalAction = isApprovalAction;
        SortOrder = sortOrder;
        IsProvisional = isProvisional;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>Commands such as Вернуть and Отклонить require a written reason (ТЗ 13).</summary>
    public bool RequiresReason { get; private set; }

    /// <summary>
    /// Marks an approval action so the invariant "the initiator cannot approve
    /// their own request" can be enforced independently of a display name.
    /// </summary>
    public bool IsApprovalAction { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsProvisional { get; private set; }

    public static WorkflowCommand Create(
        string code,
        string name,
        bool requiresReason = false,
        bool isApprovalAction = false,
        int sortOrder = 0,
        bool isProvisional = true)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainRuleViolationException("Command code is required.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("Command name is required.");
        }

        var normalizedCode = NormalizeRequired(code, 100, "Command code");
        var normalizedName = NormalizeRequired(name, 200, "Command name");
        return new WorkflowCommand(
            normalizedCode,
            normalizedName,
            requiresReason,
            isApprovalAction,
            sortOrder,
            isProvisional);
    }

    private static string NormalizeRequired(string? value, int maxLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleViolationException($"{fieldName} is required.");
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new DomainRuleViolationException($"{fieldName} cannot exceed {maxLength} characters.");
        }

        return normalized;
    }
}
