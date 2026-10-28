using Bms.Application.ExpenseRequests;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Payments;

namespace Bms.Application.Treasury;

/// <summary>A legitimate scope exceeds the synchronous report's technical ceiling.</summary>
public sealed class DatedPaymentCalendarScopeTooLargeException : Exception
{
    public DatedPaymentCalendarScopeTooLargeException()
        : base("The dated payment calendar period is too large. Request a narrower period.")
    {
    }
}

/// <summary>A separate dated view; the existing capped calendar contract is unchanged.</summary>
public sealed record DatedPaymentCalendarQuery(
    DateOnly From,
    DateOnly Through,
    int Skip,
    int Take,
    string? StatusCode = null,
    string? CurrencyCode = null)
{
    public string Grouping { get; init; } = PaymentCalendarGroupingCodes.Date;

    public void Validate()
    {
        new PaymentCalendarQuery(From, Through, Skip, Take, StatusCode)
        {
            Grouping = Grouping,
        }.Validate();
        if (CurrencyCode is not null
            && (CurrencyCode.Length != 3 || CurrencyCode.Any(character => character is < 'A' or > 'Z')))
        {
            throw new InvalidPaymentCalendarQueryException();
        }
    }
}

public static class DatedPaymentCalendarRowKindCodes
{
    public const string Plan = "PLAN";
    public const string BankPayment = "BANK_PAYMENT";
    public const string BankReversal = "BANK_REVERSAL";
    public const string AppliedAllocation = "APPLIED_ALLOCATION";
}

/// <summary>
/// Internal, previously authorized capture of one request with its COMPLETE
/// ledger and allocation history. Labels and CanOpen belong to the same read
/// snapshot. This is not an HTTP input, authorization check or public DTO.
/// </summary>
public sealed record DatedPaymentCalendarRequestCapture(
    ExpenseRequest Request,
    Guid CurrencyId,
    string? CounterpartyName,
    string? OrganizationCode,
    string? OrganizationName,
    bool CanOpen,
    IReadOnlyList<ConfirmedPaymentEvent> Events,
    IReadOnlyList<ExpenseRequestAppliedOverpaymentSnapshot> Allocations);

/// <summary>
/// Only request/organization IDs support navigation and grouping. Source event,
/// allocation, reversal IDs and raw timestamps are deliberately not exposed.
/// Plan and actual columns are separate; unknown historical plan KZT stays null.
/// </summary>
public sealed record DatedPaymentCalendarRowDto(
    DateOnly BusinessDate,
    string Kind,
    Guid ExpenseRequestId,
    string Status,
    string CurrencyCode,
    string? CounterpartyName,
    string? Purpose,
    Guid? OrganizationId,
    string? OrganizationCode,
    string? OrganizationName,
    decimal? PlanAmount,
    decimal? PlanAmountKzt,
    decimal? SignedAmount,
    decimal? SignedAmountKzt,
    bool CanOpen);

public sealed record DatedPaymentCalendarCurrencyTotalsDto(
    string CurrencyCode,
    int PlanCount,
    decimal PlanAmount,
    decimal BankPaymentAmount,
    decimal BankReversalAmount,
    decimal AppliedAllocationAmount,
    decimal MovementAmount);

public sealed record DatedPaymentCalendarKztTotalsDto(
    int PlanCount,
    int PlanKztUnknownCount,
    decimal? PlanAmountKzt,
    decimal BankPaymentAmountKzt,
    decimal BankReversalAmountKzt,
    decimal AppliedAllocationAmountKzt,
    decimal MovementAmountKzt);

public sealed record DatedPaymentCalendarOrganizationGroupDto(
    Guid? OrganizationId,
    string? OrganizationCode,
    string? OrganizationName,
    int Total,
    IReadOnlyList<DatedPaymentCalendarCurrencyTotalsDto> CurrencyTotals,
    DatedPaymentCalendarKztTotalsDto KztTotals);

public sealed record DatedPaymentCalendarPageDto(
    IReadOnlyList<DatedPaymentCalendarRowDto> Items,
    int Skip,
    int Take,
    int Total,
    string Grouping,
    string BusinessTimeZoneId,
    IReadOnlyList<DatedPaymentCalendarCurrencyTotalsDto> CurrencyTotals,
    DatedPaymentCalendarKztTotalsDto KztTotals,
    IReadOnlyList<DatedPaymentCalendarOrganizationGroupDto> OrganizationGroups);

/// <summary>
/// One complete filtered report, before paging. A later API/export adapter can
/// consume the same calculation; it must not recalculate money from page rows.
/// </summary>
public sealed record DatedPaymentCalendarReport(
    DatedPaymentCalendarQuery Query,
    string BusinessTimeZoneId,
    IReadOnlyList<DatedPaymentCalendarRowDto> Rows,
    IReadOnlyList<DatedPaymentCalendarCurrencyTotalsDto> CurrencyTotals,
    DatedPaymentCalendarKztTotalsDto KztTotals,
    IReadOnlyList<DatedPaymentCalendarOrganizationGroupDto> OrganizationGroups)
{
    public DatedPaymentCalendarPageDto ToPage()
    {
        var items = Array.AsReadOnly(Rows.Skip(Query.Skip).Take(Query.Take).ToArray());
        var visibleOrganizations = items.Select(row => row.OrganizationId).ToHashSet();
        return new DatedPaymentCalendarPageDto(
            items, Query.Skip, Query.Take, Rows.Count, Query.Grouping, BusinessTimeZoneId,
            CurrencyTotals, KztTotals,
            Array.AsReadOnly(OrganizationGroups
                .Where(group => visibleOrganizations.Contains(group.OrganizationId)).ToArray()));
    }
}
