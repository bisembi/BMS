using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;

namespace Bms.Api.Security;

public sealed record WindowsAuthenticationConfiguration(bool Enabled)
{
    public const string EnabledKey = "BmsSecurity:WindowsAuthentication:Enabled";

    public static WindowsAuthenticationConfiguration Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = configuration[EnabledKey];
        if (value is null) return new(false);
        if (!bool.TryParse(value, out var enabled))
            throw new InvalidOperationException("Windows authentication requires an explicit boolean setting.");
        if (!enabled) return new(false);
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("The localhost Windows authentication adapter requires Windows.");
        if (bool.TryParse(configuration[DevelopmentActorHeaderBoundaryMiddleware.ConfigurationKey], out var headers) && headers)
            throw new InvalidOperationException("Windows authentication requires development actor headers to be disabled.");
        if (bool.TryParse(configuration["BmsSecurity:ForwardedHeaders:Enabled"], out var forwarding) && forwarding)
            throw new InvalidOperationException("The localhost Windows authentication adapter requires a direct connection.");
        if (!CsrfProtectionConfiguration.Resolve(configuration).RequiresToken)
            throw new InvalidOperationException("Windows authentication requires CSRF token issuance and enforcement.");
        return new(true);
    }

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(this);
        var authentication = services.AddAuthentication();
        if (!Enabled) return;
        services.Configure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = NegotiateDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = NegotiateDefaults.AuthenticationScheme;
        });
        authentication.AddNegotiate(options =>
        {
            options.Events = new NegotiateEvents
            {
                OnAuthenticated = context =>
            {
                var principal = ProjectWindowsPrincipal(context.Principal);
                if (principal is null) context.Fail("A Windows account identity is required.");
                else context.Principal = principal;
                return Task.CompletedTask;
            }
            };
        });
    }

    // Only the OS-authenticated user SID becomes the BMS actor. Windows groups,
    // generic NameIdentifier claims and descriptive imported usernames are not
    // authority to become a BMS employee or administrator.
    internal static ClaimsPrincipal? ProjectWindowsPrincipal(ClaimsPrincipal? source)
    {
        if (!OperatingSystem.IsWindows() || source is null) return null;
        var identities = source.Identities.ToArray();
        if (identities.Length != 1 || identities[0] is not WindowsIdentity windows
            || !windows.IsAuthenticated || windows.User is not { } sid) return null;
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, sid.Value)],
            NegotiateDefaults.AuthenticationScheme));
    }
}
