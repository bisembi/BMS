using Bms.Api.Diagnostics;

namespace Bms.Api.Problems;

/// <summary>
/// Applies the server-owned correlation boundary to every ProblemDetails body.
/// </summary>
internal static class ApiProblemDetails
{
    internal const string TraceIdExtensionName = "traceId";
    internal const string UnavailableTraceIdentifier = ApiTraceIdentifier.Unavailable;

    internal static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddProblemDetails(Configure);
    }

    private static void Configure(ProblemDetailsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Extensions[TraceIdExtensionName] =
                ApiTraceIdentifier.Canonicalize(context.HttpContext.TraceIdentifier);
        };
    }
}
