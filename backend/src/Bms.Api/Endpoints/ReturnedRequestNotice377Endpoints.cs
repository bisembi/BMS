using Bms.Api.Problems;
using Bms.Application.Common;
using Bms.Application.Tasks;
using Bms.Domain.Common;

namespace Bms.Api.Endpoints;

public static class ReturnedRequestNotice377Endpoints
{
    public static IEndpointRouteBuilder MapReturnedRequestNotices377(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/notifications/returned-requests",ReadAsync).WithName("OwnReturnedRequestNotices377").WithTags("Notifications");
        return endpoints;
    }
    private static async Task<IResult> ReadAsync(HttpRequest request,HttpResponse response,ReturnedRequestNotices377 service,
        ILogger<ReturnedRequestNotices377> logger,CancellationToken token)
    {
        response.Headers.CacheControl="no-store";
        if(request.Query.Any(p=>p.Key is not("skip" or "take")||p.Value.Count!=1))return SafeProblemResults.InvalidRequest();
        var skip=0;var take=25;
        if((request.Query.ContainsKey("skip")&&!int.TryParse(request.Query["skip"],out skip))
            ||(request.Query.ContainsKey("take")&&!int.TryParse(request.Query["take"],out take)))return SafeProblemResults.InvalidRequest();
        try{return Results.Ok(await service.ListAsync(skip,take,token));}
        catch(ActorNotAuthenticatedException){return SafeProblemResults.AuthenticationRequired();}
        catch(ActorForbiddenException){return SafeProblemResults.Forbidden();}
        catch(DomainRuleViolationException){return SafeProblemResults.InvalidRequest();}
        catch(OperationCanceledException){throw;}
        catch(Exception e)when(!Bms.Api.Diagnostics.ApiProcessFatalExceptionClassifier.IsFatal(e))
        {logger.LogWarning("Returned request notification read unavailable.");return Results.Problem(statusCode:503,title:"Уведомления временно недоступны.");}
    }
}
