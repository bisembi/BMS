using Bms.Domain.ExpenseRequests;

namespace Bms.Application.Treasury;

public static class PaymentCalendarGroupingCodes
{
    public const string Date = "date";
    public const string Organization = "organization";
}

/// <summary>
/// The exact statuses a payment calendar row may carry. A request outside this
/// set has no settlement meaning: it is either not yet approved for payment or
/// it left the payment path entirely, so it must never appear in a treasury
/// period view.
/// </summary>
public static class PaymentCalendarStatusCodes
{
    public const string ToPay = "TO_PAY";
    public const string PartiallyPaid = "PARTIALLY_PAID";
    public const string Paid = "PAID";
    public const string Closed = "CLOSED";

    private static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>(StringComparer.Ordinal)
        {
            ToPay,
            PartiallyPaid,
            Paid,
            Closed,
        };

    public static bool IsAllowed(string statusCode) =>
        !string.IsNullOrEmpty(statusCode) && Allowed.Contains(statusCode);

    public static IReadOnlyCollection<ExpenseRequestStatus> AllowedStatuses { get; } =
    [
        ExpenseRequestStatus.ToPay,
        ExpenseRequestStatus.PartiallyPaid,
        ExpenseRequestStatus.Paid,
        ExpenseRequestStatus.Closed,
    ];

    public static ExpenseRequestStatus Parse(string statusCode) => statusCode switch
    {
        ToPay => ExpenseRequestStatus.ToPay,
        PartiallyPaid => ExpenseRequestStatus.PartiallyPaid,
        Paid => ExpenseRequestStatus.Paid,
        Closed => ExpenseRequestStatus.Closed,
        _ => throw new InvalidPaymentCalendarQueryException(),
    };
}

/// <summary>
/// A read-only request for one page of the payment calendar. Both period ends
/// are inclusive: a treasurer asking for a month means the whole month.
/// </summary>
public sealed record PaymentCalendarQuery(
    DateOnly From,
    DateOnly Through,
    int Skip,
    int Take,
    string? StatusCode)
{
    public const int MinimumTake = 1;
    public const int MaximumTake = 100;

    public string Grouping { get; init; } = PaymentCalendarGroupingCodes.Date;

    /// <summary>
    /// Validates the query without correcting it. Silently clamping a bad
    /// period would answer a question the caller did not ask, and in a money
    /// view that is worse than refusing.
    /// </summary>
    public void Validate()
    {
        if (Grouping is not PaymentCalendarGroupingCodes.Date and not PaymentCalendarGroupingCodes.Organization)
        {
            throw new InvalidPaymentCalendarQueryException();
        }

        if (From > Through)
        {
            throw new InvalidPaymentCalendarQueryException();
        }

        if (Skip < 0 || Take < MinimumTake || Take > MaximumTake)
        {
            throw new InvalidPaymentCalendarQueryException();
        }

        if (StatusCode is not null && !PaymentCalendarStatusCodes.IsAllowed(StatusCode))
        {
            throw new InvalidPaymentCalendarQueryException();
        }
    }

    public IReadOnlyCollection<ExpenseRequestStatus> ResolveStatuses() =>
        StatusCode is null
            ? PaymentCalendarStatusCodes.AllowedStatuses
            : [PaymentCalendarStatusCodes.Parse(StatusCode)];
}

/// <summary>
/// One calendar row. Carries no payment identifier, no source system, no event
/// id and no internal ledger key. Request and organization IDs support UI
/// navigation/grouping, never access to the accounting records behind a row.
/// </summary>
public sealed record PaymentCalendarRowDto(
    Guid ExpenseRequestId,
    DateOnly PlannedPaymentDate,
    string Status,
    string CurrencyCode,
    string? CounterpartyName,
    string? Purpose,
    decimal ObligationAmount,
    decimal PaidAmount,
    decimal RemainingAmount,
    string Progress,
    bool CanOpen)
{
    public Guid? OrganizationId { get; init; }
    public string? OrganizationCode { get; init; }
    public string? OrganizationName { get; init; }

    /// <summary>Only credit already applied to this obligation; never a reservation.</summary>
    public decimal AppliedOverpaymentAmount { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public decimal ApprovedRoundingAmount { get; init; }
    public decimal BankPaidAmount => PaidAmount - AppliedOverpaymentAmount - ApprovedRoundingAmount;
}

/// <summary>
/// Totals over the whole filtered scope, not over the returned page: the
/// question a period view answers is "how much is due in this period", which a
/// page sum cannot answer.
/// </summary>
public sealed record PaymentCalendarTotalsDto(
    decimal ObligationAmount,
    decimal PaidAmount,
    decimal RemainingAmount,
    decimal OverpaidAmount)
{
    public decimal AppliedOverpaymentAmount { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public decimal ApprovedRoundingAmount { get; init; }
    public decimal BankPaidAmount => PaidAmount - AppliedOverpaymentAmount - ApprovedRoundingAmount;
}

public sealed record PaymentCalendarPageDto(
    IReadOnlyList<PaymentCalendarRowDto> Items,
    int Skip,
    int Take,
    int Total,
    PaymentCalendarTotalsDto Totals)
{
    public string Grouping { get; init; } = PaymentCalendarGroupingCodes.Date;
    public IReadOnlyList<PaymentCalendarOrganizationGroupDto> OrganizationGroups { get; init; } = [];
}

/// <summary>Full-period totals for an organization represented on this page.</summary>
public sealed record PaymentCalendarOrganizationGroupDto(
    Guid? OrganizationId,
    string? OrganizationCode,
    string? OrganizationName,
    int Total,
    PaymentCalendarTotalsDto Totals);

/// <summary>
/// A malformed calendar query. The message is fixed: it states what is wrong
/// with the request shape and never echoes the value that was sent.
/// </summary>
public sealed class InvalidPaymentCalendarQueryException : Exception
{
    private const string SafeMessage =
        "The payment calendar query is outside its public contract.";

    public InvalidPaymentCalendarQueryException()
        : base(SafeMessage)
    {
    }
}

/// <summary>
/// The calendar could not be read. Reported as unavailability rather than as
/// an empty period: an outage must never look like "nothing to pay".
/// </summary>
public sealed class PaymentCalendarUnavailableException : Exception
{
    private const string SafeMessage =
        "The payment calendar is temporarily unavailable.";

    public PaymentCalendarUnavailableException()
        : base(SafeMessage)
    {
    }

    public PaymentCalendarUnavailableException(Exception innerException)
        : base(
            SafeMessage,
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}
