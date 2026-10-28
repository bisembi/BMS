namespace Bms.Api.Security;

/// <summary>
/// The two independent decisions a deployment makes about antiforgery.
///
/// The mechanism is always present: antiforgery is registered and every
/// mutating route carries the validating filter. What a deployment chooses is
/// whether tokens are handed out and, separately, whether they are demanded.
/// Keeping the two apart is what makes a staged rollout possible: hand tokens
/// out first, let clients pick them up, and only then start refusing requests
/// that arrive without one.
/// </summary>
/// <param name="IssuesToken">
/// Whether the session contract carries a request token and sets the
/// antiforgery cookie. <c>false</c> is the default and keeps the session
/// payload exactly as it was before the mechanism existed.
/// </param>
/// <param name="RequiresToken">
/// Whether the filter refuses a mutating request that carries no valid token.
/// <c>false</c> is the default. It cannot be <c>true</c> while
/// <paramref name="IssuesToken"/> is <c>false</c>: demanding something the
/// host never hands out would refuse every write, and that is a configuration
/// mistake rather than a policy.
/// </param>
public sealed record CsrfProtectionSettings(bool IssuesToken, bool RequiresToken)
{
    /// <summary>The state a deployment gets when it configures nothing.</summary>
    public static CsrfProtectionSettings Disabled { get; } = new(false, false);

    /// <summary>Tokens are handed out, nothing is demanded yet.</summary>
    public static CsrfProtectionSettings IssueOnly { get; } = new(true, false);

    /// <summary>Tokens are handed out and demanded.</summary>
    public static CsrfProtectionSettings Enforcing { get; } = new(true, true);
}
