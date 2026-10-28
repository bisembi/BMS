using Microsoft.Extensions.Configuration;

namespace Bms.Infrastructure.Time;

/// <summary>
/// Reporting needs an explicit business calendar even when foreign-currency
/// operations remain disabled for lack of an approved rate-age policy.
/// An explicitly configured common calendar takes precedence for consistency.
/// </summary>
public static class BusinessCalendarConfiguration
{
    public const string ReportingTimeZoneConfigurationKey = "Bms:Reporting:BusinessTimeZoneId";

    public static string? ResolveTimeZoneId(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration[ConfiguredCurrencyRateUsagePolicy.BusinessTimeZoneConfigurationKey]
            ?? configuration[ReportingTimeZoneConfigurationKey];
    }
}
