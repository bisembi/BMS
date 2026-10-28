using Bms.Domain.Common;

namespace Bms.Domain.Payments;

public sealed record PaymentFactSourceBalance(
    Guid SourceEventId,
    DateTimeOffset PaidAt,
    decimal OriginalAmount,
    decimal OriginalAmountKzt,
    decimal ReversedAmount,
    decimal ReversedAmountKzt,
    decimal ActiveAmount,
    decimal ActiveAmountKzt,
    IReadOnlyList<Guid> ReversalEventIds);

public sealed record PaymentFactLedgerReconciliation(
    Guid ExpenseRequestId,
    Guid CurrencyId,
    decimal OriginalAmount,
    decimal OriginalAmountKzt,
    decimal ReversedAmount,
    decimal ReversedAmountKzt,
    decimal ActiveAmount,
    decimal ActiveAmountKzt,
    IReadOnlyList<PaymentFactSourceBalance> Sources);

/// <summary>
/// Reconciles immutable confirmed facts into paired active source balances.
/// This is an evidence calculation only: source order is stable for review,
/// but it does not approve payment-to-line FIFO, overpayment attribution,
/// workflow status, audit or any persistence operation.
/// </summary>
public static class PaymentFactLedgerReconciler
{
    public static PaymentFactLedgerReconciliation Reconcile(
        Guid expenseRequestId,
        Guid currencyId,
        IEnumerable<ConfirmedPaymentEvent> paymentEvents)
    {
        if (expenseRequestId == Guid.Empty || currencyId == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "Payment ledger reconciliation requires request and currency ids.");
        }
        ArgumentNullException.ThrowIfNull(paymentEvents);

        var events = paymentEvents.ToArray();
        ValidateEvents(expenseRequestId, currencyId, events);
        var byEventId = events.ToDictionary(item => item.EventId);
        var originals = events.Where(item => !item.IsReversal).ToArray();
        var reversalsByOriginal = BuildReversalGroups(events, byEventId);

        var sources = originals
            .Select(original => BuildSourceBalance(
                original,
                reversalsByOriginal.GetValueOrDefault(original.EventId) ?? []))
            .OrderBy(source => source.PaidAt.UtcDateTime)
            .ThenBy(
                source => source.SourceEventId.ToString("D"),
                StringComparer.Ordinal)
            .ToArray();

        var originalAmount = SumRepresentable(
            sources.Select(source => source.OriginalAmount),
            "Payment ledger original amount");
        var originalAmountKzt = SumRepresentable(
            sources.Select(source => source.OriginalAmountKzt),
            "Payment ledger original KZT amount");
        var reversedAmount = SumRepresentable(
            sources.Select(source => source.ReversedAmount),
            "Payment ledger reversed amount");
        var reversedAmountKzt = SumRepresentable(
            sources.Select(source => source.ReversedAmountKzt),
            "Payment ledger reversed KZT amount");
        var activeAmount = SumRepresentable(
            sources.Select(source => source.ActiveAmount),
            "Payment ledger active amount");
        var activeAmountKzt = SumRepresentable(
            sources.Select(source => source.ActiveAmountKzt),
            "Payment ledger active KZT amount");

        if (originalAmount - reversedAmount != activeAmount
            || originalAmountKzt - reversedAmountKzt != activeAmountKzt)
        {
            throw new InvalidOperationException(
                "Payment ledger currency and KZT totals do not reconcile.");
        }

        return new PaymentFactLedgerReconciliation(
            expenseRequestId,
            currencyId,
            originalAmount,
            originalAmountKzt,
            reversedAmount,
            reversedAmountKzt,
            activeAmount,
            activeAmountKzt,
            Array.AsReadOnly(sources));
    }

    private static Dictionary<Guid, ConfirmedPaymentEvent[]> BuildReversalGroups(
        IReadOnlyList<ConfirmedPaymentEvent> events,
        IReadOnlyDictionary<Guid, ConfirmedPaymentEvent> byEventId)
    {
        var reversals = new Dictionary<Guid, List<ConfirmedPaymentEvent>>();
        foreach (var reversal in events.Where(item => item.IsReversal))
        {
            if (reversal.ReversedEventId is not { } originalId
                || originalId == Guid.Empty)
            {
                throw new DomainRuleViolationException(
                    "A payment reversal must reference one original payment event in the ledger.");
            }

            if (!byEventId.TryGetValue(originalId, out var original)
                || original.IsReversal)
            {
                throw new DomainRuleViolationException(
                    "A payment reversal must reference one original payment event in the ledger.");
            }

            if (!reversals.TryGetValue(originalId, out var group))
            {
                group = [];
                reversals.Add(originalId, group);
            }
            group.Add(reversal);
        }

        return reversals.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray());
    }

    private static PaymentFactSourceBalance BuildSourceBalance(
        ConfirmedPaymentEvent original,
        IReadOnlyList<ConfirmedPaymentEvent> reversals)
    {
        var reversedAmount = SumRepresentable(
            reversals.Select(item => item.Amount),
            "Payment source reversed amount");
        var reversedAmountKzt = SumRepresentable(
            reversals.Select(item => item.AmountKzt),
            "Payment source reversed KZT amount");
        if (reversedAmount > original.Amount || reversedAmountKzt > original.AmountKzt)
        {
            throw new DomainRuleViolationException(
                "Cumulative payment reversals cannot exceed their original paired event.");
        }

        var activeAmount = original.Amount - reversedAmount;
        var activeAmountKzt = original.AmountKzt - reversedAmountKzt;
        if ((activeAmount == 0m) != (activeAmountKzt == 0m))
        {
            throw new DomainRuleViolationException(
                "A payment source currency and KZT balance must close together.");
        }

        var reversalEventIds = reversals
            .OrderBy(item => item.PaidAt.UtcDateTime)
            .ThenBy(item => item.EventId.ToString("D"), StringComparer.Ordinal)
            .Select(item => item.EventId)
            .ToArray();
        return new PaymentFactSourceBalance(
            original.EventId,
            original.PaidAt,
            original.Amount,
            original.AmountKzt,
            reversedAmount,
            reversedAmountKzt,
            activeAmount,
            activeAmountKzt,
            Array.AsReadOnly(reversalEventIds));
    }

    private static void ValidateEvents(
        Guid expenseRequestId,
        Guid currencyId,
        IReadOnlyList<ConfirmedPaymentEvent> events)
    {
        var eventIds = new HashSet<Guid>();
        var structuralIdentities = new HashSet<(string SourceSystem, string ExternalId)>();
        foreach (var paymentEvent in events)
        {
            if (paymentEvent is null)
            {
                throw new DomainRuleViolationException(
                    "Payment ledger event cannot be null.");
            }

            ConfirmedPaymentEventContract.Validate(paymentEvent);
            if (paymentEvent.ExpenseRequestId != expenseRequestId
                || paymentEvent.CurrencyId != currencyId)
            {
                throw new DomainRuleViolationException(
                    "Every payment ledger event must belong to the exact request and currency scope.");
            }
            if (!eventIds.Add(paymentEvent.EventId))
            {
                throw new DomainRuleViolationException(
                    "Payment ledger event ids must be unique.");
            }
            if (!structuralIdentities.Add((
                    paymentEvent.SourceSystem,
                    paymentEvent.ExternalId)))
            {
                throw new DomainRuleViolationException(
                    "Payment ledger structural identities must be unique and case-sensitive.");
            }
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
}
