namespace Bms.Api.Security;

// Presence is telemetry, never an authentication token or an authorization source.
// One random ID per page lifetime; no SQL persistence, URLs, IPs or passwords.
public sealed class ActiveSessionRegistry(TimeProvider clock)
{
    public const int ActiveSeconds = 180;
    public const int MaximumSessions = 4096;
    public const int MaximumActorSessions = 32;
    private readonly object gate = new();
    private readonly Dictionary<(string Actor, Guid Tab), Entry> entries = new();
    private sealed record Entry(string Actor, Guid Tab, string Name, string? Role,
        DateTimeOffset First, DateTimeOffset Last, long Sequence);

    public bool Touch(string actor, Guid tab, string name, string? role, long sequence)
    {
        if (tab == Guid.Empty || sequence is < 1 or > 9_007_199_254_740_991
            || !Bms.Application.Common.ActorIdentity.IsCanonical(actor))
            throw new ArgumentException("Invalid presence identity.");
        lock (gate)
        {
            var now = clock.GetUtcNow();
            Prune(now);
            var key = (actor, tab);
            if (!entries.TryGetValue(key, out var existing)
                && (entries.Count >= MaximumSessions
                    || entries.Values.Count(x => x.Actor == actor) >= MaximumActorSessions)) return false;
            if (existing is not null && sequence <= existing.Sequence) return true;
            entries[key] = new Entry(actor, tab, name, role, existing?.First ?? now, now, sequence);
            return true;
        }
    }

    public ActiveSessionPage Read(string? search, int skip, int take)
    {
        if (search?.Length > 100 || skip < 0 || skip > 1_000_000 || take is < 1 or > 100)
            throw new ArgumentException("Invalid presence query.");
        lock (gate)
        {
            var now = clock.GetUtcNow();
            Prune(now);
            var query = entries.Values.Where(x => string.IsNullOrWhiteSpace(search)
                || x.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
            var ordered = query.OrderBy(x => x.Name, StringComparer.Ordinal)
                .ThenBy(x => x.First).ThenBy(x => x.Actor, StringComparer.Ordinal).ThenBy(x => x.Tab).ToArray();
            return new ActiveSessionPage(ordered.Skip(skip).Take(take)
                .Select(x => new ActiveSessionRow(x.Tab, x.Name, x.Role, x.First, x.Last)).ToArray(),
                ordered.Length, skip, take, now, ActiveSeconds);
        }
    }

    public void Close(string actor, Guid tab, long sequence)
    {
        lock (gate)
        {
            if (entries.TryGetValue((actor, tab), out var entry) && sequence > entry.Sequence)
                entries.Remove((actor, tab));
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var key in entries.Where(x => now - x.Value.Last >= TimeSpan.FromSeconds(ActiveSeconds))
            .Select(x => x.Key).ToArray()) entries.Remove(key);
    }
}

public sealed record ActiveSessionRow(Guid SessionId, string UserName, string? RoleCode,
    DateTimeOffset ConnectedAtUtc, DateTimeOffset LastSeenAtUtc);
public sealed record ActiveSessionPage(IReadOnlyList<ActiveSessionRow> Items, int Total, int Skip, int Take,
    DateTimeOffset CheckedAtUtc, int ActiveSeconds);
