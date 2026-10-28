using Bms.Domain.Common;

namespace Bms.Domain.Payments;

/// <summary>A separately approved NON-CASH waiver. Never a confirmed bank event.</summary>
public sealed record ApprovedPaymentRounding400(Guid ApprovalId, Guid ExpenseRequestId, Guid CurrencyId,
    Guid CashEventId, decimal Amount, DateTimeOffset EffectiveAtUtc, DateTimeOffset ApprovedAtUtc);

public static class ApprovedPaymentRoundingPolicy400
{
    public static decimal Validate(Guid requestId, Guid currencyId, decimal obligation,
        PaymentFactLedgerReconciliation ledger, IReadOnlyList<ApprovedPaymentRounding400> rounding)
    {
        ArgumentNullException.ThrowIfNull(rounding);
        if (rounding.Count == 0) return 0m;
        // This version deliberately supports only one explicit, sub-tenge KZT
        // decision per request. No ambient tolerance and no fuzzy matching.
        if (rounding.Count != 1) throw new DomainRuleViolationException("One explicit rounding decision required.");
        var row = rounding[0];
        MoneyAmountPolicy.EnsurePositive(row.Amount, "Approved rounding");
        ConfirmedPaymentEventContract.ValidateProcessingTime(row.ApprovedAtUtc);
        ConfirmedPaymentEventContract.ValidateProcessingTime(row.EffectiveAtUtc);
        var cash = ledger.Sources.SingleOrDefault(s => s.SourceEventId == row.CashEventId);
        if (row.ApprovalId == Guid.Empty || row.ExpenseRequestId != requestId || row.CurrencyId != currencyId
            || ledger.ExpenseRequestId != requestId || ledger.CurrencyId != currencyId
            || row.Amount >= 1m || row.EffectiveAtUtc > row.ApprovedAtUtc || cash is null
            || cash.ActiveAmount != cash.OriginalAmount || cash.ActiveAmountKzt != cash.OriginalAmountKzt
            || cash.PaidAt != row.EffectiveAtUtc || ledger.ActiveAmount != ledger.ActiveAmountKzt
            || ledger.ActiveAmount + row.Amount != obligation)
            throw new DomainRuleViolationException("Approved rounding is not bound to the exact active cash obligation.");
        return row.Amount;
    }
}

public sealed record ApprovedCashSlice400(Guid ExpenseRequestId, decimal ObligationAmount,
    decimal CashAmount, decimal RoundingAmount);

public static class ApprovedCashAllocationPolicy400
{
    public static void Validate(decimal sourceCash, IReadOnlyList<ApprovedCashSlice400> slices)
    {
        MoneyAmountPolicy.EnsurePositive(sourceCash, "Primary cash amount");
        ArgumentNullException.ThrowIfNull(slices);
        if (slices.Count is < 1 or > 100 || slices.Any(s => s.ExpenseRequestId == Guid.Empty)
            || slices.Select(s => s.ExpenseRequestId).Distinct().Count() != slices.Count)
            throw new DomainRuleViolationException("Distinct approved target requests required.");
        foreach (var slice in slices)
        {
            MoneyAmountPolicy.EnsurePositive(slice.ObligationAmount, "Request obligation");
            MoneyAmountPolicy.EnsurePositive(slice.CashAmount, "Allocated bank cash");
            MoneyAmountPolicy.EnsureNonNegative(slice.RoundingAmount, "Approved rounding");
            if (slice.RoundingAmount >= 1m || slice.CashAmount + slice.RoundingAmount != slice.ObligationAmount)
                throw new DomainRuleViolationException("Allocated cash and explicit rounding must exactly cover the request.");
        }
        if (slices.Sum(s => s.CashAmount) != sourceCash)
            throw new DomainRuleViolationException("Allocate the primary document exactly once, without any excess or remainder.");
    }
}
