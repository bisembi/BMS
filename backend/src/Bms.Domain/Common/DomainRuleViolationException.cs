namespace Bms.Domain.Common;

/// Reviewed business reasons are metadata, never text copied from an exception.
/// The exact exception type and internal message stay compatible with existing
/// callers; an unannotated violation still uses the generic public fallback.
public sealed class DomainRuleViolationException(string message) : Exception(message)
{
    public const int MaximumSubmittedRowIndex = 999_999;

    public DomainRuleViolationReason? Reason { get; }

    /// Zero-based position in the submitted lines, not a database identifier.
    public int? SubmittedRowIndex { get; }

    public DomainRuleViolationException(
        string message,
        DomainRuleViolationReason reason,
        int? submittedRowIndex = null) : this(message)
    {
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        Reason = reason;
        // A diagnostic bound must not introduce a new business validation rule.
        SubmittedRowIndex = submittedRowIndex is >= 0 and <= MaximumSubmittedRowIndex
            ? submittedRowIndex : null;
    }
}

public enum DomainRuleViolationReason
{
    BudgetArticleUnavailable,
    BudgetArticleCfoMismatch,
    BudgetInApproval,
    BudgetPeriodInvalid,
    BudgetLinesInvalid,
    BudgetNumberingUninitialized,
    ExpenseDraftContractMismatch,
    ExpenseDraftArticleCfoMismatch,
    ExpenseDraftLineCfoMismatch,
    ExpenseDraftNumberReservationExpired,
    ExpenseDraftPurposeManaged,
    ExpenseDraftUrgencyInvalid,
    ExpenseDraftCommentInvalid,
    ExpenseDraftJustificationInvalid,
    ExpenseDraftTextInvalid,
    ExpenseDraftCurrencyInvalid,
    ExpenseDraftUploadUnavailable,
    ExpenseAttachmentNewContractRequiresSave,
}
