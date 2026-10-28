using Bms.Domain.ExpenseRequests;

namespace Bms.Application.Treasury;

/// <summary>User-approved cash-forecast display rule. Never posts or changes a
/// request's contractual/planned date, workflow status or historical bank date.</summary>
public static class PaymentCalendarForecast443
{
    public static bool ShouldRoll(ExpenseRequestStatus status) => status is
        ExpenseRequestStatus.InApproval or ExpenseRequestStatus.Returned or ExpenseRequestStatus.AwaitingNsi
        or ExpenseRequestStatus.Approved or ExpenseRequestStatus.PartiallyPaid;

    public static bool IsAllowed(ExpenseRequestStatus status) => ShouldRoll(status)
        || status is ExpenseRequestStatus.ToPay or ExpenseRequestStatus.Paid or ExpenseRequestStatus.Closed;

    public static bool IsAllowedCode(string code) => ExpenseRequestStatusCode.TryParse(code,out var status) && IsAllowed(status);

    public static DateOnly NextBusinessDay(DateTimeOffset utcNow,TimeZoneInfo companyZone)
    {
        ArgumentNullException.ThrowIfNull(companyZone);
        if(utcNow==default)throw new PaymentCalendarUnavailableException();
        var date=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow,companyZone).DateTime).AddDays(1);
        while(date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)date=date.AddDays(1);
        return date;
    }

    public static DateOnly? PlanDate(ExpenseRequestStatus status,DateOnly? original,DateOnly? forecastDate) =>
        forecastDate.HasValue&&ShouldRoll(status)?forecastDate:original;
}
