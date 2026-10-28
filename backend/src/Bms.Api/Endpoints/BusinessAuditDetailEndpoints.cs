using Bms.Api.Problems;
using Bms.Api.Diagnostics;
using Bms.Application.Abstractions;
using Bms.Application.Audit;
using Bms.Application.Common;

namespace Bms.Api.Endpoints;

public static class BusinessAuditDetailEndpoints
{
    public static IEndpointRouteBuilder MapBusinessAuditDetailEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/audit-monitor/business-events/{auditId:guid}",
                GetAsync)
            .WithName("GetBusinessAuditEvent")
            .WithTags("Business audit monitor");
        return endpoints;
    }

    internal static async Task<IResult> GetAsync(
        Guid auditId,
        BusinessAuditDetailService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            var result = await service.GetAsync(auditId, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
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
