using Microsoft.AspNetCore.Authentication;

namespace Bms.Api.Security;

public static class ProductionAuthenticationStartupGuard
{
    internal const string MissingSchemeMessage =
        "A default production authentication scheme is required outside Development and Testing.";

    public static async Task EnsureConfiguredAsync(
        IHostEnvironment environment,
        IAuthenticationSchemeProvider schemeProvider)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(schemeProvider);

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            return;
        }

        var defaultScheme = await schemeProvider.GetDefaultAuthenticateSchemeAsync();
        if (defaultScheme is null || string.IsNullOrWhiteSpace(defaultScheme.Name))
        {
            throw new InvalidOperationException(MissingSchemeMessage);
        }
    }
}
