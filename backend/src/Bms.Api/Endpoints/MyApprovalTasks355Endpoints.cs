using Bms.Api.Problems;
using Bms.Application.Common;
using Bms.Application.Tasks;
using Bms.Domain.Common;

namespace Bms.Api.Endpoints;

public static class MyApprovalTasks355Endpoints
{
    public static IEndpointRouteBuilder MapMyApprovalTasks355(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/my-tasks", ReadAsync).WithName("MyApprovalTasks355").WithTags("Tasks");
        endpoints.MapGet("/api/v1/notifications/budget-adjustments", ReadAdjustmentsAsync).WithName("BudgetAdjustmentNotifications448").WithTags("Notifications");
        return endpoints;
    }

    private static Task<IResult> ReadAsync(HttpRequest request, HttpResponse response,
        MyApprovalTasks355 service, CancellationToken token) => ReadCoreAsync(request, response, service, false, token);
    private static Task<IResult> ReadAdjustmentsAsync(HttpRequest request, HttpResponse response,
        MyApprovalTasks355 service, CancellationToken token) => ReadCoreAsync(request, response, service, true, token);
    private static async Task<IResult> ReadCoreAsync(HttpRequest request, HttpResponse response,
        MyApprovalTasks355 service, bool adjustmentsOnly, CancellationToken token)
    {
        response.Headers.CacheControl = "no-store";
        if (request.Query.Any(pair => pair.Key is not ("skip" or "take") || pair.Value.Count != 1))
            return SafeProblemResults.InvalidRequest();
        var skip = 0; var take = 50;
        if ((request.Query.ContainsKey("skip") && !int.TryParse(request.Query["skip"], out skip))
            || (request.Query.ContainsKey("take") && !int.TryParse(request.Query["take"], out take)))
            return SafeProblemResults.InvalidRequest();
        try { return Results.Ok(adjustmentsOnly ? await service.ListAdjustments448Async(skip, take, token) : await service.ListAsync(skip, take, token)); }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (DomainRuleViolationException) { return SafeProblemResults.InvalidRequest(); }
    }
}
