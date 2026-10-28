using Bms.Api.Problems;
using Bms.Application.Abstractions;
using Bms.Domain.Workflow;

namespace Bms.Api.Security;

/// <summary>Does not grant reads. Denies business writes independently of page/service permissions.</summary>
public sealed class AuditorReadOnlyBoundaryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ICurrentActor actor)
    {
        if (context.Request.Path.StartsWithSegments("/api")
            && actor.ActiveRoleCode == ExpenseRequestWorkflowCodes.AuditorRoleCode
            && context.Request.Method is not ("GET" or "HEAD" or "OPTIONS")
            && !(HttpMethods.IsPost(context.Request.Method)
                && (context.GetEndpoint()?.Metadata.GetMetadata<AuditorNonBusinessPost>() is not null
                    || context.GetEndpoint()?.Metadata.GetMetadata<Bms.Api.Endpoints.StaffSelfService471>() is not null)))
        {
            context.Response.Headers.CacheControl = "no-store";
            await SafeProblemResults.Forbidden().ExecuteAsync(context);
            return;
        }
        await next(context);
    }
}

/// <summary>Only persisted-file viewing and bounded session/diagnostic telemetry, never business edits.</summary>
public sealed class AuditorNonBusinessPost;
