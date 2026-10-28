using Bms.Domain.Common;

namespace Bms.Domain.Workflow;

/// <summary>
/// Displayed status of an expense request. OPEN-01 keeps the exact names and
/// the mapping to logical states unapproved, so both codes are stored as data.
/// Provisional rows are never executable.
/// </summary>
public sealed class DocumentStatus
{
    private DocumentStatus()
    {
    }

    private DocumentStatus(
        string code,
        string name,
        string logicalStateCode,
        string color,
        int sortOrder,
        bool isProvisional)
    {
        Code = code;
        Name = name;
        LogicalStateCode = logicalStateCode;
        Color = color;
        SortOrder = sortOrder;
        IsProvisional = isProvisional;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string LogicalStateCode { get; private set; } = string.Empty;

    public string Color { get; private set; } = "neutral";

    public int SortOrder { get; private set; }

    /// <summary>True while the name is not approved by the business (OPEN-01).</summary>
    public bool IsProvisional { get; private set; }

    public static DocumentStatus Create(
        string code,
        string name,
        string logicalStateCode,
        string color = "neutral",
        int sortOrder = 0,
        bool isProvisional = true)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainRuleViolationException("Status code is required.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("Status name is required.");
        }

        if (string.IsNullOrWhiteSpace(logicalStateCode))
        {
            throw new DomainRuleViolationException("Logical state code is required.");
        }

        var normalizedCode = NormalizeRequired(code, 100, "Status code");
        var normalizedName = NormalizeRequired(name, 200, "Status name");
        var normalizedLogicalStateCode = NormalizeRequired(
            logicalStateCode,
            100,
            "Logical state code");
        var normalizedColor = NormalizeRequired(color, 20, "Status color");

        return new DocumentStatus(
            normalizedCode,
            normalizedName,
            normalizedLogicalStateCode,
            normalizedColor,
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
