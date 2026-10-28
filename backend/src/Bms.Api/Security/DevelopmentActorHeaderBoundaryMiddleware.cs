using Bms.Api.Diagnostics;

namespace Bms.Api.Security;

public sealed class DevelopmentActorHeaderBoundaryMiddleware(
    RequestDelegate next,
    IWebHostEnvironment environment,
    IConfiguration configuration)
{
    internal const string ActorHeader = "X-Bms-Actor-Id";
    internal const string RolesHeader = "X-Bms-Actor-Roles";
    internal const string ProblemCode = "DevelopmentActorHeadersForbidden";
    internal const string ConfigurationKey = "BmsSecurity:AllowDevelopmentActorHeaders";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (AllowsHeaders(environment, configuration)
            || (!context.Request.Headers.ContainsKey(ActorHeader)
                && !context.Request.Headers.ContainsKey(RolesHeader)))
        {
            await next(context);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        await Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Development actor headers are not allowed.",
            detail: "Development actor headers require Development or Testing and explicit server opt-in.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = ProblemCode,
                ["traceId"] = ApiTraceIdentifier.Canonicalize(context.TraceIdentifier)
            }).ExecuteAsync(context);
    }

    internal static bool AllowsHeaders(
        IHostEnvironment hostEnvironment,
        IConfiguration applicationConfiguration)
    {
        var isLocalEnvironment =
            hostEnvironment.IsDevelopment() || hostEnvironment.IsEnvironment("Testing");
        return isLocalEnvironment
            && bool.TryParse(applicationConfiguration[ConfigurationKey], out var explicitlyAllowed)
            && explicitlyAllowed;
    }
}
