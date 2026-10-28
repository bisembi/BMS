using Bms.Domain.Common;

namespace Bms.Domain.Overpayments;

public sealed record ActiveOverpaymentReservationSnapshot(
    Guid AllocationId,
    Guid TargetRequestId,
    DateTimeOffset ReservedAtUtc,
    decimal Amount,
    decimal AmountKzt,
    bool HasPaymentOrder);

public sealed record AppliedOverpaymentAllocationSnapshot(
    Guid AllocationId,
    Guid TargetRequestId,
    DateTimeOffset AppliedAtUtc,
    decimal Amount,
    decimal AmountKzt);

public sealed record OverpaymentReservationRelease(
    Guid AllocationId,
    Guid TargetRequestId,
    DateTimeOffset ReservedAtUtc,
    decimal AmountBefore,
    decimal AmountKztBefore,
    decimal ReleasedAmount,
    decimal ReleasedAmountKzt,
    decimal AmountAfter,
    decimal AmountKztAfter,
    bool RequiresPaymentOrderReview);

public sealed record AppliedOverpaymentDeficitSlice(
    Guid AllocationId,
    Guid TargetRequestId,
    DateTimeOffset AppliedAtUtc,
    decimal AmountBefore,
    decimal AmountKztBefore,
    decimal DeficitAmount,
    decimal DeficitAmountKzt,
    decimal AmountAfter,
    decimal AmountKztAfter);

public sealed record OverpaymentSourceReversalPlan(
    Guid SourceId,
    string CurrencyCode,
    decimal ReversalAmount,
    decimal ReversalAmountKzt,
    decimal FreeReductionAmount,
    decimal FreeReductionAmountKzt,
    decimal ReleasedReservationAmount,
    decimal ReleasedReservationAmountKzt,
    decimal DeficitAmount,
    decimal DeficitAmountKzt,
    IReadOnlyList<OverpaymentReservationRelease> ReservationReleases,
    IReadOnlyList<AppliedOverpaymentDeficitSlice> AppliedDeficitSlices);

/// <summary>
/// Rebuilds the ordered effect of one authoritative source reversal over
/// snapshots already read under the caller's managed lock. The result is a
/// write plan only: it does not mutate requests, statuses, payment orders,
/// deficits or audit records.
/// </summary>
public static class OverpaymentSourceReversalPlanner
{
    public static OverpaymentSourceReversalPlan Plan(
        Guid sourceId,
        string currencyCode,
        decimal reversalAmount,
        decimal reversalAmountKzt,
        decimal freeAmount,
        decimal freeAmountKzt,
        IEnumerable<ActiveOverpaymentReservationSnapshot> activeReservations,
        IEnumerable<AppliedOverpaymentAllocationSnapshot> appliedAllocations)
    {
        ValidateHeader(
            sourceId,
            currencyCode,
            reversalAmount,
            reversalAmountKzt,
            freeAmount,
            freeAmountKzt);
        ArgumentNullException.ThrowIfNull(activeReservations);
        ArgumentNullException.ThrowIfNull(appliedAllocations);

        var active = activeReservations.ToArray();
        var applied = appliedAllocations.ToArray();
        ValidateAllocations(currencyCode, active, applied);
        ValidateSourceTotals(freeAmount, freeAmountKzt, active, applied);

        var remainingReversal = reversalAmount;
        var calculatedReversalKzt = 0m;

        var freeReduction = decimal.Min(freeAmount, remainingReversal);
        var freeReductionKzt = AllocatePair(freeAmountKzt, freeReduction, freeAmount);
        remainingReversal -= freeReduction;
        calculatedReversalKzt = AddRepresentable(
            calculatedReversalKzt,
            freeReductionKzt,
            "Calculated reversal KZT total");

        var reservationReleases = new List<OverpaymentReservationRelease>();
        foreach (var reservation in active
                     .OrderByDescending(item => item.ReservedAtUtc)
                     .ThenByDescending(
                         item => item.AllocationId.ToString("D"),
                         StringComparer.Ordinal))
        {
            if (remainingReversal == 0m)
            {
                break;
            }

            var releasedAmount = decimal.Min(reservation.Amount, remainingReversal);
            var releasedAmountKzt = PairedMoneyAllocationPolicy.Allocate(
                reservation.AmountKzt,
                releasedAmount,
                reservation.Amount);
            reservationReleases.Add(new OverpaymentReservationRelease(
                reservation.AllocationId,
                reservation.TargetRequestId,
                reservation.ReservedAtUtc,
                reservation.Amount,
                reservation.AmountKzt,
                releasedAmount,
                releasedAmountKzt,
                reservation.Amount - releasedAmount,
                reservation.AmountKzt - releasedAmountKzt,
                reservation.HasPaymentOrder));
            remainingReversal -= releasedAmount;
            calculatedReversalKzt = AddRepresentable(
                calculatedReversalKzt,
                releasedAmountKzt,
                "Calculated reversal KZT total");
        }

        var deficitSlices = new List<AppliedOverpaymentDeficitSlice>();
        foreach (var allocation in applied
                     .OrderByDescending(item => item.AppliedAtUtc)
                     .ThenByDescending(
                         item => item.AllocationId.ToString("D"),
                         StringComparer.Ordinal))
        {
            if (remainingReversal == 0m)
            {
                break;
            }

            var deficitAmount = decimal.Min(allocation.Amount, remainingReversal);
            var deficitAmountKzt = PairedMoneyAllocationPolicy.Allocate(
                allocation.AmountKzt,
                deficitAmount,
                allocation.Amount);
            deficitSlices.Add(new AppliedOverpaymentDeficitSlice(
                allocation.AllocationId,
                allocation.TargetRequestId,
                allocation.AppliedAtUtc,
                allocation.Amount,
                allocation.AmountKzt,
                deficitAmount,
                deficitAmountKzt,
                allocation.Amount - deficitAmount,
                allocation.AmountKzt - deficitAmountKzt));
            remainingReversal -= deficitAmount;
            calculatedReversalKzt = AddRepresentable(
                calculatedReversalKzt,
                deficitAmountKzt,
                "Calculated reversal KZT total");
        }

        if (remainingReversal != 0m)
        {
            throw new DomainRuleViolationException(
                "Source reversal exceeds the free, reserved and applied source balance.");
        }
        if (calculatedReversalKzt != reversalAmountKzt)
        {
            throw new DomainRuleViolationException(
                "Source reversal KZT pair does not reconcile with ordered source balances.");
        }

        var releasedReservationAmount = SumRepresentable(
            reservationReleases.Select(item => item.ReleasedAmount),
            "Released reservation total");
        var releasedReservationAmountKzt = SumRepresentable(
            reservationReleases.Select(item => item.ReleasedAmountKzt),
            "Released reservation KZT total");
        var deficitAmountTotal = SumRepresentable(
            deficitSlices.Select(item => item.DeficitAmount),
            "Reversal deficit total");
        var deficitAmountKztTotal = SumRepresentable(
            deficitSlices.Select(item => item.DeficitAmountKzt),
            "Reversal deficit KZT total");

        return new OverpaymentSourceReversalPlan(
            sourceId,
            currencyCode,
            reversalAmount,
            reversalAmountKzt,
            freeReduction,
            freeReductionKzt,
            releasedReservationAmount,
            releasedReservationAmountKzt,
            deficitAmountTotal,
            deficitAmountKztTotal,
            Array.AsReadOnly(reservationReleases.ToArray()),
            Array.AsReadOnly(deficitSlices.ToArray()));
    }

    private static decimal AllocatePair(
        decimal pairedAmount,
        decimal allocatedAmount,
        decimal availableAmount)
    {
        if (allocatedAmount == 0m)
        {
            return 0m;
        }

        return PairedMoneyAllocationPolicy.Allocate(
            pairedAmount,
            allocatedAmount,
            availableAmount);
    }

    private static void ValidateHeader(
        Guid sourceId,
        string currencyCode,
        decimal reversalAmount,
        decimal reversalAmountKzt,
        decimal freeAmount,
        decimal freeAmountKzt)
    {
        if (sourceId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Overpayment reversal source id is required.");
        }
        ValidateCurrencyCode(currencyCode);
        MoneyAmountPolicy.EnsurePositive(reversalAmount, "Source reversal amount");
        MoneyAmountPolicy.EnsurePositive(reversalAmountKzt, "Source reversal KZT amount");
        MoneyAmountPolicy.EnsureNonNegative(freeAmount, "Free overpayment amount");
        MoneyAmountPolicy.EnsureNonNegative(freeAmountKzt, "Free overpayment KZT amount");
        if ((freeAmount == 0m) != (freeAmountKzt == 0m))
        {
            throw new DomainRuleViolationException(
                "Free overpayment currency and KZT balances must both be zero or both be positive.");
        }
        if (string.Equals(currencyCode, "KZT", StringComparison.Ordinal)
            && (reversalAmount != reversalAmountKzt || freeAmount != freeAmountKzt))
        {
            throw new DomainRuleViolationException(
                "A KZT overpayment reversal requires equal currency and KZT pairs.");
        }
    }

    private static void ValidateAllocations(
        string currencyCode,
        IReadOnlyList<ActiveOverpaymentReservationSnapshot> active,
        IReadOnlyList<AppliedOverpaymentAllocationSnapshot> applied)
    {
        var allocationIds = new HashSet<Guid>();
        foreach (var reservation in active)
        {
            if (reservation is null)
            {
                throw new DomainRuleViolationException(
                    "Active overpayment reservation snapshot cannot be null.");
            }
            ValidateAllocation(
                currencyCode,
                reservation.AllocationId,
                reservation.TargetRequestId,
                reservation.ReservedAtUtc,
                reservation.Amount,
                reservation.AmountKzt,
                allocationIds);
        }
        foreach (var allocation in applied)
        {
            if (allocation is null)
            {
                throw new DomainRuleViolationException(
                    "Applied overpayment allocation snapshot cannot be null.");
            }
            ValidateAllocation(
                currencyCode,
                allocation.AllocationId,
                allocation.TargetRequestId,
                allocation.AppliedAtUtc,
                allocation.Amount,
                allocation.AmountKzt,
                allocationIds);
        }
    }

    private static void ValidateAllocation(
        string currencyCode,
        Guid allocationId,
        Guid targetRequestId,
        DateTimeOffset occurredAtUtc,
        decimal amount,
        decimal amountKzt,
        ISet<Guid> allocationIds)
    {
        if (allocationId == Guid.Empty || !allocationIds.Add(allocationId))
        {
            throw new DomainRuleViolationException(
                "Overpayment allocation ids must be nonempty and unique across states.");
        }
        if (targetRequestId == Guid.Empty || occurredAtUtc == default)
        {
            throw new DomainRuleViolationException(
                "Overpayment allocation requires target request and occurrence time.");
        }
        MoneyAmountPolicy.EnsurePositive(amount, "Overpayment allocation amount");
        MoneyAmountPolicy.EnsurePositive(amountKzt, "Overpayment allocation KZT amount");
        if (string.Equals(currencyCode, "KZT", StringComparison.Ordinal)
            && amount != amountKzt)
        {
            throw new DomainRuleViolationException(
                "A KZT overpayment allocation requires equal currency and KZT amounts.");
        }
    }

    private static void ValidateSourceTotals(
        decimal freeAmount,
        decimal freeAmountKzt,
        IReadOnlyCollection<ActiveOverpaymentReservationSnapshot> active,
        IReadOnlyCollection<AppliedOverpaymentAllocationSnapshot> applied)
    {
        _ = SumRepresentable(
            new[] { freeAmount }
                .Concat(active.Select(item => item.Amount))
                .Concat(applied.Select(item => item.Amount)),
            "Reducible source currency total");
        _ = SumRepresentable(
            new[] { freeAmountKzt }
                .Concat(active.Select(item => item.AmountKzt))
                .Concat(applied.Select(item => item.AmountKzt)),
            "Reducible source KZT total");
    }

    private static decimal SumRepresentable(IEnumerable<decimal> values, string fieldName)
    {
        var total = 0m;
        foreach (var value in values)
        {
            total = AddRepresentable(total, value, fieldName);
        }
        return total;
    }

    private static decimal AddRepresentable(decimal current, decimal addition, string fieldName)
    {
        decimal result;
        try
        {
            result = current + addition;
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException($"{fieldName} exceeds decimal storage range.");
        }

        MoneyAmountPolicy.EnsureNonNegative(result, fieldName);
        return result;
    }

    private static void ValidateCurrencyCode(string currencyCode)
    {
        if (currencyCode is not { Length: 3 }
            || currencyCode.Any(character => character is < 'A' or > 'Z'))
        {
            throw new DomainRuleViolationException(
                "Overpayment reversal currency must contain exactly three uppercase ASCII letters.");
        }
    }
}
