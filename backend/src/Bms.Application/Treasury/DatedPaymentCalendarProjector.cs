using Bms.Application.ExpenseRequests;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Payments;

namespace Bms.Application.Treasury;

/// <summary>
/// Pure dated projection of one previously authorized request. Owner decision
/// 2026-09-14: the calendar shows every actual bank movement and every Applied
/// allocation on its source date, without capping them to the obligation. The
/// capped settlement projection used by cards and command validation is a
/// different calculation and is not consulted here.
/// The caller must pass the complete immutable payment ledger and the complete
/// allocation scope of the request; this component performs no authorization.
/// Cancellation is cooperative: it is observed while the inputs are copied, per
/// source item and per summed row, and between the shared ledger reconciliation
/// and the ordering step. Those two synchronous calls are not preempted.
/// </summary>
public static class DatedPaymentCalendarProjector
{
    private const string Reserved = "Reserved";
    private const string Applied = "Applied";
    private const string Released = "Released";

    public static DatedPaymentCalendarProjection Project(
        ExpenseRequest request,
        Guid currencyId,
        DatedPaymentCalendarPeriod period,
        IReadOnlyList<ConfirmedPaymentEvent> events,
        IReadOnlyList<ExpenseRequestAppliedOverpaymentSnapshot> allocations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(allocations);
        if (currencyId == Guid.Empty || period.BusinessTimeZone is null || period.From > period.Through)
        {
            throw new DatedPaymentCalendarProjectionException();
        }

        // One copy of each input: validation and projection read the same items.
        var ledgerEvents = Snapshot(events, cancellationToken);
        var allocationItems = Snapshot(allocations, cancellationToken);

        // Whole-evidence integrity comes before any date filter, so an orphan,
        // duplicate or foreign item outside the period is refused, not hidden.
        var ledger = PaymentFactLedgerReconciler.Reconcile(request.Id, currencyId, ledgerEvents);
        cancellationToken.ThrowIfCancellationRequested();
        var isKzt = string.Equals(request.CurrencyCode, "KZT", StringComparison.Ordinal);
        ValidateAllocations(request, currencyId, isKzt, allocationItems, cancellationToken);

        var rows = new List<DatedPaymentCalendarMovementRow>();
        var identities = new HashSet<DatedPaymentCalendarSourceIdentity>();
        var ledgerNet = 0m;
        var ledgerNetKzt = 0m;
        foreach (var item in ledgerEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isKzt && item.Amount != item.AmountKzt)
            {
                throw new DatedPaymentCalendarProjectionException();
            }

            var sign = item.IsReversal ? -1m : 1m;
            ledgerNet = Add(ledgerNet, sign * item.Amount);
            ledgerNetKzt = Add(ledgerNetKzt, sign * item.AmountKzt);
            AddMovement(
                rows,
                identities,
                period,
                item.IsReversal
                    ? DatedPaymentCalendarMovementKind.BankReversal
                    : DatedPaymentCalendarMovementKind.BankPayment,
                new DatedPaymentCalendarSourceIdentity(
                    DatedPaymentCalendarSourceType.PaymentEvent,
                    item.EventId),
                item.PaidAt,
                sign * item.Amount,
                sign * item.AmountKzt,
                item.ReversedEventId);
        }

        if (ledgerNet != ledger.ActiveAmount || ledgerNetKzt != ledger.ActiveAmountKzt)
        {
            throw new DatedPaymentCalendarProjectionException();
        }

        foreach (var item in allocationItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Status != Applied)
            {
                // Reserved and Released allocations are not execution.
                continue;
            }

            if (item.AppliedOrReleasedAtUtc is not { } appliedAtUtc)
            {
                throw new DatedPaymentCalendarProjectionException();
            }

            AddMovement(
                rows,
                identities,
                period,
                DatedPaymentCalendarMovementKind.AppliedAllocation,
                new DatedPaymentCalendarSourceIdentity(
                    DatedPaymentCalendarSourceType.Allocation,
                    item.AllocationId),
                appliedAtUtc,
                item.Amount,
                item.AmountKzt,
                reversedEventId: null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var ordered = rows
            .OrderBy(row => row.BusinessDate)
            .ThenBy(row => row.OccurredAt.UtcDateTime)
            .ThenBy(row => row.Source.Type)
            .ThenBy(row => row.Source.Id.ToString("D"), StringComparer.Ordinal)
            .ToArray();

        cancellationToken.ThrowIfCancellationRequested();
        var bankPayments = Sum(ordered, DatedPaymentCalendarMovementKind.BankPayment, cancellationToken);
        var bankReversals = Sum(ordered, DatedPaymentCalendarMovementKind.BankReversal, cancellationToken);
        var appliedAllocations = Sum(ordered, DatedPaymentCalendarMovementKind.AppliedAllocation, cancellationToken);
        var movementTotal = Sum(ordered, kind: null, cancellationToken);
        if (Add(Add(bankPayments.Amount, bankReversals.Amount), appliedAllocations.Amount)
                != movementTotal.Amount
            || Add(Add(bankPayments.AmountKzt, bankReversals.AmountKzt), appliedAllocations.AmountKzt)
                != movementTotal.AmountKzt)
        {
            throw new DatedPaymentCalendarProjectionException();
        }

        return new DatedPaymentCalendarProjection(
            request.Id,
            currencyId,
            request.CurrencyCode,
            period,
            PlanEntry(request, period, isKzt),
            Array.AsReadOnly(ordered),
            bankPayments,
            bankReversals,
            appliedAllocations,
            movementTotal);
    }

    private static T[] Snapshot<T>(IReadOnlyList<T> source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = new List<T>(source.Count);
        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(item);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return items.ToArray();
    }

    private static DatedPaymentCalendarPlanEntry? PlanEntry(
        ExpenseRequest request,
        DatedPaymentCalendarPeriod period,
        bool isKzt)
    {
        if (request.PlannedPaymentDate is not { } plannedDate
            || plannedDate < period.From
            || plannedDate > period.Through)
        {
            return null;
        }

        var amount = request.TotalAmount;
        var amountKzt = request.TotalAmountKzt;
        MoneyAmountPolicy.EnsureNonNegative(amount, "Dated calendar planned amount");
        MoneyAmountPolicy.EnsureNonNegative(amountKzt, "Dated calendar planned KZT amount");
        if (isKzt && amountKzt != amount)
        {
            throw new DatedPaymentCalendarProjectionException();
        }

        return new DatedPaymentCalendarPlanEntry(plannedDate, amount, amountKzt);
    }

    private static void AddMovement(
        List<DatedPaymentCalendarMovementRow> rows,
        HashSet<DatedPaymentCalendarSourceIdentity> identities,
        DatedPaymentCalendarPeriod period,
        DatedPaymentCalendarMovementKind kind,
        DatedPaymentCalendarSourceIdentity source,
        DateTimeOffset occurredAt,
        decimal signedAmount,
        decimal signedAmountKzt,
        Guid? reversedEventId)
    {
        if (!identities.Add(source))
        {
            throw new DatedPaymentCalendarProjectionException();
        }

        var businessDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(occurredAt, period.BusinessTimeZone).DateTime);
        if (businessDate < period.From || businessDate > period.Through)
        {
            return;
        }

        rows.Add(new DatedPaymentCalendarMovementRow(
            businessDate,
            kind,
            source,
            occurredAt,
            signedAmount,
            signedAmountKzt,
            reversedEventId));
    }

    private static DatedPaymentCalendarAmounts Sum(
        IReadOnlyList<DatedPaymentCalendarMovementRow> rows,
        DatedPaymentCalendarMovementKind? kind,
        CancellationToken cancellationToken)
    {
        var amount = 0m;
        var amountKzt = 0m;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (kind is null || row.Kind == kind)
            {
                amount = Add(amount, row.SignedAmount);
                amountKzt = Add(amountKzt, row.SignedAmountKzt);
            }
        }

        // A running total may pass the storage bound before a later signed row
        // brings it back, so only the completed aggregate is bounded.
        return new DatedPaymentCalendarAmounts(Representable(amount), Representable(amountKzt));
    }

    /// <summary>Exact accumulation; range is checked on completed aggregates only.</summary>
    private static decimal Add(decimal left, decimal right)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException)
        {
            throw new DatedPaymentCalendarProjectionException();
        }
    }

    private static decimal Representable(decimal total)
    {
        if (!MoneyAmountPolicy.IsRepresentable(total))
        {
            throw new DatedPaymentCalendarProjectionException();
        }

        return total;
    }

    /// <summary>
    /// Mirrors the settlement projection's allocation evidence contract, except
    /// that it deliberately applies no obligation cap.
    /// </summary>
    private static void ValidateAllocations(
        ExpenseRequest request,
        Guid currencyId,
        bool isKzt,
        IReadOnlyList<ExpenseRequestAppliedOverpaymentSnapshot> allocations,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<Guid>();
        foreach (var item in allocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item is null || item.AllocationId == Guid.Empty || !ids.Add(item.AllocationId) ||
                item.TargetExpenseRequestId != request.Id || item.OrganizationId == Guid.Empty ||
                item.OrganizationId != request.OrganizationId || item.CounterpartyId == Guid.Empty ||
                item.CounterpartyId != request.CounterpartyId || item.CurrencyId != currencyId ||
                item.Status is not (Reserved or Applied or Released) ||
                (item.Status == Reserved) != (item.AppliedOrReleasedAtUtc is null))
            {
                throw new DatedPaymentCalendarProjectionException();
            }

            MoneyAmountPolicy.EnsurePositive(item.Amount, "Allocated amount");
            MoneyAmountPolicy.EnsurePositive(item.AmountKzt, "Allocated KZT amount");
            ConfirmedPaymentEventContract.ValidateProcessingTime(item.ReservedAtUtc);
            if (item.AppliedOrReleasedAtUtc is { } changed)
            {
                ConfirmedPaymentEventContract.ValidateProcessingTime(changed);
                if (changed < item.ReservedAtUtc)
                {
                    throw new DatedPaymentCalendarProjectionException();
                }
            }

            if (isKzt && item.Amount != item.AmountKzt)
            {
                throw new DatedPaymentCalendarProjectionException();
            }
        }
    }
}
