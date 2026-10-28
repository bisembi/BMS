using Bms.Domain.Overpayments;

namespace Bms.Application.OverpaymentDeficits;

public enum ManualDeficitCorrectionOperation
{
    Create = 1,
    Approve = 2,
    Reject = 3
}

public sealed record CreateManualDeficitCorrectionCommand(decimal Amount, string Basis);

public sealed record ApproveManualDeficitCorrectionCommand(string? Comment);

public sealed record RejectManualDeficitCorrectionCommand(string Reason, string? Comment);

/// <summary>
/// Private application/persistence boundary, not an HTTP response. The employee
/// and command identities remain available for transactional audit and replay.
/// </summary>
public sealed record ManualDeficitCorrectionSnapshot(
    ManualOverpaymentDeficitCorrectionProposal Proposal,
    Guid CreationCommandId,
    Guid? DecisionCommandId,
    Guid? FinancialDirectorId,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionComment,
    string? RejectionReason);

public sealed record ManualDeficitCorrectionContext(
    ManualDeficitCorrectionSnapshot Correction,
    OverpaymentDeficitSnapshot Deficit);

/// <summary>
/// Business result of a correction. No external payment/source identifiers,
/// employee UUIDs or internal command keys are exposed by this projection.
/// </summary>
public sealed record ManualDeficitCorrectionDto(
    Guid Id,
    Guid DeficitId,
    string CurrencyCode,
    string State,
    decimal Amount,
    decimal AmountKzt,
    decimal DeficitAmountAtCreation,
    decimal DeficitAmountKztAtCreation,
    string Basis,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionComment,
    string? RejectionReason);

public sealed record ManualDeficitCorrectionCommandResult(
    ManualDeficitCorrectionDto Correction,
    bool IsReplay,
    int ResponseStatusCode);

public sealed class ManualDeficitCorrectionNotFoundException : Exception
{
    public ManualDeficitCorrectionNotFoundException()
        : base("The overpayment deficit or correction is not available.")
    {
    }
}

public sealed class ManualDeficitCorrectionConflictException : Exception
{
    public ManualDeficitCorrectionConflictException()
        : base("The overpayment deficit correction state has changed.")
    {
    }
}

public sealed class ManualDeficitCorrectionUnavailableException : Exception
{
    public ManualDeficitCorrectionUnavailableException()
        : base("The overpayment deficit correction could not be processed consistently.")
    {
    }

    public ManualDeficitCorrectionUnavailableException(Exception innerException)
        : base("The overpayment deficit correction could not be processed consistently.", innerException)
    {
    }
}
