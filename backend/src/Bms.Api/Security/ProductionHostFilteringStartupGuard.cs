namespace Bms.Api.Security;

/// <summary>
/// Prevents a production-like host from silently disabling ASP.NET Core host
/// filtering through an absent or top-level-wildcard AllowedHosts setting.
/// </summary>
public static class ProductionHostFilteringStartupGuard
{
    internal const string MissingAllowlistMessage =
        "An explicit AllowedHosts allowlist without a top-level wildcard is required " +
        "outside Development and Testing.";

    public static void EnsureConfigured(
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            return;
        }

        var allowedHosts = configuration["AllowedHosts"]?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
        if (allowedHosts.Length == 0 || allowedHosts.Any(IsTopLevelWildcard))
        {
            throw new InvalidOperationException(MissingAllowlistMessage);
        }
    }

    private static bool IsTopLevelWildcard(string host) =>
        host is "*" or "[::]" or "0.0.0.0";
}
