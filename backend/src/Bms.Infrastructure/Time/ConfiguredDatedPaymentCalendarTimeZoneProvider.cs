using Bms.Application.Treasury;
using Microsoft.Extensions.Configuration;

namespace Bms.Infrastructure.Time;

/// <summary>
/// Uses the existing explicit BMS business-zone setting without inventing a
/// production default or coupling calendar dates to the currency-rate age.
/// Missing/invalid configuration refuses this read capability, not an empty
/// calendar. Resolution runs inside the service's safe error boundary.
/// </summary>
public sealed class ConfiguredDatedPaymentCalendarTimeZoneProvider(IConfiguration configuration)
    : IDatedPaymentCalendarTimeZoneProvider
{
    public TimeZoneInfo? Resolve()
    {
        var id = BusinessCalendarConfiguration.ResolveTimeZoneId(configuration);
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (!string.Equals(id, id.Trim(), StringComparison.Ordinal) || id.Any(char.IsControl))
        {
            throw new InvalidOperationException("Configured BMS business time zone is not canonical.");
        }
        return TimeZoneInfo.FindSystemTimeZoneById(id);
    }
}
