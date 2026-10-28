namespace Bms.Api.Security;

/// <summary>
/// The single way a mutating route declares that it is behind the antiforgery
/// filter. Both the filter and the marker are attached together, so the
/// coverage suite cannot be satisfied by a route that only looks protected.
/// </summary>
public static class CsrfEndpointConventions
{
    /// <summary>
    /// Puts <see cref="CsrfValidationEndpointFilter"/> in front of the handler
    /// and records <see cref="CsrfProtectedRouteMetadata"/> on the endpoint.
    /// </summary>
    public static RouteHandlerBuilder RequireCsrfToken(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .AddEndpointFilter<CsrfValidationEndpointFilter>()
            .WithMetadata(CsrfProtectedRouteMetadata.Instance);
    }
}
