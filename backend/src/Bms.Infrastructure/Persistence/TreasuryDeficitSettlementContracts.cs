using Bms.Domain.Overpayments;

namespace Bms.Infrastructure.Persistence;

internal sealed record TreasuryDeficitSettlementCommand(
    Guid CommandId,
    Guid OrganizationId,
    Guid CounterpartyId,
    Guid CurrencyId,
    string CurrencyCode,
    RealOverpaymentDeficitSettlementKind Kind,
    string SourceReference,
    Guid? SourceExpenseRequestId,
    decimal SourceAmount,
    decimal SourceAmountKzt,
    Guid UserId);

internal sealed record StoredOverpaymentDeficit(
    Guid OrganizationId,
    Guid CounterpartyId,
    Guid CurrencyId,
    string CurrencyCode,
    Guid DeficitId,
    decimal OriginalAmount,
    decimal OriginalAmountKzt,
    decimal CurrentAmount,
    decimal CurrentAmountKzt,
    Guid SourceOverpaymentId,
    string ExternalReversalId,
    DateTimeOffset OccurredAtUtc,
    Guid? SourceExpenseRequestId,
    Guid CreationCommandId,
    Guid? ActualCorrectionExecutorId,
    DateTimeOffset? SettledAtUtc);

internal sealed record StoredOverpaymentDeficitSettlement(
    Guid DeficitId,
    Guid CommandId,
    string Kind,
    string SourceReference,
    Guid? SourceExpenseRequestId,
    decimal Amount,
    decimal AmountKzt,
    DateTimeOffset AppliedAtUtc,
    Guid UserId);

internal sealed record TreasuryDeficitSettlementResult(
    decimal SourceAmount,
    decimal SourceAmountKzt,
    decimal AppliedAmount,
    decimal AppliedAmountKzt,
    decimal RemainingSourceAmount,
    decimal RemainingSourceAmountKzt,
    bool HasUnsettledDeficit,
    IReadOnlyList<StoredOverpaymentDeficitSettlement> Settlements,
    int AffectedEntries);

internal sealed class TreasuryDeficitSettlementUnavailableException : Exception
{
    private const string SafeMessage =
        "The overpayment deficit settlement could not be stored consistently.";

    internal TreasuryDeficitSettlementUnavailableException()
        : base(SafeMessage)
    {
    }

    internal TreasuryDeficitSettlementUnavailableException(Exception innerException)
        : base(
            SafeMessage,
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}

internal interface ITreasuryDeficitSettlementRuntime
{
    bool HasSerializableTransaction { get; }

    bool HasPendingChanges { get; }

    Task AcquireExclusiveTransactionLockAsync(
        string resource,
        CancellationToken cancellationToken);

    Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> ExpenseRequestMatchesScopeAsync(
        Guid expenseRequestId,
        Guid organizationId,
        Guid counterpartyId,
        Guid currencyId,
        CancellationToken cancellationToken);

    Task<bool> SettlementCommandExistsAsync(
        Guid commandId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredOverpaymentDeficitSettlement>> FindByCommandAsync(
        Guid commandId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredOverpaymentDeficitSettlement>> FindBySourceAsync(
        string kind,
        string sourceReference,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredOverpaymentDeficit>> FindActiveDeficitsAsync(
        Guid organizationId,
        Guid counterpartyId,
        Guid currencyId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> FindMatchingOverpaymentSourceIdsAsync(
        IReadOnlyCollection<Guid> sourceIds,
        Guid organizationId,
        Guid counterpartyId,
        Guid currencyId,
        CancellationToken cancellationToken);

    Task<int> UpdateDeficitAsync(
        StoredOverpaymentDeficit expectedDeficit,
        decimal amountAfter,
        decimal amountKztAfter,
        DateTimeOffset? settledAtUtc,
        CancellationToken cancellationToken);

    Task<int> InsertSettlementAsync(
        StoredOverpaymentDeficitSettlement settlement,
        CancellationToken cancellationToken);

    Task<StoredOverpaymentDeficit?> FindDeficitByIdAsync(
        Guid deficitId,
        CancellationToken cancellationToken);
}
