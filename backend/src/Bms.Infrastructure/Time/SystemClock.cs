using Bms.Application.Abstractions;

namespace Bms.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow
    {
        get
        {
            var now = DateTimeOffset.UtcNow;
            var millisecondTicks = now.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond;
            return new DateTimeOffset(millisecondTicks, TimeSpan.Zero);
        }
    }
}
