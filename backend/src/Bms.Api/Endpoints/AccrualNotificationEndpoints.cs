using System.Globalization;
using Bms.Api.Diagnostics;
using Bms.Api.Problems;
using Bms.Application.Abstractions;
using Bms.Application.Accruals.Notifications;
using Bms.Application.Common;

namespace Bms.Api.Endpoints;

public static class AccrualNotificationEndpoints
{
    public static IEndpointRouteBuilder MapAccrualNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/notifications/accruals", ListAsync)
            .WithName("ListOwnAccrualNotifications").WithTags("Notifications");
        endpoints.MapGet("/api/v1/integration-monitor/accrual-notifications", MonitorAsync)
            .WithName("GetAccrualNotificationMonitor").WithTags("Integration monitor");
        return endpoints;
    }

    private static async Task<IResult> ListAsync(HttpRequest request, HttpResponse response,
        ICurrentActor actor, AccrualNotificationReadService service, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            if (request.Query.Keys.Any(key => !string.Equals(key, "skip", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(key, "take", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidAccrualNotificationQueryException();
            }
            return Results.Ok(await service.ListAsync(Parse(request, "skip", 0),
                Parse(request, "take", 25), cancellationToken));
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (InvalidAccrualNotificationQueryException)
        {
            ApiFailureLog.InvalidRequest(response.HttpContext, ApiFailureOperation.AccrualNotificationList);
            return SafeProblemResults.InvalidRequest();
        }
        catch (OperationCanceledException) { throw; }
        catch (AccrualNotificationReadUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(response.HttpContext, ApiFailureOperation.AccrualNotificationList);
            return SafeProblemResults.AccrualNotificationsUnavailable();
        }
    }

    private static async Task<IResult> MonitorAsync(HttpResponse response,
        AccrualNotificationReadService service, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try { return Results.Ok(await service.GetMonitorAsync(cancellationToken)); }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (OperationCanceledException) { throw; }
        catch (AccrualNotificationReadUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(response.HttpContext, ApiFailureOperation.AccrualNotificationMonitor);
            return SafeProblemResults.AccrualNotificationsUnavailable();
        }
    }

    private static int Parse(HttpRequest request, string key, int fallback)
    {
        if (!request.Query.TryGetValue(key, out var values)) { return fallback; }
        if (values.Count != 1 || !int.TryParse(values[0], NumberStyles.None,
            CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidAccrualNotificationQueryException();
        }
        return value;
    }
}
