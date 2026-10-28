namespace Bms.Application.Abstractions;

public sealed class CurrencyRateUsageContext
{
    public CurrencyRateUsageContext(DateOnly businessDate, int maximumAgeDays)
    {
        if (maximumAgeDays < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumAgeDays),
                "Maximum currency-rate age must be nonnegative.");
        }

        BusinessDate = businessDate;
        MaximumAgeDays = maximumAgeDays;
    }

    public DateOnly BusinessDate { get; }

    public int MaximumAgeDays { get; }

    public bool Accepts(DateOnly rateDate) =>
        rateDate <= BusinessDate
        && rateDate.DayNumber >= BusinessDate.DayNumber - MaximumAgeDays;
}

public interface ICurrencyRateUsagePolicy
{
    CurrencyRateUsageContext? GetCurrentContext();
}
