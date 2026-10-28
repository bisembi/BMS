using Bms.Api.Problems;
using Bms.Api.Diagnostics;
using Bms.Application.Audit;
using Bms.Application.Common;
using Bms.Application.Abstractions;

namespace Bms.Api.Endpoints;

public static class BusinessAuditSearchEndpoints
{
    public static IEndpointRouteBuilder MapBusinessAuditSearchEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/audit-monitor/business-events/search",
                SearchAsync)
            .WithName("SearchBusinessAuditEvents")
            .WithTags("Business audit monitor");
        return endpoints;
    }

    internal static async Task<IResult> SearchAsync(
        string? actorExternalId,
        string? objectType,
        string? objectId,
        string? action,
        string? correlationId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? skip,
        int? take,
        BusinessAuditSearchService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            return Results.Ok(await service.SearchAsync(
                new BusinessAuditSearchFilter(
                    actorExternalId,
                    objectType,
                    objectId,
                    action,
                    correlationId,
                    fromUtc,
                    toUtc),
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
        catch (ArgumentException)
        {
            ApiFailureLog.InvalidRequest(
                response.HttpContext,
                ApiFailureOperation.BusinessAuditSearch);
            return SafeProblemResults.InvalidRequest();
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
