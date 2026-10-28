using Bms.Domain.Common;
using Bms.Domain.Payments;

namespace Bms.Infrastructure.Persistence;

internal enum TreasuryPaymentFactWriteOutcome
{
    Stored = 1,
    AlreadyStored = 2
}

internal sealed record TreasuryPaymentFactWriteResult(
    TreasuryPaymentFactWriteOutcome Outcome,
    Guid EventId,
    DateTimeOffset ProcessedAtUtc,
    int AffectedEntries);

internal sealed class TreasuryPaymentFactWriteUnavailableException : Exception
{
    private const string SafeMessage =
        "The confirmed payment fact could not be stored consistently.";

    internal TreasuryPaymentFactWriteUnavailableException()
        : base(SafeMessage)
    {
    }

    internal TreasuryPaymentFactWriteUnavailableException(Exception innerException)
        : base(
            SafeMessage,
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}

internal sealed record StoredTreasuryPaymentFact(
    string SourceSystem,
    string ExternalId,
    Guid EventId,
    Guid ExpenseRequestId,
    Guid CurrencyId,
    decimal Amount,
    decimal AmountKzt,
    DateTimeOffset PaidAt,
    string? PaymentNumber,
    bool IsReversal,
    Guid? ReversedEventId,
    DateTimeOffset ProcessedAtUtc);

internal interface ITreasuryPaymentFactWriteRuntime
{
    bool HasSerializableTransaction { get; }

    bool HasPendingChanges { get; }

    Task AcquireExclusiveTransactionLockAsync(
        string resource,
        CancellationToken cancellationToken);

    Task<bool> ExpenseRequestUsesCurrencyAsync(
        Guid expenseRequestId,
        Guid currencyId,
        CancellationToken cancellationToken);

    Task<StoredTreasuryPaymentFact?> FindByStructuralIdentityAsync(
        string sourceSystem,
        string externalId,
        CancellationToken cancellationToken);

    Task<StoredTreasuryPaymentFact?> FindByEventIdAsync(
        Guid eventId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredTreasuryPaymentFact>> FindReversalsAsync(
        Guid reversedEventId,
        CancellationToken cancellationToken);

    Task<int> InsertAsync(
        StoredTreasuryPaymentFact paymentFact,
        CancellationToken cancellationToken);
}

/// <summary>
/// Dormant inner write stage for a confirmed event already resolved by a
/// future Gate G1 Inbox adapter. It neither opens nor commits a transaction;
/// the caller must include request projections, overpayment effects, audit and
/// command idempotency in the same outer Serializable transaction.
/// </summary>
internal static class TreasuryPaymentFactWriter
{
    internal static async Task<TreasuryPaymentFactWriteResult> WriteAsync(
        ConfirmedPaymentEvent paymentEvent,
        DateTimeOffset processedAtUtc,
        ITreasuryPaymentFactWriteRuntime runtime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paymentEvent);
        ArgumentNullException.ThrowIfNull(runtime);

        try
        {
            ConfirmedPaymentEventContract.Validate(paymentEvent);
            ConfirmedPaymentEventContract.ValidateProcessingTime(processedAtUtc);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);

            foreach (var resource in TreasuryPaymentFactLockKeys.Create(paymentEvent))
            {
                await runtime.AcquireExclusiveTransactionLockAsync(
                    resource,
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);

            var existingByIdentity = await runtime.FindByStructuralIdentityAsync(
                paymentEvent.SourceSystem,
                paymentEvent.ExternalId,
                cancellationToken);
            var existingByEventId = await runtime.FindByEventIdAsync(
                paymentEvent.EventId,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);
            var requestUsesCurrency = await runtime.ExpenseRequestUsesCurrencyAsync(
                paymentEvent.ExpenseRequestId,
                paymentEvent.CurrencyId,
                cancellationToken);
            if (!requestUsesCurrency)
            {
                throw new TreasuryPaymentFactWriteUnavailableException();
            }

            if (existingByIdentity is not null)
            {
                if (existingByEventId is null
                    || !HasSameStructuralIdentity(existingByIdentity, existingByEventId)
                    || !MatchesImmutableEvent(existingByIdentity, paymentEvent))
                {
                    throw new TreasuryPaymentFactWriteUnavailableException();
                }

                return new TreasuryPaymentFactWriteResult(
                    TreasuryPaymentFactWriteOutcome.AlreadyStored,
                    existingByIdentity.EventId,
                    existingByIdentity.ProcessedAtUtc,
                    0);
            }

            if (existingByEventId is not null)
            {
                throw new TreasuryPaymentFactWriteUnavailableException();
            }

            if (paymentEvent.IsReversal)
            {
                await ValidateReversalAsync(paymentEvent, runtime, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);
            var stored = ToStored(paymentEvent, processedAtUtc);
            var affectedEntries = await runtime.InsertAsync(stored, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (affectedEntries != 1 || runtime.HasPendingChanges)
            {
                throw new TreasuryPaymentFactWriteUnavailableException();
            }

            var persisted = await runtime.FindByStructuralIdentityAsync(
                paymentEvent.SourceSystem,
                paymentEvent.ExternalId,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!runtime.HasSerializableTransaction
                || runtime.HasPendingChanges
                || persisted is null
                || !MatchesStoredFact(persisted, stored))
            {
                throw new TreasuryPaymentFactWriteUnavailableException();
            }

            return new TreasuryPaymentFactWriteResult(
                TreasuryPaymentFactWriteOutcome.Stored,
                persisted.EventId,
                persisted.ProcessedAtUtc,
                affectedEntries);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TreasuryPaymentFactWriteUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TreasuryPaymentFactWriteUnavailableException(exception);
        }
    }

    private static async Task ValidateReversalAsync(
        ConfirmedPaymentEvent reversal,
        ITreasuryPaymentFactWriteRuntime runtime,
        CancellationToken cancellationToken)
    {
        if (reversal.ReversedEventId is not { } reversedEventId
            || reversedEventId == Guid.Empty)
        {
            throw new TreasuryPaymentFactWriteUnavailableException();
        }

        var original = await runtime.FindByEventIdAsync(reversedEventId, cancellationToken);
        if (original is null
            || original.IsReversal
            || original.ExpenseRequestId != reversal.ExpenseRequestId
            || original.CurrencyId != reversal.CurrencyId)
        {
            throw new TreasuryPaymentFactWriteUnavailableException();
        }

        var priorReversals = await runtime.FindReversalsAsync(
            reversedEventId,
            cancellationToken);
        if (priorReversals.Any(item =>
                !item.IsReversal
                || item.ReversedEventId != reversedEventId
                || item.ExpenseRequestId != original.ExpenseRequestId
                || item.CurrencyId != original.CurrencyId))
        {
            throw new TreasuryPaymentFactWriteUnavailableException();
        }

        var reversedAmount = SumRepresentable(
            priorReversals.Select(item => item.Amount),
            "Existing reversal amount");
        var reversedAmountKzt = SumRepresentable(
            priorReversals.Select(item => item.AmountKzt),
            "Existing reversal KZT amount");

        var remainingAmount = original.Amount - reversedAmount;
        var remainingAmountKzt = original.AmountKzt - reversedAmountKzt;
        if (remainingAmount <= 0m
            || remainingAmountKzt <= 0m
            || reversal.Amount > remainingAmount)
        {
            throw new TreasuryPaymentFactWriteUnavailableException();
        }

        var expectedReversalAmountKzt = PairedMoneyAllocationPolicy.Allocate(
            remainingAmountKzt,
            reversal.Amount,
            remainingAmount);
        if (reversal.AmountKzt != expectedReversalAmountKzt)
        {
            throw new TreasuryPaymentFactWriteUnavailableException();
        }
    }

    private static decimal SumRepresentable(
        IEnumerable<decimal> amounts,
        string fieldName)
    {
        var total = 0m;
        foreach (var amount in amounts)
        {
            try
            {
                total += amount;
            }
            catch (OverflowException)
            {
                throw new DomainRuleViolationException(
                    $"{fieldName} exceeds decimal storage range.");
            }

            MoneyAmountPolicy.EnsureNonNegative(total, fieldName);
        }

        return total;
    }

    private static StoredTreasuryPaymentFact ToStored(
        ConfirmedPaymentEvent paymentEvent,
        DateTimeOffset processedAtUtc) =>
        new(
            paymentEvent.SourceSystem,
            paymentEvent.ExternalId,
            paymentEvent.EventId,
            paymentEvent.ExpenseRequestId,
            paymentEvent.CurrencyId,
            paymentEvent.Amount,
            paymentEvent.AmountKzt,
            paymentEvent.PaidAt,
            paymentEvent.PaymentNumber,
            paymentEvent.IsReversal,
            paymentEvent.ReversedEventId,
            processedAtUtc);

    private static bool MatchesImmutableEvent(
        StoredTreasuryPaymentFact stored,
        ConfirmedPaymentEvent candidate) =>
        stored.SourceSystem == candidate.SourceSystem
        && stored.ExternalId == candidate.ExternalId
        && stored.EventId == candidate.EventId
        && stored.ExpenseRequestId == candidate.ExpenseRequestId
        && stored.CurrencyId == candidate.CurrencyId
        && stored.Amount == candidate.Amount
        && stored.AmountKzt == candidate.AmountKzt
        && stored.PaidAt == candidate.PaidAt
        && string.Equals(stored.PaymentNumber, candidate.PaymentNumber, StringComparison.Ordinal)
        && stored.IsReversal == candidate.IsReversal
        && stored.ReversedEventId == candidate.ReversedEventId;

    private static bool MatchesStoredFact(
        StoredTreasuryPaymentFact actual,
        StoredTreasuryPaymentFact expected) =>
        HasSameStructuralIdentity(actual, expected)
        && actual.EventId == expected.EventId
        && actual.ExpenseRequestId == expected.ExpenseRequestId
        && actual.CurrencyId == expected.CurrencyId
        && actual.Amount == expected.Amount
        && actual.AmountKzt == expected.AmountKzt
        && actual.PaidAt == expected.PaidAt
        && string.Equals(actual.PaymentNumber, expected.PaymentNumber, StringComparison.Ordinal)
        && actual.IsReversal == expected.IsReversal
        && actual.ReversedEventId == expected.ReversedEventId
        && actual.ProcessedAtUtc == expected.ProcessedAtUtc;

    private static bool HasSameStructuralIdentity(
        StoredTreasuryPaymentFact left,
        StoredTreasuryPaymentFact right) =>
        string.Equals(left.SourceSystem, right.SourceSystem, StringComparison.Ordinal)
        && string.Equals(left.ExternalId, right.ExternalId, StringComparison.Ordinal);

    private static void EnsureAttemptReady(ITreasuryPaymentFactWriteRuntime runtime)
    {
        if (!runtime.HasSerializableTransaction || runtime.HasPendingChanges)
        {
            throw new TreasuryPaymentFactWriteUnavailableException();
        }
    }
}
