using System.Text.Json.Serialization;
using Bms.Api.Problems;
using Bms.Api.Security;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;

namespace Bms.Api.Endpoints;

public static class StaffSubstitution471Endpoints
{
    public static IEndpointRouteBuilder MapStaffSubstitution471(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/staff-substitutions").WithTags("Staff");
        group.MapGet("/options", (HttpContext c, ICurrentActor actor, IStaffSubstitutionStore471 store, CancellationToken t) =>
            Handle(c, () => store.OptionsAsync(ActorIdentity.RequireBusinessDataAccess(actor), t)));
        group.MapGet("/news", (HttpContext c, ICurrentActor actor, IStaffSubstitutionStore471 store, CancellationToken t) =>
            Handle(c, () => store.NewsAsync(ActorIdentity.RequireBusinessDataAccess(actor), t)));
        group.MapGet("/pending", (HttpContext c, ICurrentActor actor, IStaffSubstitutionStore471 store, CancellationToken t) =>
            Handle(c, () => store.PendingAsync(ActorIdentity.RequireBusinessDataAccess(actor), t)));
        group.MapGet("", (int? skip, int? take, HttpContext c, ICurrentActor actor, IStaffSubstitutionStore471 store, CancellationToken t) =>
            Handle(c, () => store.ListAsync(ActorIdentity.RequireBusinessDataAccess(actor), skip ?? 0, take ?? 50, t)));
        group.MapGet("/{id:guid}", (Guid id, HttpContext c, ICurrentActor actor, IStaffSubstitutionStore471 store, CancellationToken t) =>
            Handle(c, () => store.GetAsync(ActorIdentity.RequireBusinessDataAccess(actor), id, t)));
        group.MapGet("/{id:guid}/actions", (Guid id, HttpContext c, ICurrentActor actor, IStaffSubstitutionStore471 store, CancellationToken t) =>
            Handle(c, () => store.ActionsAsync(ActorIdentity.RequireBusinessDataAccess(actor), id, t)));
        group.MapPost("/{id:guid}/{action}", (Guid id, string action, StaffCommand471 body, HttpContext c,
            ICurrentActor actor, IStaffSubstitutionStore471 store, CancellationToken t) =>
            Handle(c, () => store.ExecuteAsync(ActorIdentity.RequireBusinessDataAccess(actor), id, body.CommandId,
                body.ExpectedVersion, action, body.Input, t)))
            .RequireCsrfToken().WithMetadata(new StaffSelfService471());
        return endpoints;
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record StaffCommand471(Guid CommandId, int ExpectedVersion, StaffSubstitutionInput471? Input = null);
    private static async Task<IResult> Handle<T>(HttpContext context, Func<Task<T>> work)
    {
        context.Response.Headers.CacheControl = "no-store";
        try { var value = await work(); return value is null ? Results.NotFound() : Results.Ok(value); }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (StaffSubstitutionConflict471) { return Results.Conflict(new { message = "Документ или права изменились. Обновите документ перед повторным действием." }); }
        catch (ArgumentException) { return SafeProblemResults.InvalidRequest(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (!Bms.Api.Diagnostics.ApiProcessFatalExceptionClassifier.IsFatal(e))
        { context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("StaffSubstitution471").LogWarning("Staff substitution operation unavailable.");
            return Results.Problem(statusCode:503, title:"Замещения временно недоступны. Проверьте состояние документа перед повтором."); }
    }
}

/// <summary>Own HR document only; authorization never permits financial edits.</summary>
public sealed class StaffSelfService471;
