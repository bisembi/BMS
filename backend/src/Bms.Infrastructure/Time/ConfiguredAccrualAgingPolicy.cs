using System.Globalization;
using Bms.Application.Abstractions;
using Bms.Application.Accruals;
using Bms.Domain.Accruals;
using Microsoft.Extensions.Configuration;

namespace Bms.Infrastructure.Time;

public sealed class ConfiguredAccrualAgingPolicy : IAccrualAgingPolicy
{
    public const string YellowConfigurationKey = "Bms:ActiveAccruals:YellowFromDays";
    public const string OrangeConfigurationKey = "Bms:ActiveAccruals:OrangeFromDays";
    public const string RedConfigurationKey = "Bms:ActiveAccruals:RedFromDays";

    private readonly IClock _clock;
    private readonly AccrualAgeThresholds? _thresholds;
    private readonly TimeZoneInfo? _businessTimeZone;

    public ConfiguredAccrualAgingPolicy(IConfiguration configuration, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        var values = new[]
        {
            configuration[YellowConfigurationKey],
            configuration[OrangeConfigurationKey],
            configuration[RedConfigurationKey]
        };
        if (values.All(string.IsNullOrWhiteSpace))
        {
            return;
        }

        var ages = new int[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            if (!int.TryParse(values[index], NumberStyles.None, CultureInfo.InvariantCulture, out ages[index]))
            {
                throw new InvalidOperationException("All accrual age thresholds must be configured as integers.");
            }
        }

        try
        {
            _thresholds = new AccrualAgeThresholds(ages[0], ages[1], ages[2]);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("Configured accrual age thresholds are invalid.", exception);
        }

        var timeZoneId = BusinessCalendarConfiguration.ResolveTimeZoneId(configuration);
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            // The deployment owner must choose the business calendar. UTC is
            // not an implicit fallback and currency-rate policy is not changed.
            return;
        }

        try
        {
            _businessTimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException("Configured BMS business time zone is invalid.", exception);
        }
    }

    public AccrualAgingContext? GetCurrentContext() =>
        _thresholds is not null && _businessTimeZone is not null
            ? new AccrualAgingContext(_clock.UtcNow, _businessTimeZone, _thresholds)
            : null;
}
