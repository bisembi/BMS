using System.Globalization;
using Bms.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Bms.Infrastructure.Time;

public sealed class ConfiguredCurrencyRateUsagePolicy : ICurrencyRateUsagePolicy
{
    public const string BusinessTimeZoneConfigurationKey = "Bms:BusinessTimeZoneId";
    public const string MaximumAgeDaysConfigurationKey = "Bms:CurrencyRates:MaximumAgeDays";

    private readonly IClock _clock;
    private readonly TimeZoneInfo? _businessTimeZone;
    private readonly int? _maximumAgeDays;

    public ConfiguredCurrencyRateUsagePolicy(IConfiguration configuration, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        var timeZoneId = configuration[BusinessTimeZoneConfigurationKey];
        var maximumAgeText = configuration[MaximumAgeDaysConfigurationKey];
        if (string.IsNullOrWhiteSpace(timeZoneId) && string.IsNullOrWhiteSpace(maximumAgeText))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(timeZoneId) || string.IsNullOrWhiteSpace(maximumAgeText))
        {
            throw new InvalidOperationException(
                "Both Bms:BusinessTimeZoneId and Bms:CurrencyRates:MaximumAgeDays must be configured together.");
        }

        try
        {
            _businessTimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new InvalidOperationException("Configured BMS business time zone is unknown.", exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new InvalidOperationException("Configured BMS business time zone is invalid.", exception);
        }

        if (!int.TryParse(
                maximumAgeText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var maximumAgeDays)
            || maximumAgeDays < 0)
        {
            throw new InvalidOperationException(
                "Bms:CurrencyRates:MaximumAgeDays must be a nonnegative integer.");
        }

        _maximumAgeDays = maximumAgeDays;
    }

    public CurrencyRateUsageContext? GetCurrentContext()
    {
        if (_businessTimeZone is null || !_maximumAgeDays.HasValue)
        {
            return null;
        }

        var localNow = TimeZoneInfo.ConvertTime(_clock.UtcNow, _businessTimeZone);
        return new CurrencyRateUsageContext(
            DateOnly.FromDateTime(localNow.DateTime),
            _maximumAgeDays.Value);
    }
}
