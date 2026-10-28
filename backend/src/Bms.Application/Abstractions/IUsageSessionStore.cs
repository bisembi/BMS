namespace Bms.Application.Abstractions;

// Presence measures an open workspace, not keyboard activity or paid working hours.
public interface IUsageSessionStore
{
    Task<bool> TouchAsync(string actor, Guid tab, long sequence, string name, bool closed, CancellationToken cancellationToken);
    Task<UsageSessionPage> ReadAsync(string? search, DateOnly from, DateOnly through, int skip, int take, CancellationToken cancellationToken);
    Task<UsageDailyPage> ReadDailyAsync(string? search, DateOnly from, DateOnly through, int skip, int take, CancellationToken cancellationToken);
}

public sealed class UsageSessionCapacityException : Exception { }

public sealed record UsageSessionRow(Guid SessionId, string UserName, DateTimeOffset EnteredAtUtc,
    DateTimeOffset LastSeenAtUtc, DateTimeOffset? ExitedAtUtc, long DurationSeconds, string State);
public sealed record UsageSessionPage(IReadOnlyList<UsageSessionRow> Items, int Total, int Skip, int Take,
    DateTimeOffset CheckedAtUtc);
public sealed record UsageDailyRow(Guid SummaryId, DateOnly Day, string UserName, DateTimeOffset FirstEntryAtUtc,
    DateTimeOffset LastExitAtUtc, long DurationSeconds, int SessionCount, bool HasApproximateExit, bool IsActive);
public sealed record UsageDailyPage(IReadOnlyList<UsageDailyRow> Items, int Total, int Skip, int Take, DateTimeOffset CheckedAtUtc);
