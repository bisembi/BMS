namespace Bms.Application.ExpenseRequests;

public static class ExpenseRequestCopyDefaults
{
    private static readonly TimeZoneInfo BusinessZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Qyzylorda");

    public static DateOnly NextPaymentDate(DateTimeOffset utcNow)
    {
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, BusinessZone).DateTime).AddDays(1);
        return date.DayOfWeek switch
        {
            DayOfWeek.Saturday => date.AddDays(2),
            DayOfWeek.Sunday => date.AddDays(1),
            _ => date
        };
    }
}
