using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Domain.Payments;

/// <summary>
/// The persisted status a payment application plan targets. Codes stay in the
/// workflow registry; this enum only narrows the payment-driven subset.
/// </summary>
public enum KztPaymentTargetStatus
{
    ToPay = 1,
    PartiallyPaid = 2,
    Paid = 3,
}

/// <summary>
/// One expense-request line of the plan: how much of the obligation is now
/// settled, how much reserve leaves the budget and how much stays reserved.
/// All amounts are absolute (net replacement), never deltas from a prior run.
/// </summary>
public sealed record KztPaymentLineApplication(
    Guid LineId,
    int SortOrder,
    decimal ObligationAmountKzt,
    decimal SettledAmountKzt,
    decimal RemainingReserveKzt);

/// <summary>
/// The full application plan for one KZT expense request against its full
/// reconciled confirmed-payment ledger. The plan is pure evidence: it opens no
/// transaction, performs no persistence and never consults a current exchange
/// rate. Excess cash is reported but deliberately not attributed to any
/// overpayment scope here.
/// </summary>
public sealed record KztPaymentApplicationPlan(
    Guid ExpenseRequestId,
    KztPaymentTargetStatus TargetStatus,
    PaymentSettlementProgress Progress,
    decimal ObligationAmountKzt,
    decimal ActiveLedgerAmountKzt,
    decimal CashAppliedKzt,
    decimal ExcessAmountKzt,
    decimal ReleasedReserveKzt,
    decimal RemainingReserveKzt,
    decimal ReservedDeltaKzt,
    decimal PaidDeltaKzt,
    bool HasConfirmedPaymentFacts,
    IReadOnlyList<KztPaymentLineApplication> Lines);

/// <summary>
/// Builds the factual application of a reconciled KZT payment ledger to one
/// expense request. Always computed from the complete ledger and the immutable
/// line snapshots — never from a previous plan's delta — so a reversal simply
/// yields a smaller absolute state (rule: recalculate, do not append a
/// negative payment). Fail-closed for every non-KZT currency: no approved rule
/// splits banked KZT between obligation and overpayment for foreign requests.
/// </summary>
public static class KztPaymentApplicationPlanner
{
    public static KztPaymentApplicationPlan Plan(
        Guid expenseRequestId,
        ExpenseRequestStatus currentStatus,
        string currencyCode,
        IReadOnlyList<PaymentSettlementLineSnapshot> lineSnapshots,
        PaymentFactLedgerReconciliation ledger)
    {
        if (expenseRequestId == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "A KZT payment application plan requires a nonempty request id.");
        }
        ArgumentNullException.ThrowIfNull(lineSnapshots);
        ArgumentNullException.ThrowIfNull(ledger);

        if (!string.Equals(currencyCode, "KZT", StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException(
                "Payment application is activated only for KZT requests.");
        }
        if (currentStatus is not (ExpenseRequestStatus.ToPay
            or ExpenseRequestStatus.PartiallyPaid
            or ExpenseRequestStatus.Paid))
        {
            throw new DomainRuleViolationException(
                "Payment application accepts only TO_PAY, PARTIALLY_PAID or PAID requests.");
        }
        if (ledger.ExpenseRequestId != expenseRequestId)
        {
            throw new DomainRuleViolationException(
                "The payment ledger must belong to the exact expense request.");
        }
        if (ledger.ActiveAmount != ledger.ActiveAmountKzt)
        {
            throw new DomainRuleViolationException(
                "A KZT ledger requires equal request-currency and KZT active balances.");
        }

        MoneyAmountPolicy.EnsureNonNegative(
            ledger.ActiveAmountKzt,
            "Active ledger KZT amount");

        // The obligation is validated and summed by the settlement calculator;
        // run it first with zero settlement to reuse its line validation, then
        // with the attributed amount. Attribution is the single business rule
        // this planner adds: cash applied = min(active ledger, obligation).
        var probe = PaymentSettlementProgressCalculator.Calculate(
            currencyCode,
            lineSnapshots,
            settledAmountInRequestCurrency: 0m,
            actualSettledAmountKzt: 0m);
        var cashApplied = decimal.Min(
            ledger.ActiveAmountKzt,
            probe.ObligationAmountInRequestCurrency);
        var excess = ledger.ActiveAmountKzt - cashApplied;
        MoneyAmountPolicy.EnsureNonNegative(excess, "Excess KZT amount");

        var settlement = PaymentSettlementProgressCalculator.Calculate(
            currencyCode,
            lineSnapshots,
            cashApplied,
            cashApplied);

        var targetStatus = settlement.Progress switch
        {
            PaymentSettlementProgress.Unpaid => KztPaymentTargetStatus.ToPay,
            PaymentSettlementProgress.PartiallySettled => KztPaymentTargetStatus.PartiallyPaid,
            PaymentSettlementProgress.FullySettled => KztPaymentTargetStatus.Paid,
            _ => throw new DomainRuleViolationException(
                "Payment settlement progress is not defined."),
        };

        var lines = settlement.Lines
            .Select(line => new KztPaymentLineApplication(
                line.LineId,
                line.SortOrder,
                line.ObligationAmountInRequestCurrency,
                line.SettledAmountInRequestCurrency,
                line.RemainingReserveKzt))
            .ToArray();

        // Net absolute targets against the original reserve: the writer
        // replaces stored values with these, so replay and reversal are both
        // idempotent by construction.
        var reservedDelta = settlement.RemainingReserveKzt - settlement.OriginalReserveKzt;
        var paidDelta = settlement.ReleasedReserveKzt;

        return new KztPaymentApplicationPlan(
            expenseRequestId,
            targetStatus,
            settlement.Progress,
            settlement.ObligationAmountInRequestCurrency,
            ledger.ActiveAmountKzt,
            cashApplied,
            excess,
            settlement.ReleasedReserveKzt,
            settlement.RemainingReserveKzt,
            reservedDelta,
            paidDelta,
            HasConfirmedPaymentFacts: ledger.Sources.Count > 0,
            Array.AsReadOnly(lines));
    }
}
