using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Payments;

namespace Bms.Application.ExpenseRequests;

/// <summary>
/// Private application evidence for one overpayment slice. The source scope is
/// carried with the allocation so a missing or foreign source is refused
/// instead of silently disappearing from the settlement calculation.
/// </summary>
public sealed record ExpenseRequestAppliedOverpaymentSnapshot(
    Guid AllocationId,
    Guid TargetExpenseRequestId,
    Guid OrganizationId,
    Guid CounterpartyId,
    Guid CurrencyId,
    decimal Amount,
    decimal AmountKzt,
    string Status,
    DateTimeOffset ReservedAtUtc,
    DateTimeOffset? AppliedOrReleasedAtUtc);

/// <summary>
/// One authoritative read-only settlement projection shared by the request
/// card and the optional list columns. Reserved/released overpayment and cash
/// above the remaining obligation never count as settlement.
/// </summary>
public sealed record ExpenseRequestSettlementProjection(
    decimal ObligationAmount,
    decimal CashPaymentAmount,
    decimal AppliedOverpaymentAmount,
    decimal SettledAmount,
    decimal RemainingAmount,
    PaymentSettlementProgress Progress,
    bool HasConfirmedPaymentFacts,
    DateOnly? FullyPaidDate,
    ExpenseRequestSettlementKztProjection? ForeignCurrencyKzt);

/// <summary>
/// Exact KZT view required by UI contract 12.1 for a foreign-currency request.
/// The original obligation can be known while payment attribution remains
/// unavailable: current requirements do not define which bank event owns a
/// foreign-currency cash excess. In that case all three derived values stay
/// null instead of inventing an exchange-rate split.
/// </summary>
public sealed record ExpenseRequestSettlementKztProjection(
    decimal ObligationAmountKzt,
    decimal? PaidAmountKzt,
    decimal? BankPaymentAmountKzt,
    decimal? CurrencyDeviationKzt);

public static class ExpenseRequestSettlementProjector
{
    public static ExpenseRequestSettlementProjection Project(
        ExpenseRequest request,
        Guid currencyId,
        IReadOnlyList<ConfirmedPaymentEvent> events,
        IReadOnlyList<ExpenseRequestAppliedOverpaymentSnapshot> allocations,
        IReadOnlyList<ApprovedPaymentRounding400>? approvedRounding = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(allocations);
        if (currencyId == Guid.Empty)
        {
            throw new ExpenseRequestSettlementProjectionException();
        }

        var obligation = request.TotalAmount;
        MoneyAmountPolicy.EnsureNonNegative(obligation, "Expense-request obligation");
        var ledger = PaymentFactLedgerReconciler.Reconcile(request.Id, currencyId, events);
        ValidateAllocations(request, currencyId, allocations);
        var applied = allocations.Where(item => item.Status == "Applied").ToArray();
        var appliedAmount = applied.Sum(item => item.Amount);
        if (approvedRounding is { Count: > 0 } && (request.CurrencyCode != "KZT" || appliedAmount != 0))
            throw new ExpenseRequestSettlementProjectionException();
        var roundingAmount = ApprovedPaymentRoundingPolicy400.Validate(request.Id, currencyId,
            obligation, ledger, approvedRounding ?? []);
        if (appliedAmount > obligation)
        {
            throw new ExpenseRequestSettlementProjectionException();
        }

        // UI contract 12.1: confirmed cash PLUS applied overpayment. Cash above
        // the part still owed belongs to an overpayment source, not to this
        // request's settlement.
        var cashAmount = decimal.Min(obligation - appliedAmount - roundingAmount, ledger.ActiveAmount);
        var settled = checked(appliedAmount + cashAmount + roundingAmount);
        var remaining = obligation - settled;
        var progress = settled switch
        {
            0m => PaymentSettlementProgress.Unpaid,
            _ when settled == obligation => PaymentSettlementProgress.FullySettled,
            _ => PaymentSettlementProgress.PartiallySettled,
        };

        return new ExpenseRequestSettlementProjection(
            obligation,
            cashAmount,
            appliedAmount,
            settled,
            remaining,
            progress,
            ledger.Sources.Count > 0,
            FirstFullyPaidDate(request, events, applied, obligation, approvedRounding ?? []),
            ForeignCurrencyKzt(request, ledger, applied, cashAmount, settled));
    }

    private static ExpenseRequestSettlementKztProjection? ForeignCurrencyKzt(
        ExpenseRequest request,
        PaymentFactLedgerReconciliation ledger,
        IReadOnlyList<ExpenseRequestAppliedOverpaymentSnapshot> applied,
        decimal cashAmount,
        decimal settledAmount)
    {
        if (request.CurrencyCode == "KZT")
        {
            return null;
        }

        if (request.TotalAmountKzt is not { } obligationAmountKzt)
        {
            // A legacy or rate-unavailable foreign request can still have an
            // exact settlement in its own currency. Preserve that proven
            // result for the shared list/card projection, while publishing no
            // KZT value at all instead of revaluing it with a current rate.
            return null;
        }

        MoneyAmountPolicy.EnsurePositive(
            obligationAmountKzt,
            "Expense-request KZT obligation");

        // Whole-ledger KZT is exact while every active cash unit still belongs
        // to the obligation. If foreign cash exceeds the remaining obligation,
        // the approved documents do not define the business order that splits
        // bank KZT between this request and its overpayment source. Preserve the
        // known original equivalent but refuse to manufacture the other values.
        if (cashAmount != ledger.ActiveAmount)
        {
            return new ExpenseRequestSettlementKztProjection(
                obligationAmountKzt,
                PaidAmountKzt: null,
                BankPaymentAmountKzt: null,
                CurrencyDeviationKzt: null);
        }

        decimal appliedAmountKzt;
        try
        {
            appliedAmountKzt = applied.Sum(item => item.AmountKzt);
        }
        catch (OverflowException)
        {
            throw new ExpenseRequestSettlementProjectionException();
        }

        decimal paidAmountKzt;
        try
        {
            paidAmountKzt = checked(ledger.ActiveAmountKzt + appliedAmountKzt);
        }
        catch (OverflowException)
        {
            throw new ExpenseRequestSettlementProjectionException();
        }

        var lines = request.Lines
            .Select(line => line.AmountKzt is { } amountKzt
                ? new PaymentSettlementLineSnapshot(
                    line.Id,
                    line.SortOrder,
                    line.Amount,
                    amountKzt)
                : throw new ExpenseRequestSettlementProjectionException())
            .ToArray();
        var calculation = PaymentSettlementProgressCalculator.Calculate(
            request.CurrencyCode,
            lines,
            settledAmount,
            paidAmountKzt);
        if (calculation.ObligationAmountInRequestCurrency != request.TotalAmount ||
            calculation.OriginalReserveKzt != obligationAmountKzt)
        {
            throw new ExpenseRequestSettlementProjectionException();
        }

        return new ExpenseRequestSettlementKztProjection(
            obligationAmountKzt,
            paidAmountKzt,
            ledger.ActiveAmountKzt,
            calculation.CurrencyDeviationKzt);
    }

    private static void ValidateAllocations(
        ExpenseRequest request,
        Guid currencyId,
        IReadOnlyList<ExpenseRequestAppliedOverpaymentSnapshot> allocations)
    {
        var ids = new HashSet<Guid>();
        foreach (var item in allocations)
        {
            if (item is null || item.AllocationId == Guid.Empty || !ids.Add(item.AllocationId) ||
                item.TargetExpenseRequestId != request.Id || item.OrganizationId == Guid.Empty ||
                item.OrganizationId != request.OrganizationId || item.CounterpartyId == Guid.Empty ||
                item.CounterpartyId != request.CounterpartyId || item.CurrencyId != currencyId ||
                item.Status is not ("Reserved" or "Applied" or "Released") ||
                (item.Status == "Reserved") != (item.AppliedOrReleasedAtUtc is null))
            {
                throw new ExpenseRequestSettlementProjectionException();
            }

            MoneyAmountPolicy.EnsurePositive(item.Amount, "Allocated amount");
            MoneyAmountPolicy.EnsurePositive(item.AmountKzt, "Allocated KZT amount");
            ConfirmedPaymentEventContract.ValidateProcessingTime(item.ReservedAtUtc);
            if (item.AppliedOrReleasedAtUtc is { } changed)
            {
                ConfirmedPaymentEventContract.ValidateProcessingTime(changed);
                if (changed < item.ReservedAtUtc)
                {
                    throw new ExpenseRequestSettlementProjectionException();
                }
            }
            if (request.CurrencyCode == "KZT" && item.Amount != item.AmountKzt)
            {
                throw new ExpenseRequestSettlementProjectionException();
            }
        }
    }

    /// <summary>
    /// Preserves the stored first payment date across reversal/reopen. Before
    /// that snapshot exists, follows the same atomic UTC instant groups used
    /// by the closing evidence resolver. Event GUIDs are not chronology.
    /// </summary>
    private static DateOnly? FirstFullyPaidDate(
        ExpenseRequest request,
        IReadOnlyList<ConfirmedPaymentEvent> events,
        IReadOnlyList<ExpenseRequestAppliedOverpaymentSnapshot> applied,
        decimal obligation,
        IReadOnlyList<ApprovedPaymentRounding400> rounding)
    {
        if (request.FullyPaidAtUtc is { } stored)
        {
            if (stored == default || stored.Offset != TimeSpan.Zero ||
                stored.Ticks % TimeSpan.TicksPerMillisecond != 0)
            {
                throw new ExpenseRequestSettlementProjectionException();
            }
            // The API exposes a business date, not the UTC storage day.
            // A 1C payment at 00:00 UTC+5 belongs to that same local day.
            return DateOnly.FromDateTime(stored.ToOffset(TimeSpan.FromHours(5)).DateTime);
        }
        if (obligation == 0m)
        {
            return null;
        }

        var running = 0m;
        DateOnly? first = null;
        var movements = events.Select(item => (
                At: item.PaidAt.UtcDateTime,
                Id: item.EventId,
                Amount: item.IsReversal ? -item.Amount : item.Amount))
            .Concat(applied.Select(item => (
                At: item.AppliedOrReleasedAtUtc.GetValueOrDefault().UtcDateTime,
                Id: item.AllocationId,
                Amount: item.Amount)))
            .Concat(rounding.Select(item => (At: item.EffectiveAtUtc.UtcDateTime,
                Id: item.ApprovalId, Amount: item.Amount)));
        foreach (var instant in movements.OrderBy(item => item.At).GroupBy(item => item.At))
        {
            // Source corrections retain the original bank timestamp. Their
            // original/reversal/replacement must be applied together, or a
            // GUID ordering can invent a negative balance or an earlier full
            // payment date that never existed in the corrected ledger.
            running = checked(running + instant.Sum(item => item.Amount));
            if (running < 0m)
            {
                throw new ExpenseRequestSettlementProjectionException();
            }
            if (first is null && running >= obligation)
            {
                first = DateOnly.FromDateTime(instant.Key.AddHours(5));
            }
        }
        return first;
    }
}

public sealed class ExpenseRequestSettlementProjectionException : Exception
{
    public ExpenseRequestSettlementProjectionException()
        : base("Expense-request settlement evidence is inconsistent.")
    {
    }
}
