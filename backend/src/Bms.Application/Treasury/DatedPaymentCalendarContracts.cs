namespace Bms.Application.Treasury;

/// <summary>
/// Inclusive business-date window of the dated payment calendar. The time zone
/// is always supplied by the caller: the projection never reads the machine
/// zone and does not choose a production default.
/// </summary>
public sealed record DatedPaymentCalendarPeriod(
    DateOnly From,
    DateOnly Through,
    TimeZoneInfo BusinessTimeZone)
{
    /// <summary>Only the forecast matrix opts in. Historical dated reports do not.</summary>
    public DateOnly? ForecastPlanDate { get; init; }
}

public enum DatedPaymentCalendarMovementKind
{
    BankPayment = 1,
    BankReversal = 2,
    AppliedAllocation = 3,
}

public enum DatedPaymentCalendarSourceType
{
    PaymentEvent = 1,
    Allocation = 2,
}

/// <summary>
/// Exact internal evidence identity. The type is part of the identity, so a
/// payment event and an allocation that happen to share a Guid stay distinct.
/// </summary>
public readonly record struct DatedPaymentCalendarSourceIdentity(
    DatedPaymentCalendarSourceType Type,
    Guid Id);

/// <summary>
/// The request's planned obligation on its PlannedPaymentDate. The KZT value
/// is the request's own recorded equivalent and stays null when it is not
/// available; it is never revalued or replaced by zero.
/// </summary>
public sealed record DatedPaymentCalendarPlanEntry(
    DateOnly BusinessDate,
    decimal Amount,
    decimal? AmountKzt);

/// <summary>
/// One actual movement on its own source business date. Amounts are signed:
/// a reversal is negative on the reversal's date and names its original event,
/// which is a separate row whenever it falls inside the period.
/// </summary>
public sealed record DatedPaymentCalendarMovementRow(
    DateOnly BusinessDate,
    DatedPaymentCalendarMovementKind Kind,
    DatedPaymentCalendarSourceIdentity Source,
    DateTimeOffset OccurredAt,
    decimal SignedAmount,
    decimal SignedAmountKzt,
    Guid? ReversedEventId);

public sealed record DatedPaymentCalendarAmounts(
    decimal Amount,
    decimal AmountKzt);

/// <summary>
/// Internal application evidence for one previously authorized request; not a
/// public API DTO. Totals are exact sums of the signed rows. The plan is kept
/// apart from movements: movements are not capped to the obligation, so the
/// difference between them is deliberately not published.
/// </summary>
public sealed record DatedPaymentCalendarProjection(
    Guid ExpenseRequestId,
    Guid CurrencyId,
    string CurrencyCode,
    DatedPaymentCalendarPeriod Period,
    DatedPaymentCalendarPlanEntry? Plan,
    IReadOnlyList<DatedPaymentCalendarMovementRow> Movements,
    DatedPaymentCalendarAmounts BankPayments,
    DatedPaymentCalendarAmounts BankReversals,
    DatedPaymentCalendarAmounts AppliedAllocations,
    DatedPaymentCalendarAmounts MovementTotal);

public sealed class DatedPaymentCalendarProjectionException : Exception
{
    public DatedPaymentCalendarProjectionException()
        : base("Dated payment calendar evidence is inconsistent.")
    {
    }
}
