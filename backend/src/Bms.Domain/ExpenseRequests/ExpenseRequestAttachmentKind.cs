using Bms.Domain.Common;

namespace Bms.Domain.ExpenseRequests;

/// <summary>Business purpose of an immutable expense-request attachment.</summary>
public enum ExpenseRequestAttachmentKind
{
    SupportingDocument = 1,
    NewContractDocument = 2,
    AdditionalDocument = 3
}

public static class ExpenseRequestAttachmentKindCode
{
    // These are shared wire and SQL-storage values. Keep them aligned with
    // SQL031 and the strict frontend public-response parser.
    public const string SupportingDocument = "SUPPORTING";
    public const string NewContractDocument = "NEW_CONTRACT";
    public const string AdditionalDocument = "ADDITIONAL";

    public static string ToCode(this ExpenseRequestAttachmentKind kind) => kind switch
    {
        ExpenseRequestAttachmentKind.SupportingDocument => SupportingDocument,
        ExpenseRequestAttachmentKind.NewContractDocument => NewContractDocument,
        ExpenseRequestAttachmentKind.AdditionalDocument => AdditionalDocument,
        _ => throw new DomainRuleViolationException("Attachment kind is not defined.")
    };

    public static bool TryParse(string? code, out ExpenseRequestAttachmentKind kind)
    {
        kind = code switch
        {
            SupportingDocument => ExpenseRequestAttachmentKind.SupportingDocument,
            NewContractDocument => ExpenseRequestAttachmentKind.NewContractDocument,
            AdditionalDocument => ExpenseRequestAttachmentKind.AdditionalDocument,
            _ => default
        };
        return kind != default;
    }

    public static ExpenseRequestAttachmentKind Parse(string? code) =>
        TryParse(code, out var kind)
            ? kind
            : throw new DomainRuleViolationException("Attachment kind code is not canonical.");
}
