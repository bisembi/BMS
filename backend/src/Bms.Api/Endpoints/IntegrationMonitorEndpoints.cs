using Bms.Api.Problems;
using Bms.Api.Diagnostics;
using Bms.Application.Common;
using Bms.Application.Integration;

namespace Bms.Api.Endpoints;

public static class IntegrationMonitorEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationMonitorEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/integration-monitor")
            .WithTags("Integration monitor");
        group.MapGet("/outbox", ListOutboxAsync);
        group.MapGet("/outbox-summary", GetOutboxBacklogSummaryAsync);
        group.MapGet("/database-readiness", GetDatabaseReadinessAsync);
        group.MapGet("/reference-projections", GetReferenceProjectionInventoryAsync);
        return endpoints;
    }

    internal static async Task<IResult> GetOutboxBacklogSummaryAsync(
        OutboxBacklogSummaryService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            return Results.Ok(await service.GetAsync(cancellationToken));
        }
        catch (ActorNotAuthenticatedException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.");
        }
        catch (ActorForbiddenException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Outbox backlog summary access is forbidden.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.IntegrationOutboxBacklogSummary);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Outbox backlog summary is unavailable.");
        }
    }

    internal static async Task<IResult> GetReferenceProjectionInventoryAsync(
        ReferenceProjectionInventoryService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            return Results.Ok(await service.GetAsync(cancellationToken));
        }
        catch (ActorNotAuthenticatedException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.");
        }
        catch (ActorForbiddenException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Reference projection inventory access is forbidden.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.IntegrationReferenceProjectionInventory);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Reference projection inventory is unavailable.");
        }
    }

    internal static async Task<IResult> GetDatabaseReadinessAsync(
        DatabaseReadinessMonitorService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            return Results.Ok(await service.GetAsync(cancellationToken));
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.IntegrationDatabaseReadiness);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Database readiness check is unavailable.");
        }
    }

    internal static async Task<IResult> ListOutboxAsync(
        int? skip,
        int? take,
        OutboxMonitorService service,
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
    }
}
