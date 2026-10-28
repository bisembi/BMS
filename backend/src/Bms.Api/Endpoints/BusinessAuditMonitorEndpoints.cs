using Bms.Api.Problems;
using Bms.Api.Diagnostics;
using Bms.Application.Abstractions;
using Bms.Application.Audit;
using Bms.Application.Common;

namespace Bms.Api.Endpoints;

public static class BusinessAuditMonitorEndpoints
{
    public static IEndpointRouteBuilder MapBusinessAuditMonitorEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/audit-monitor/business-events",
                ListAsync)
            .WithName("ListBusinessAuditEvents")
            .WithTags("Business audit monitor");
        return endpoints;
    }

    internal static async Task<IResult> ListAsync(
        int? skip,
        int? take,
        BusinessAuditMonitorService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            return Results.Ok(await service.ListAsync(
                skip ?? 0,
                take ?? 25,
                cancellationToken));
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (BusinessAuditReadAuthorityUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.BusinessAuditReadAuthority);
            return SafeProblemResults.BusinessAuditUnavailable();
        }
    }
}
