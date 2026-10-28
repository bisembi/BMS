using Bms.Domain.Accruals;

namespace Bms.Application.Accruals;

public interface IAccrualAgingPolicy
{
    AccrualAgingContext? GetCurrentContext();
}

/// <summary>
/// One immutable calendar/settings snapshot shared by ordering, projection and
/// export. Closing or reopening a request does not supply a different origin.
/// </summary>
public sealed class AccrualAgingContext
{
    public AccrualAgingContext(
        DateTimeOffset capturedAtUtc,
        TimeZoneInfo businessTimeZone,
        AccrualAgeThresholds thresholds)
    {
        if (capturedAtUtc == default || capturedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The accrual aging clock must be a non-default UTC instant.");
        }

        CapturedAtUtc = capturedAtUtc;
        BusinessTimeZone = businessTimeZone ?? throw new ArgumentNullException(nameof(businessTimeZone));
        Thresholds = thresholds ?? throw new ArgumentNullException(nameof(thresholds));
        AsOfDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(capturedAtUtc, businessTimeZone).DateTime);
    }

    public DateTimeOffset CapturedAtUtc { get; }

    public DateOnly AsOfDate { get; }

    public TimeZoneInfo BusinessTimeZone { get; }

    public AccrualAgeThresholds Thresholds { get; }

    public DateOnly GetPaymentBusinessDate(DateTimeOffset fullyPaidAtUtc)
    {
        if (fullyPaidAtUtc == default || fullyPaidAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The full-payment origin must be a non-default UTC instant.");
        }

        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fullyPaidAtUtc, BusinessTimeZone).DateTime);
        if (date > AsOfDate)
        {
            throw new ArgumentException("An active accrual cannot have a future payment date.");
        }

        return date;
    }

    public int GetAgeDays(DateTimeOffset fullyPaidAtUtc) =>
        AsOfDate.DayNumber - GetPaymentBusinessDate(fullyPaidAtUtc).DayNumber;

    public AccrualAgeZone GetZone(DateTimeOffset fullyPaidAtUtc) =>
        Thresholds.Classify(GetAgeDays(fullyPaidAtUtc));

    public static string PublicZone(AccrualAgeZone zone) => zone switch
    {
        AccrualAgeZone.None => "none",
        AccrualAgeZone.Yellow => "yellow",
        AccrualAgeZone.Orange => "orange",
        AccrualAgeZone.Red => "red",
        _ => throw new ArgumentOutOfRangeException(nameof(zone))
    };
}
