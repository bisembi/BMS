using System.Text.RegularExpressions;

namespace Bms.Api.Diagnostics;

internal sealed record ClientUiFailure(Guid IncidentId, string Kind, string Build,
    string? Asset, int Line, int Column, string ErrorType);

/// Only allowlisted technical fields reach the existing Windows Event Log provider.
/// No raw exception, URL, actor identity, request headers or business data is logged.
internal sealed class ClientUiDiagnostics(ILogger<ClientUiDiagnostics> logger,
    IEnumerable<string> assets, TimeProvider clock)
{
    internal const int EventId = 9610;
    private readonly HashSet<string> knownAssets = new(assets.Where(IsAsset), StringComparer.Ordinal);
    private readonly object gate = new();
    private readonly Dictionary<string, int> actorCounts = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> incidents = [];
    private long window = -1;
    private int count;

    internal static bool IsAsset(string? name) => name is { Length: <= 140 }
        && Regex.IsMatch(name, @"\A[A-Za-z0-9_-]{1,120}-[A-Za-z0-9_-]{8}\.(js|css)\z",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    internal bool Valid(ClientUiFailure value) => value.IncidentId != Guid.Empty
        && value.Kind is "render" or "runtime" or "rejection" or "resource" or "import"
        && value.ErrorType is "Unknown" or "Error" or "TypeError" or "RangeError" or "ReferenceError" or "SyntaxError" or "ChunkLoadError"
        && value.Build is not null && knownAssets.Contains(value.Build) && value.Build.StartsWith("index-", StringComparison.Ordinal)
        && (value.Asset is null || knownAssets.Contains(value.Asset))
        && value.Line is >= 0 and <= 10_000_000 && value.Column is >= 0 and <= 10_000_000
        && (value.Asset is not null || (value.Line == 0 && value.Column == 0));

    internal bool Record(ClientUiFailure value, string actor)
    {
        if (!Valid(value)) return false;
        lock (gate)
        {
            var current = clock.GetUtcNow().ToUnixTimeSeconds() / 60;
            if (current != window) { window = current; count = 0; actorCounts.Clear(); incidents.Clear(); }
            if (incidents.Contains(value.IncidentId)) return true;
            var actorCount = actorCounts.GetValueOrDefault(actor);
            if (count >= 100 || actorCount >= 5) return false;
            actorCounts[actor] = actorCount + 1; count += 1; incidents.Add(value.IncidentId);
            try
            {
                logger.LogWarning(new EventId(EventId, "ClientUiFailure"),
                    "BMS UI failure. IncidentId={IncidentId}; Kind={Kind}; Build={Build}; Asset={Asset}; Line={Line}; Column={Column}; ErrorType={ErrorType}",
                    value.IncidentId, value.Kind, value.Build, value.Asset ?? "none", value.Line, value.Column, value.ErrorType);
                return true;
            }
            catch { return false; } // Diagnostics must not break business workflows.
        }
    }
}
