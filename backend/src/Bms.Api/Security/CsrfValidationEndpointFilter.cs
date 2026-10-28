using Bms.Api.Problems;
using Microsoft.AspNetCore.Antiforgery;

namespace Bms.Api.Security;

/// <summary>
/// Validates the antiforgery token of one mutating route before its handler
/// runs.
///
/// This is deliberately an explicit per-route filter rather than the framework
/// middleware. The middleware validates POST, PUT and PATCH only — <c>DELETE</c>
/// is outside its method list — and its automatic validation depends on form
/// parameter binding, which the multipart attachment route does not use
/// because it reads the form by hand. A filter named on the route covers both.
///
/// Nothing about the token reaches the response or a log. The framework's
/// validation exception carries text that distinguishes a missing token from
/// one meant for a different user; that distinction is useful to an attacker
/// and is dropped here in favour of one fixed refusal.
/// </summary>
public sealed class CsrfValidationEndpointFilter(
    IAntiforgery antiforgery,
    CsrfProtectionSettings settings) : IEndpointFilter
{
    private readonly IAntiforgery antiforgery =
        antiforgery ?? throw new ArgumentNullException(nameof(antiforgery));
    private readonly CsrfProtectionSettings settings =
        settings ?? throw new ArgumentNullException(nameof(settings));

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (!settings.RequiresToken)
        {
            return await next(context);
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return SafeProblemResults.CsrfTokenInvalid();
        }

        return await next(context);
    }
}
