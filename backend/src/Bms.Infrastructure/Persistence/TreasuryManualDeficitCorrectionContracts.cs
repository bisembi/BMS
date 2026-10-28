using Bms.Domain.Overpayments;

namespace Bms.Infrastructure.Persistence;

internal enum TreasuryManualDeficitCorrectionDecisionKind
{
    Approve = 1,
    Reject = 2
}

internal sealed record TreasuryManualDeficitCorrectionCreateCommand(
    Guid CommandId,
    Guid CorrectionId,
    Guid DeficitId,
    decimal CorrectionAmount,
    string Basis,
    Guid ResponsibleAccountantId);

internal sealed record TreasuryManualDeficitCorrectionDecisionCommand(
    Guid CommandId,
    Guid CorrectionId,
    TreasuryManualDeficitCorrectionDecisionKind Kind,
    Guid FinancialDirectorId,
    string? DecisionComment,
    string? RejectionReason);

internal sealed record StoredManualOverpaymentDeficitCorrection(
    Guid CorrectionId,
    Guid DeficitId,
    Guid OrganizationId,
    Guid CounterpartyId,
    Guid CurrencyId,
    string CurrencyCode,
    decimal DeficitAmountAtCreation,
    decimal DeficitAmountKztAtCreation,
    decimal CorrectionAmount,
    decimal CorrectionAmountKzt,
    string Basis,
    Guid ResponsibleAccountantId,
    Guid? FinancialDirectorId,
    string State,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionComment,
    string? RejectionReason,
    Guid CreationCommandId,
    Guid? DecisionCommandId);

internal sealed record StoredManualDeficitSettlement(
    Guid DeficitId,
    Guid CommandId,
    string Kind,
    Guid CorrectionId,
    decimal Amount,
    decimal AmountKzt,
    DateTimeOffset AppliedAtUtc,
    Guid UserId);

internal sealed record TreasuryManualDeficitCorrectionCreateResult(
    StoredManualOverpaymentDeficitCorrection Correction,
    int AffectedEntries);

internal sealed record TreasuryManualDeficitCorrectionDecisionResult(
    StoredManualOverpaymentDeficitCorrection Correction,
    StoredOverpaymentDeficit Deficit,
    StoredManualDeficitSettlement? Settlement,
    int AffectedEntries);

internal sealed class TreasuryManualDeficitCorrectionUnavailableException : Exception
{
    private const string SafeMessage =
        "The manual overpayment deficit correction could not be stored consistently.";

    internal TreasuryManualDeficitCorrectionUnavailableException()
        : base(SafeMessage)
    {
    }

    internal TreasuryManualDeficitCorrectionUnavailableException(Exception innerException)
        : base(
            SafeMessage,
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}

internal interface ITreasuryManualDeficitCorrectionRuntime
{
    bool HasSerializableTransaction { get; }

    bool HasPendingChanges { get; }

    Task AcquireExclusiveTransactionLockAsync(
        string resource,
        CancellationToken cancellationToken);

    Task<bool> EmployeeExistsAsync(Guid employeeId, CancellationToken cancellationToken);

    Task<bool> CommandExistsAsync(Guid commandId, CancellationToken cancellationToken);

    Task<bool> SourceMatchesScopeAsync(
        Guid sourceId,
        Guid organizationId,
        Guid counterpartyId,
        Guid currencyId,
        Guid? sourceExpenseRequestId,
        DateTimeOffset deficitOccurredAtUtc,
        CancellationToken cancellationToken);

    Task<StoredOverpaymentDeficit?> FindDeficitByIdAsync(
        Guid deficitId,
        CancellationToken cancellationToken);

    Task<StoredManualOverpaymentDeficitCorrection?> FindCorrectionByIdAsync(
        Guid correctionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredManualOverpaymentDeficitCorrection>>
        FindPendingCorrectionsByDeficitAsync(
            Guid deficitId,
            CancellationToken cancellationToken);

    Task<StoredManualDeficitSettlement?> FindSettlementByCorrectionAsync(
        Guid correctionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredManualDeficitSettlement>> FindSettlementsByCommandAsync(
        Guid commandId,
        CancellationToken cancellationToken);

    Task<int> InsertCorrectionAsync(
        StoredManualOverpaymentDeficitCorrection correction,
        CancellationToken cancellationToken);

    Task<int> UpdateDeficitAsync(
        StoredOverpaymentDeficit expectedDeficit,
        decimal amountAfter,
        decimal amountKztAfter,
        Guid actualCorrectionExecutorId,
        DateTimeOffset? settledAtUtc,
        CancellationToken cancellationToken);

    Task<int> InsertSettlementAsync(
        StoredManualDeficitSettlement settlement,
        CancellationToken cancellationToken);

    Task<int> UpdateCorrectionDecisionAsync(
        StoredManualOverpaymentDeficitCorrection expectedCorrection,
        ManualOverpaymentDeficitCorrectionDecision decision,
        Guid decisionCommandId,
        DateTimeOffset decidedAtUtc,
        CancellationToken cancellationToken);
}
