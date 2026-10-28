using System.Text.RegularExpressions;

namespace Bms.Infrastructure;

// Trusted process-level installation settings. None can be supplied through
// request headers, query parameters, imported payloads or employee permissions.
internal static partial class BmsInstallationSettings
{
    public static string ReplicaDatabase { get; } = ReadReplica();

    private static string ReadReplica()
    {
        var name = Environment.GetEnvironmentVariable("BMS_REPLICA_DATABASE") ?? "BmsReplica";
        if (!SqlIdentifier().IsMatch(name))
            throw new InvalidOperationException("A canonical replica database identifier is required.");
        return name;
    }

    public static Uri PublicBaseUri
    {
        get
        {
            var text = Environment.GetEnvironmentVariable("BMS_PUBLIC_BASE_URL");
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "https"
                || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/"
                || uri.Query.Length != 0 || uri.Fragment.Length != 0
                || uri.HostNameType != UriHostNameType.Dns)
                throw new InvalidOperationException("A canonical HTTPS BMS origin is required for mail.");
            return uri;
        }
    }

    public static string MailDomain
    {
        get
        {
            var domain = Environment.GetEnvironmentVariable("BMS_MAIL_DOMAIN");
            if (domain is null || domain.Length > 253 || !DnsDomain().IsMatch(domain))
                throw new InvalidOperationException("An explicit corporate mail domain is required.");
            return domain;
        }
    }

    public static string MailSettingsPath
    {
        get
        {
            var path = Environment.GetEnvironmentVariable("BMS_MAIL_SETTINGS_PATH");
            if (path is null || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)
                || !string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An explicit canonical local protected mail path is required.");
            return path;
        }
    }

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]{0,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SqlIdentifier();
    [GeneratedRegex(@"\A[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex DnsDomain();
}
