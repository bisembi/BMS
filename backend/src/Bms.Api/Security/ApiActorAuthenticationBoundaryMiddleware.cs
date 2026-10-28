using Bms.Api.Diagnostics;
using Bms.Application.Abstractions;

namespace Bms.Api.Security;

public sealed class ApiActorAuthenticationBoundaryMiddleware(RequestDelegate next)
{
    internal const string ApiPrefix = "/api/v1";
    internal const string ProblemCode = "AuthenticationRequired";

    public async Task InvokeAsync(HttpContext context, ICurrentActor currentActor)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(currentActor);

        if (!context.Request.Path.StartsWithSegments(ApiPrefix))
        {
            await next(context);
            return;
        }

        EnforceNoStore(context.Response);
        if (currentActor.IsAuthenticated)
        {
            await next(context);
            return;
        }

        await Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Authentication is required.",
            detail: "A canonical authenticated actor is required for this API.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = ProblemCode,
                ["traceId"] = ApiTraceIdentifier.Canonicalize(context.TraceIdentifier)
            }).ExecuteAsync(context);
    }

    private static void EnforceNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.OnStarting(static state =>
        {
            var currentResponse = (HttpResponse)state;
            currentResponse.Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        }, response);
    }
}
