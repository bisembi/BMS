using System.Globalization;
using Bms.Api.Problems;
using Bms.Api.Security;
using Bms.Application.Budgeting;
using Bms.Application.Common;
using Bms.Domain.Common;

namespace Bms.Api.Endpoints;

public static class CompanyBudgetVersionEndpoints
{
    public static RouteGroupBuilder MapCompanyBudgetVersions(this RouteGroupBuilder group)
    {
        group.MapGet("/company-versions", (HttpRequest r, HttpResponse p, CompanyBudgetVersionService s, CancellationToken t) =>
            HandleAsync(r, p, async () => { Filter(r, "skip", "take"); return Results.Ok(await s.ListAsync(Int(r, "skip", 0), Int(r, "take", 25), t)); }));
        group.MapGet("/company-versions/options", (HttpRequest r, HttpResponse p, CompanyBudgetVersionService s, CancellationToken t) =>
            HandleAsync(r, p, async () => { Filter(r, "skip", "take", "query", "costCenterId"); return Results.Ok(await s.OptionsAsync(Text(r, "query") ?? "", Id(r, "costCenterId"), Int(r, "skip", 0), Int(r, "take", 25), t)); }));
        group.MapGet("/company-versions/{id:guid}", (Guid id, HttpRequest r, HttpResponse p, CompanyBudgetVersionService s, CancellationToken t) =>
            HandleAsync(r, p, async () => { Filter(r); return Results.Ok(await s.ReadAsync(id, t)); }));
        group.MapPost("/company-versions", (CreateCompanyBudgetVersionCommand c, HttpRequest r, HttpResponse p, CompanyBudgetVersionService s, CancellationToken t) =>
            HandleAsync(r, p, async () => { Filter(r); return Results.Json(await s.CreateAsync(c, Key(r), t), statusCode: 201); })).RequireCsrfToken();
        group.MapGet("/company-versions/{id:guid}/history", (Guid id, HttpRequest r, HttpResponse p, CompanyBudgetVersionService s, CancellationToken t) =>
            HandleAsync(r, p, async () => { Filter(r, "skip", "take"); return Results.Ok(await s.HistoryAsync(id, Int(r, "skip", 0), Int(r, "take", 25), t)); }));
        group.MapPost("/company-versions/{id:guid}/revisions", (Guid id, ReviseCompanyBudgetVersionCommand c, HttpRequest r, HttpResponse p, CompanyBudgetVersionService s, CancellationToken t) =>
            HandleAsync(r, p, async () => { Filter(r); return Results.Json(await s.ReviseAsync(id, c, Key(r), t), statusCode: 201); })).RequireCsrfToken();
        group.MapPut("/company-versions/{id:guid}", (Guid id, UpdateCompanyBudgetVersionCommand c, HttpRequest r, HttpResponse p, CompanyBudgetVersionService s, CancellationToken t) =>
            HandleAsync(r, p, async () => { Filter(r); return Results.Ok(await s.UpdateAsync(id, c, Key(r), t)); })).RequireCsrfToken();
        group.MapPost("/company-versions/{id:guid}/activate", (Guid id, ActivateCompanyBudgetVersionCommand c, HttpRequest r, HttpResponse p, CompanyBudgetVersionService s, CancellationToken t) =>
            HandleAsync(r, p, async () => { Filter(r); return Results.Ok(await s.ActivateAsync(id, c, Key(r), t)); })).RequireCsrfToken();
        group.MapPut("/company-versions/{id:guid}/active-composition", (Guid id, UpdateActiveCompanyBudgetCompositionCommand c, HttpRequest r, HttpResponse p, CompanyBudgetVersionService s, CancellationToken t) =>
            HandleAsync(r, p, async () => { Filter(r); return Results.Ok(await s.UpdateActiveCompositionAsync(id, c, Key(r), t)); })).RequireCsrfToken();
        group.MapDelete("/company-versions/{id:guid}", (Guid id, [Microsoft.AspNetCore.Mvc.FromBody] DeleteCompanyBudgetVersionCommand c, HttpRequest r, HttpResponse p, CompanyBudgetVersionService s, CancellationToken t) =>
            HandleAsync(r, p, async () => { Filter(r); return Results.Ok(await s.DeleteAsync(id, c, Key(r), t)); })).RequireCsrfToken();
        return group;
    }
    private static void Filter(HttpRequest r, params string[] keys)
    {
        if (r.Query.Keys.Any(k => !keys.Contains(k)) || r.Query.Any(x => x.Value.Count != 1)) throw new ArgumentException("Invalid query.");
    }
    private static string? Text(HttpRequest r, string key) => r.Query.TryGetValue(key, out var value) ? value[0] : null;
    private static int Int(HttpRequest r, string key, int fallback) => Text(r, key) is { } value
        ? int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : throw new ArgumentException("Invalid page.") : fallback;
    private static Guid? Id(HttpRequest r, string key) => Text(r, key) is { } value
        ? Guid.TryParse(value, out var id) && id != Guid.Empty ? id : throw new ArgumentException("Invalid ID.") : null;
    private static string Key(HttpRequest r) => r.Headers.TryGetValue("Idempotency-Key", out var keys) && keys.Count == 1
        ? keys[0] ?? throw new InvalidIdempotencyKeyException("Missing key.") : throw new InvalidIdempotencyKeyException("Missing key.");
    private static async Task<IResult> HandleAsync(HttpRequest r, HttpResponse p, Func<Task<IResult>> action)
    {
        p.Headers.CacheControl = "no-store";
        try { return await action(); }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (CompanyBudgetVersionNotFoundException) { return Results.NotFound(); }
        catch (CompanyBudgetVersionConflictException) { return Results.Conflict(new { message = "Версия изменилась. Обновите данные." }); }
        catch (IdempotencyConflictException) { return Results.Conflict(); }
        catch (InvalidIdempotencyKeyException) { return SafeProblemResults.InvalidRequest(); }
        catch (DomainRuleViolationException) { return SafeProblemResults.InvalidRequest(); }
        catch (ArgumentException) { return SafeProblemResults.InvalidRequest(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Results.Problem(statusCode: 503, detail: "Company budget versions are temporarily unavailable."); }
    }
}
