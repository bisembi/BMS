using Bms.Application.Abstractions;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Freezes the substitution-validity instant for one dependency-injection
/// scope so page/count/detail/export predicates cannot cross a period boundary
/// during one request.
/// </summary>
public sealed class ExpenseRequestAccessMoment
{
    public ExpenseRequestAccessMoment(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        UtcNow = clock.UtcNow;
    }

    public DateTimeOffset UtcNow { get; }
}
