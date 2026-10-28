using Microsoft.Extensions.Hosting;

namespace Bms.Api.Security;

/// <summary>
/// Reads the two settings that decide whether antiforgery tokens are issued
/// and whether they are required, and refuses to start the host when the pair
/// is written in a way nobody can read with confidence.
///
/// Three properties matter and are tested separately. Absent settings keep the
/// behaviour a deployment had before this mechanism existed. An unreadable
/// setting is not treated as "off": a deployment that meant to switch the
/// protection on and mistyped the value must find out at startup, not by
/// serving unprotected traffic. And requiring a token the host never issues is
/// refused outright rather than started, because it would turn every write on
/// every mutating route into a refusal.
/// </summary>
public static class CsrfProtectionConfiguration
{
    internal const string IssuesTokenKey = "BmsSecurity:Csrf:IssuesToken";
    internal const string RequiresTokenKey = "BmsSecurity:Csrf:RequiresToken";
    internal const string AllowInsecureHttpCookieKey =
        "BmsSecurity:Csrf:AllowInsecureHttpCookie";

    internal const string AmbiguousIssueMessage =
        "BmsSecurity:Csrf:IssuesToken must be true or false.";
    internal const string AmbiguousSwitchMessage =
        "BmsSecurity:Csrf:RequiresToken must be true or false.";
    internal const string RequiresWithoutIssueMessage =
        "BmsSecurity:Csrf:RequiresToken cannot be true while " +
        "BmsSecurity:Csrf:IssuesToken is false: the host would demand a token it never issues.";
    internal const string AmbiguousInsecureHttpCookieMessage =
        "BmsSecurity:Csrf:AllowInsecureHttpCookie must be true or false.";
    internal const string InsecureCookieOutsideStagingMessage =
        "BmsSecurity:Csrf:AllowInsecureHttpCookie is allowed only in the Staging environment.";

    /// <summary>
    /// The header the client sends the request token in. Fixed rather than
    /// configurable: a name that can differ between deployments is one more
    /// thing that can silently disagree with the client. Header names are
    /// compared without regard to case, so this spelling is the canonical one
    /// rather than a constraint on the wire.
    /// </summary>
    public const string HeaderName = "X-CSRF-Token";

    /// <summary>
    /// The name of the cookie holding the antiforgery cookie token.
    ///
    /// Deliberately not a <c>__Host-</c> name: that prefix would bind the
    /// mechanism to a published-origin and TLS story this package is not
    /// allowed to decide. The cookie is issued only while tokens are issued,
    /// so a deployment that has not switched issuance on never receives it.
    /// </summary>
    public const string CookieName = "bms-csrf";

    /// <summary>
    /// Returns the state named by configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Either setting is present but is not a boolean, or the pair asks for a
    /// token to be required without being issued.
    /// </exception>
    public static CsrfProtectionSettings Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var issuesToken = ReadSwitch(configuration, IssuesTokenKey, AmbiguousIssueMessage);
        var requiresToken = ReadSwitch(configuration, RequiresTokenKey, AmbiguousSwitchMessage);

        if (requiresToken && !issuesToken)
        {
            throw new InvalidOperationException(RequiresWithoutIssueMessage);
        }

        return new CsrfProtectionSettings(issuesToken, requiresToken);
    }

    /// <summary>
    /// Resolves the temporary HTTP-only staging exception for the antiforgery
    /// cookie. The token pair and endpoint validation remain enabled; only the
    /// cookie's Secure attribute is relaxed so an internal HTTP staging proxy
    /// can carry the pair until the public TLS binding is installed.
    /// </summary>
    public static bool ResolveAllowInsecureHttpCookie(
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        var allow = ReadSwitch(
            configuration,
            AllowInsecureHttpCookieKey,
            AmbiguousInsecureHttpCookieMessage);
        if (allow && !string.Equals(
                environmentName,
                Environments.Staging,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(InsecureCookieOutsideStagingMessage);
        }

        return allow;
    }

    private static bool ReadSwitch(IConfiguration configuration, string key, string ambiguousMessage)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return bool.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException(ambiguousMessage);
    }
}
