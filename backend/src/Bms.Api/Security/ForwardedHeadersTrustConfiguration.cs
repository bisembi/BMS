using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

// HttpOverrides still publishes its own deprecated IPNetwork, so the one the
// options list now takes is named explicitly.
using IPNetwork = System.Net.IPNetwork;

namespace Bms.Api.Security;

/// <summary>
/// Decides whether the host may believe a proxy about who the client is and
/// which scheme was used, and refuses to start when that decision is written
/// ambiguously.
///
/// The default is not to believe anything. Forwarding is off unless it is
/// switched on explicitly, and switching it on without naming the proxies that
/// are trusted is a configuration mistake rather than a permissive default:
/// there is no reading of this configuration under which every address is
/// trusted.
///
/// Only <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> are processed.
/// <c>X-Forwarded-Host</c> is deliberately left alone, so the accepted host
/// stays whatever <c>AllowedHosts</c> says and a proxy header cannot become a
/// second way to change it.
/// </summary>
public static class ForwardedHeadersTrustConfiguration
{
    internal const string EnabledKey = "BmsSecurity:ForwardedHeaders:Enabled";
    internal const string KnownProxiesKey = "BmsSecurity:ForwardedHeaders:KnownProxies";
    internal const string KnownNetworksKey = "BmsSecurity:ForwardedHeaders:KnownNetworks";

    /// <summary>
    /// The framework's own switch, <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c>
    /// once the host strips its prefix.
    /// </summary>
    internal const string FrameworkSwitchKey = "ForwardedHeaders_Enabled";

    internal const string FrameworkSwitchEnabledMessage =
        "The framework forwarded-headers switch cannot be enabled; forwarding is " +
        "configured only through BmsSecurity:ForwardedHeaders.";
    internal const string FrameworkSwitchAmbiguousMessage =
        "The framework forwarded-headers switch must be false or absent.";

    internal const string AmbiguousSwitchMessage =
        "BmsSecurity:ForwardedHeaders:Enabled must be true or false.";
    internal const string MissingAllowlistMessage =
        "Enabling forwarded headers requires at least one entry in " +
        "BmsSecurity:ForwardedHeaders:KnownProxies or " +
        "BmsSecurity:ForwardedHeaders:KnownNetworks.";
    internal const string EmptyEntryMessage =
        "A forwarded-headers allowlist entry cannot be empty.";
    internal const string WildcardEntryMessage =
        "A forwarded-headers allowlist cannot trust every address.";
    internal const string InvalidProxyMessage =
        "A forwarded-headers proxy entry must be a single IP address.";
    internal const string InvalidNetworkMessage =
        "A forwarded-headers network entry must be an IP address with a prefix length.";

    /// <summary>The single hop this host is willing to take from the chain.</summary>
    internal const int ForwardLimit = 1;

    /// <summary>
    /// Returns the options to apply, or <c>null</c> when forwarding is off.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The switch is not a boolean, the allowlist is missing, or an entry is
    /// empty, a wildcard, or not an address.
    /// </exception>
    public static ForwardedHeadersOptions? Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (FrameworkSwitchEnabled(configuration))
        {
            throw new InvalidOperationException(FrameworkSwitchEnabledMessage);
        }

        var switchValue = configuration[EnabledKey];
        if (string.IsNullOrWhiteSpace(switchValue))
        {
            return null;
        }

        if (!bool.TryParse(switchValue, out var enabled))
        {
            throw new InvalidOperationException(AmbiguousSwitchMessage);
        }

        if (!enabled)
        {
            return null;
        }

        var proxies = ReadEntries(configuration, KnownProxiesKey).Select(ParseProxy).ToArray();
        var networks = ReadEntries(configuration, KnownNetworksKey).Select(ParseNetwork).ToArray();

        if (proxies.Length == 0 && networks.Length == 0)
        {
            throw new InvalidOperationException(MissingAllowlistMessage);
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
            ForwardLimit = ForwardLimit,
            RequireHeaderSymmetry = true,
        };

        // The framework trusts the loopback proxy out of the box. This host
        // trusts exactly what the deployment named and nothing else, so both
        // default lists are emptied before the allowlist is applied.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in proxies)
        {
            options.KnownProxies.Add(proxy);
        }

        foreach (var network in networks)
        {
            options.KnownIPNetworks.Add(network);
        }

        return options;
    }

    /// <summary>
    /// Reads the framework's switch the way this class reads its own.
    ///
    /// When the framework switch reads true, the web host defaults put their
    /// own forwarded-headers middleware in front of this host's pipeline with
    /// both known lists cleared, so every peer is believed. No allowlist here
    /// can narrow that, and a value the framework would quietly treat as off is
    /// not guessed at either.
    /// </summary>
    /// <exception cref="InvalidOperationException">The switch is not a boolean.</exception>
    internal static bool FrameworkSwitchEnabled(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var frameworkValue = configuration[FrameworkSwitchKey];
        if (string.IsNullOrWhiteSpace(frameworkValue))
        {
            return false;
        }

        return bool.TryParse(frameworkValue, out var frameworkEnabled)
            ? frameworkEnabled
            : throw new InvalidOperationException(FrameworkSwitchAmbiguousMessage);
    }

    private static IReadOnlyList<string> ReadEntries(IConfiguration configuration, string key) =>
        configuration.GetSection(key).GetChildren().Select(child => child.Value ?? string.Empty).ToArray();

    private static IPAddress ParseProxy(string entry)
    {
        EnsureUsableEntry(entry);
        return IPAddress.TryParse(entry.Trim(), out var address)
            ? address
            : throw new InvalidOperationException(InvalidProxyMessage);
    }

    private static IPNetwork ParseNetwork(string entry)
    {
        EnsureUsableEntry(entry);
        var parts = entry.Trim().Split('/');
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var prefix)
            || !int.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var prefixLength))
        {
            throw new InvalidOperationException(InvalidNetworkMessage);
        }

        var maximumLength = prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? 128
            : 32;
        if (prefixLength > maximumLength)
        {
            throw new InvalidOperationException(InvalidNetworkMessage);
        }

        // A zero-length prefix matches every address, which is the wildcard
        // written in a different alphabet.
        if (prefixLength == 0)
        {
            throw new InvalidOperationException(WildcardEntryMessage);
        }

        EnsureNoHostBits(prefix, prefixLength);

        return new IPNetwork(prefix, prefixLength);
    }

    /// <summary>
    /// Refuses a base address that carries bits below its prefix.
    ///
    /// Measurement found this: <c>System.Net.IPNetwork</c> accepts 10.1.2.3/8
    /// without complaint and then matches the whole of 10.0.0.0/8. A deployment
    /// that wrote a host address with a network prefix gets a range far wider
    /// than the text suggests, and gets it silently. Guessing which of the two
    /// was meant is exactly the widening this class exists to prevent, so
    /// neither is guessed.
    /// </summary>
    private static void EnsureNoHostBits(IPAddress baseAddress, int prefixLength)
    {
        var bytes = baseAddress.GetAddressBytes();
        for (var index = 0; index < bytes.Length; index++)
        {
            var networkBits = Math.Clamp(prefixLength - (index * 8), 0, 8);
            var hostMask = (byte)(0xFF >> networkBits);
            if ((bytes[index] & hostMask) != 0)
            {
                throw new InvalidOperationException(InvalidNetworkMessage);
            }
        }
    }

    private static void EnsureUsableEntry(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            throw new InvalidOperationException(EmptyEntryMessage);
        }

        if (entry.Trim() is "*" or "any" or "all")
        {
            throw new InvalidOperationException(WildcardEntryMessage);
        }
    }
}
