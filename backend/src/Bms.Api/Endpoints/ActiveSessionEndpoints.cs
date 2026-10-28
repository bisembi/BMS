using Bms.Api.Problems;
using Bms.Api.Security;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Endpoints;

public static class ActiveSessionEndpoints
{
    public const string PresencePath = "/api/v1/session/presence";
    public static IEndpointRouteBuilder MapActiveSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(PresencePath, TouchAsync).RequireCsrfToken()
            .WithMetadata(new AuditorNonBusinessPost()).WithTags("Session");
        endpoints.MapGet("/api/v1/administration/active-sessions", List).WithTags("Administration");
        endpoints.MapGet("/api/v1/administration/usage-log", UsageLogAsync).WithTags("Administration");
        endpoints.MapGet("/api/v1/administration/usage-log/daily", UsageDailyAsync).WithTags("Administration");
        return endpoints;
    }

    public sealed record PresenceRequest(Guid TabId, long Sequence, bool Closed = false);

    internal static async Task<IResult> TouchAsync(PresenceRequest request, ICurrentActor actor,
        ActiveSessionRegistry registry, HttpResponse response, CancellationToken cancellationToken,
        [FromServices] IEmployeeActorMappingReader? mapping = null,
        [FromServices] IUsageSessionStore? usage = null)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            var identity = ActorIdentity.Require(actor);
            if (request.TabId == Guid.Empty || request.Sequence is < 1 or > 9_007_199_254_740_991)
                return SafeProblemResults.InvalidRequest();
            var name = await SessionEndpoints.TryReadActorDisplayNameAsync(actor, identity, mapping, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Await durable storage: a failed write must not masquerade as a logged
            // event, and a stale pulse must not revive an explicitly closed tab.
            if (usage is not null && !await usage.TouchAsync(identity, request.TabId, request.Sequence,
                name ?? "Пользователь BMS", request.Closed, cancellationToken)) return Results.NoContent();
            if (!request.Closed && !registry.Touch(identity, request.TabId, name ?? "Пользователь BMS", actor.ActiveRoleCode, request.Sequence))
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            if (request.Closed) registry.Close(identity, request.TabId, request.Sequence);
            return Results.NoContent();
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (UsageSessionCapacityException) { return Results.StatusCode(StatusCodes.Status429TooManyRequests); }
    }

    internal static async Task<IResult> UsageLogAsync(string? search, DateOnly? from, DateOnly? through,
        int? skip, int? take, ICurrentActor actor, IUsageSessionStore usage, HttpResponse response, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            AdministrationReportReadAccess.Require(actor);
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5));
            return Results.Ok(await usage.ReadAsync(search, from ?? today.AddDays(-6), through ?? today,
                skip ?? 0, take ?? 50, cancellationToken));
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (ArgumentException) { return SafeProblemResults.InvalidRequest(); }
    }

    internal static async Task<IResult> UsageDailyAsync(string? search, DateOnly? from, DateOnly? through,
        int? skip, int? take, ICurrentActor actor, IUsageSessionStore usage, HttpResponse response, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            AdministrationReportReadAccess.Require(actor);
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5));
            return Results.Ok(await usage.ReadDailyAsync(search, from ?? today.AddDays(-6), through ?? today,
                skip ?? 0, take ?? 50, cancellationToken));
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (ArgumentException) { return SafeProblemResults.InvalidRequest(); }
    }

    internal static IResult List(string? search, int? skip, int? take, ICurrentActor actor,
        ActiveSessionRegistry registry, HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            AdministrationReportReadAccess.Require(actor);
            return Results.Ok(registry.Read(search, skip ?? 0, take ?? 50));
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (ArgumentException) { return SafeProblemResults.InvalidRequest(); }
    }
}
