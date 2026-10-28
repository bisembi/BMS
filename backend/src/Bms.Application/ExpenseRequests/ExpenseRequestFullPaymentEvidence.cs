using Bms.Domain.Common;
using Bms.Domain.Payments;

namespace Bms.Application.ExpenseRequests;

internal sealed record ExpenseRequestFullPaymentEvidence(
    DateTimeOffset FirstFullyPaidAtUtc,
    PaymentFactLedgerReconciliation Ledger);

/// <summary>
/// Proves that the current confirmed ledger still fully covers the request and
/// derives the first historical instant when the signed payment stream reached
/// that obligation. A stored first-payment instant is immutable, but current
/// full coverage is rechecked for every closing command.
/// </summary>
public static class ExpenseRequestFullPaymentEvidenceResolver
{
    /// <summary>
    /// Returns the exact first instant at which the confirmed signed ledger
    /// covered the complete request obligation. Read-only projections use the
    /// same proof as closing commands instead of substituting processing time
    /// or the earliest individual payment.
    /// </summary>
    public static DateTimeOffset ResolveFirstFullyPaidAtUtc(
        Guid expenseRequestId,
        Guid currencyId,
        decimal obligation,
        IReadOnlyList<ConfirmedPaymentEvent> events,
        DateTimeOffset? storedFullyPaidAtUtc,
        IReadOnlyList<ApprovedPaymentRounding400>? approvedRounding = null) =>
        Resolve(
            expenseRequestId,
            currencyId,
            obligation,
            events,
            storedFullyPaidAtUtc, approvedRounding).FirstFullyPaidAtUtc;

    internal static ExpenseRequestFullPaymentEvidence Resolve(
        Guid expenseRequestId,
        Guid currencyId,
        decimal obligation,
        IReadOnlyList<ConfirmedPaymentEvent> events,
        DateTimeOffset? storedFullyPaidAtUtc,
        IReadOnlyList<ApprovedPaymentRounding400>? approvedRounding = null)
    {
        MoneyAmountPolicy.EnsurePositive(obligation, "Expense-request obligation");
        ArgumentNullException.ThrowIfNull(events);

        var ledger = PaymentFactLedgerReconciler.Reconcile(
            expenseRequestId,
            currencyId,
            events);
        var rounding = approvedRounding ?? [];
        var waived = ApprovedPaymentRoundingPolicy400.Validate(expenseRequestId, currencyId, obligation, ledger, rounding);
        if (ledger.ActiveAmount + waived < obligation)
        {
            throw new ExpenseRequestClosingBlockedException();
        }

        if (storedFullyPaidAtUtc is { } stored)
        {
            EnsureUtcStorageTime(stored);
            return new ExpenseRequestFullPaymentEvidence(stored, ledger);
        }

        var running = 0m;
        DateTimeOffset? firstFullyPaidAtUtc = null;
        // Bank corrections retain the original PaidAt. At the same stored
        // instant, an event GUID is identity, not chronology: apply the signed
        // group atomically, after the ledger has validated every reversal pair.
        foreach (var instant in events
                     .OrderBy(item => item.PaidAt.UtcDateTime)
                     .GroupBy(item => item.PaidAt.UtcDateTime))
        {
            try
            {
                var credited = instant.Where(item => !item.IsReversal).Sum(item => item.Amount);
                var reversed = instant.Where(item => item.IsReversal).Sum(item => item.Amount);
                var approved = rounding.Where(r => r.EffectiveAtUtc.UtcDateTime == instant.Key).Sum(r => r.Amount);
                running = checked(running + (credited - reversed) + approved);
            }
            catch (OverflowException)
            {
                throw new DomainRuleViolationException(
                    "Payment history exceeds decimal storage range.");
            }

            if (running < 0m)
            {
                throw new DomainRuleViolationException(
                    "Payment history cannot have a negative chronological balance.");
            }
            if (firstFullyPaidAtUtc is null && running >= obligation)
            {
                firstFullyPaidAtUtc = instant.First().PaidAt.ToUniversalTime();
            }
        }

        if (firstFullyPaidAtUtc is not { } first)
        {
            throw new ExpenseRequestClosingBlockedException();
        }

        EnsureUtcStorageTime(first);
        return new ExpenseRequestFullPaymentEvidence(first, ledger);
    }

    private static void EnsureUtcStorageTime(DateTimeOffset value)
    {
        if (value == default ||
            value.Offset != TimeSpan.Zero ||
            value.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            throw new DomainRuleViolationException(
                "Full-payment evidence must be a UTC datetimeoffset(3) value.");
        }
    }
}
