using Bms.Domain.Common;

namespace Bms.Domain.Overpayments;

/// <summary>
/// Factual calculation over values already read under the caller's lock.
/// This type does not reserve sources, persist statuses or approve an OPEN
/// payment-event transport contract.
/// </summary>
public sealed record OverpaymentApplicationThresholdDecision(
    decimal ObligationAmount,
    decimal CashSettledAmount,
    decimal ActiveReservedAmount,
    bool ThresholdReached,
    decimal AppliedAmount,
    decimal ReleasedAmount,
    decimal RetainedReservedAmount,
    decimal SettledAmountAfterDecision,
    decimal RemainingObligationAmount);

public static class OverpaymentApplicationThresholdCalculator
{
    public static OverpaymentApplicationThresholdDecision Calculate(
        decimal obligationAmount,
        decimal cashSettledAmount,
        decimal activeReservedAmount)
    {
        MoneyAmountPolicy.EnsurePositive(obligationAmount, "Overpayment target obligation");
        MoneyAmountPolicy.EnsureNonNegative(cashSettledAmount, "Cash-settled amount");
        MoneyAmountPolicy.EnsureNonNegative(activeReservedAmount, "Active reserved overpayment");

        if (cashSettledAmount > obligationAmount)
        {
            throw new DomainRuleViolationException(
                "Cash settlement attributed to the obligation cannot exceed the obligation amount.");
        }

        var remainingBeforeApplication = obligationAmount - cashSettledAmount;
        var thresholdReached = activeReservedAmount >= remainingBeforeApplication;
        if (!thresholdReached)
        {
            return new OverpaymentApplicationThresholdDecision(
                obligationAmount,
                cashSettledAmount,
                activeReservedAmount,
                false,
                0m,
                0m,
                activeReservedAmount,
                cashSettledAmount,
                remainingBeforeApplication);
        }

        var appliedAmount = decimal.Min(activeReservedAmount, remainingBeforeApplication);
        var releasedAmount = activeReservedAmount - appliedAmount;
        var settledAmountAfterDecision = cashSettledAmount + appliedAmount;
        var remainingObligationAmount = obligationAmount - settledAmountAfterDecision;

        return new OverpaymentApplicationThresholdDecision(
            obligationAmount,
            cashSettledAmount,
            activeReservedAmount,
            true,
            appliedAmount,
            releasedAmount,
            0m,
            settledAmountAfterDecision,
            remainingObligationAmount);
    }
}
