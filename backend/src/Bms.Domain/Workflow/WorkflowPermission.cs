using Bms.Domain.Common;

namespace Bms.Domain.Workflow;

/// <summary>
/// One versioned transition rule of the Status x Stage x Role x Command matrix
/// (OPEN-01/OPEN-02).
///
/// A rule carries the exact target status and an optional condition code. A
/// missing or provisional rule is always a denial.
/// </summary>
public sealed class WorkflowPermission
{
    /// <summary>
    /// Subject of the document rather than a functional role (ТЗ section 3):
    /// the initiator is an attribute of the expense request, not a directory item.
    /// </summary>
    public const string InitiatorRole = "Инициатор";

    private WorkflowPermission()
    {
    }

    private WorkflowPermission(
        int definitionVersion,
        string statusCode,
        int stageNumber,
        string roleCode,
        string commandCode,
        string? targetStatusCode,
        int targetStageNumber,
        string conditionCode,
        bool isVisible,
        bool isEnabled,
        bool isProvisional)
    {
        DefinitionVersion = definitionVersion;
        StatusCode = statusCode;
        StageNumber = stageNumber;
        RoleCode = roleCode;
        CommandCode = commandCode;
        TargetStatusCode = targetStatusCode;
        TargetStageNumber = targetStageNumber;
        ConditionCode = conditionCode;
        IsVisible = isVisible;
        IsEnabled = isEnabled;
        IsProvisional = isProvisional;
    }

    public int DefinitionVersion { get; private set; }

    public string StatusCode { get; private set; } = string.Empty;

    public int StageNumber { get; private set; }

    public string RoleCode { get; private set; } = string.Empty;

    public string CommandCode { get; private set; } = string.Empty;

    /// <summary>Exact next status. Null means that the status does not change.</summary>
    public string? TargetStatusCode { get; private set; }

    /// <summary>Exact next route stage, including same-status stage advances.</summary>
    public int TargetStageNumber { get; private set; }

    /// <summary>Empty means unconditional; any other code is fail-closed.</summary>
    public string ConditionCode { get; private set; } = string.Empty;

    public bool IsVisible { get; private set; }

    public bool IsEnabled { get; private set; }

    public bool IsProvisional { get; private set; }

    public static WorkflowPermission Create(
        int definitionVersion,
        string statusCode,
        int stageNumber,
        string roleCode,
        string commandCode,
        string? targetStatusCode,
        int targetStageNumber,
        string? conditionCode = null,
        bool isVisible = true,
        bool isEnabled = true,
        bool isProvisional = true)
    {
        if (definitionVersion <= 0)
        {
            throw new DomainRuleViolationException("Workflow definition version must be positive.");
        }

        if (stageNumber < 0)
        {
            throw new DomainRuleViolationException("Workflow stage number cannot be negative.");
        }

        if (targetStageNumber < 0)
        {
            throw new DomainRuleViolationException("Target workflow stage number cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(statusCode))
        {
            throw new DomainRuleViolationException("Status code is required.");
        }

        if (string.IsNullOrWhiteSpace(roleCode))
        {
            throw new DomainRuleViolationException("Role code is required.");
        }

        if (string.IsNullOrWhiteSpace(commandCode))
        {
            throw new DomainRuleViolationException("Command code is required.");
        }

        var normalizedStatusCode = NormalizeRequired(statusCode, 100, "Status code");
        var normalizedRoleCode = NormalizeRequired(roleCode, 100, "Role code");
        var normalizedCommandCode = NormalizeRequired(commandCode, 100, "Command code");
        var normalizedTargetStatusCode = NormalizeOptional(
            targetStatusCode,
            100,
            "Target status code");
        var normalizedConditionCode = NormalizeOptional(conditionCode, 100, "Condition code")
            ?? string.Empty;

        return new WorkflowPermission(
            definitionVersion,
            normalizedStatusCode,
            stageNumber,
            normalizedRoleCode,
            normalizedCommandCode,
            normalizedTargetStatusCode,
            targetStageNumber,
            normalizedConditionCode,
            isVisible,
            isEnabled,
            isProvisional);
    }

    private static string NormalizeRequired(string value, int maxLength, string fieldName) =>
        NormalizeOptional(value, maxLength, fieldName)
        ?? throw new DomainRuleViolationException($"{fieldName} is required.");

    private static string? NormalizeOptional(string? value, int maxLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new DomainRuleViolationException($"{fieldName} cannot exceed {maxLength} characters.");
        }

        return normalized;
    }
}
