using System.Globalization;
using Bms.Api.Diagnostics;
using Bms.Api.Problems;
using Bms.Application.Abstractions;
using Bms.Application.Accruals.Notifications;
using Bms.Application.Common;

namespace Bms.Api.Endpoints;

public static class AccrualNotificationAdministrationEndpoints
{
    public static IEndpointRouteBuilder MapAccrualNotificationAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/administration/accrual-notifications", QueueAsync)
            .WithName("ListAdministrativeAccrualNotifications").WithTags("Notification administration");
        endpoints.MapGet("/api/v1/administration/accrual-notifications/issues", IssuesAsync)
            .WithName("ListAccrualNotificationConfigurationIssues").WithTags("Notification administration");
        return endpoints;
    }

    internal static async Task<IResult> QueueAsync(HttpRequest request, HttpResponse response,
        ICurrentActor actor, AccrualNotificationAdministrationService service, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            AccrualNotificationAdministrationService.RequireFinancialReader(actor);
            var (skip, take) = ParseQuery(request);
            return Results.Ok(await service.ListQueueAsync(skip, take, cancellationToken));
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (InvalidAccrualNotificationQueryException)
        {
            ApiFailureLog.InvalidRequest(response.HttpContext, ApiFailureOperation.AccrualNotificationAdministrationQueue);
            return SafeProblemResults.InvalidRequest();
        }
        catch (OperationCanceledException) { throw; }
        catch (AccrualNotificationReadUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(response.HttpContext, ApiFailureOperation.AccrualNotificationAdministrationQueue);
            return SafeProblemResults.AccrualNotificationsUnavailable();
        }
    }

    internal static async Task<IResult> IssuesAsync(HttpRequest request, HttpResponse response,
        ICurrentActor actor, AccrualNotificationAdministrationService service, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            AccrualNotificationAdministrationService.RequireFinancialReader(actor);
            var (skip, take) = ParseQuery(request);
            return Results.Ok(await service.ListIssuesAsync(skip, take, cancellationToken));
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (InvalidAccrualNotificationQueryException)
        {
            ApiFailureLog.InvalidRequest(response.HttpContext, ApiFailureOperation.AccrualNotificationAdministrationIssues);
            return SafeProblemResults.InvalidRequest();
        }
        catch (OperationCanceledException) { throw; }
        catch (AccrualNotificationReadUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(response.HttpContext, ApiFailureOperation.AccrualNotificationAdministrationIssues);
            return SafeProblemResults.AccrualNotificationsUnavailable();
        }
    }

    private static (int Skip, int Take) ParseQuery(HttpRequest request)
    {
        if (request.Query.Keys.Any(key => !string.Equals(key, "skip", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(key, "take", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidAccrualNotificationQueryException();
        }
        return (Parse(request, "skip", 0), Parse(request, "take", 25));
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
