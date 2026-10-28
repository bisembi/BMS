using Bms.Application.Accruals.Notifications;
using Microsoft.Extensions.Configuration;

namespace Bms.Infrastructure.Time;

public static class AccrualNotificationConfiguration
{
    public const string EnabledKey = "Bms:AccrualNotifications:Enabled";
    public const string SystemActorKey = "Bms:AccrualNotifications:SystemActorExternalId";

    public static AccrualNotificationSettings Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var raw = configuration[EnabledKey];
        var enabled = false;
        if (raw is not null && !bool.TryParse(raw, out enabled))
        {
            throw new InvalidOperationException("Accrual notification Enabled must be a boolean.");
        }
        return new AccrualNotificationSettings(enabled, configuration[SystemActorKey]);
    }
}
