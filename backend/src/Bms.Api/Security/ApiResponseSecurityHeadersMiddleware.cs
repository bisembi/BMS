namespace Bms.Api.Security;

/// <summary>
/// Applies browser-facing defense-in-depth headers to every exact /api/v1
/// response, including responses produced by an earlier authorization failure
/// or by the global exception handler. The boundary does not configure CORS,
/// authentication or transport security.
/// </summary>
public sealed class ApiResponseSecurityHeadersMiddleware(RequestDelegate next)
{
    internal const string ApiPrefix = "/api/v1";
    internal const string ContentSecurityPolicyHeader = "Content-Security-Policy";
    internal const string ContentSecurityPolicy =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
    internal const string ContentTypeOptionsHeader = "X-Content-Type-Options";
    internal const string ContentTypeOptions = "nosniff";
    internal const string FrameOptionsHeader = "X-Frame-Options";
    internal const string FrameOptions = "DENY";
    internal const string ReferrerPolicyHeader = "Referrer-Policy";
    internal const string ReferrerPolicy = "no-referrer";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.Path.StartsWithSegments(ApiPrefix))
        {
            await next(context);
            return;
        }

        Enforce(context.Response);
        await next(context);
    }

    private static void Enforce(HttpResponse response)
    {
        Apply(response.Headers);
        response.OnStarting(static state =>
        {
            Apply(((HttpResponse)state).Headers);
            return Task.CompletedTask;
        }, response);
    }

    internal static void ApplyTo(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Apply(response.Headers);
    }

    private static void Apply(IHeaderDictionary headers)
    {
        headers[ContentSecurityPolicyHeader] = ContentSecurityPolicy;
        headers[ContentTypeOptionsHeader] = ContentTypeOptions;
        headers[FrameOptionsHeader] = FrameOptions;
        headers[ReferrerPolicyHeader] = ReferrerPolicy;
    }
}
