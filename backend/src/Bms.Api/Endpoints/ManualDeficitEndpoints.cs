using System.Globalization;
using Bms.Api.Diagnostics;
using Bms.Api.Problems;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Application.OverpaymentDeficits;
using Bms.Domain.Common;
using Microsoft.AspNetCore.Mvc;

using Bms.Api.Security;
namespace Bms.Api.Endpoints;

public static class ManualDeficitEndpoints
{
    public static IEndpointRouteBuilder MapManualDeficitEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var deficits = endpoints.MapGroup("/api/v1/overpayment-deficits").WithTags("Overpayment deficits");
        deficits.MapGet("", ListAsync).WithName("ListOverpaymentDeficits");
        deficits.MapGet("/{id:guid}", ReadAsync).WithName("GetOverpaymentDeficit");
        deficits.MapPost("/{id:guid}/corrections", CreateAsync).RequireCsrfToken()
            .WithName("CreateManualDeficitCorrection");
        deficits.MapGet("/{id:guid}/corrections/idempotency", ResolveCreateAsync)
            .WithName("ResolveCreateManualDeficitCorrection");
        var corrections = endpoints.MapGroup("/api/v1/manual-deficit-corrections").WithTags("Overpayment deficits");
        corrections.MapPost("/{id:guid}/approve", ApproveAsync).RequireCsrfToken()
            .WithName("ApproveManualDeficitCorrection");
        corrections.MapPost("/{id:guid}/reject", RejectAsync).RequireCsrfToken()
            .WithName("RejectManualDeficitCorrection");
        corrections.MapGet("/{id:guid}/{operation}/idempotency", ResolveDecisionAsync)
            .WithName("ResolveManualDeficitCorrectionDecision");
        return endpoints;
    }

    internal static async Task<IResult> ListAsync(HttpRequest request, HttpResponse response,
        ICurrentActor actor, ManualDeficitReadService service, CancellationToken cancellationToken) =>
        await RespondAsync(request, response, actor, ApiFailureOperation.ManualDeficitList, async token =>
        {
            RequireQuery(request, "organizationId", "includeSettled", "onlyPending", "skip", "take");
            var organization = Value(request, "organizationId");
            Guid? organizationId = null;
            if (organization is not null)
                organizationId = Guid.TryParseExact(organization, "D", out var id) && id != Guid.Empty
                    ? id : throw new InvalidManualDeficitQueryException();
            return Results.Ok(await service.ListAsync(new(organizationId,
                Boolean(request, "includeSettled"), Boolean(request, "onlyPending"),
                Integer(request, "skip", 0), Integer(request, "take", 25)), token));
        }, cancellationToken);

    internal static async Task<IResult> ReadAsync(Guid id, HttpRequest request, HttpResponse response,
        ICurrentActor actor, ManualDeficitReadService service, CancellationToken cancellationToken) =>
        await RespondAsync(request, response, actor, ApiFailureOperation.ManualDeficitDetail, async token =>
        {
            RequireQuery(request, "skip", "take");
            return Results.Ok(await service.ReadAsync(id, Integer(request, "skip", 0),
                Integer(request, "take", 25), token));
        }, cancellationToken);

    internal static async Task<IResult> CreateAsync(Guid id, CreateManualDeficitCorrectionCommand? command,
        [FromServices] IManualDeficitCorrectionService service, ICurrentActor actor,
        HttpRequest request, HttpResponse response, CancellationToken cancellationToken) =>
        await ExecuteAsync(command, (body, key, token) => service.CreateAsync(id, body, key, token),
            actor, request, response, cancellationToken);

    internal static async Task<IResult> ApproveAsync(Guid id, ApproveManualDeficitCorrectionCommand? command,
        [FromServices] IManualDeficitCorrectionService service, ICurrentActor actor,
        HttpRequest request, HttpResponse response, CancellationToken cancellationToken) =>
        await ExecuteAsync(command, (body, key, token) => service.ApproveAsync(id, body, key, token),
            actor, request, response, cancellationToken);

    internal static async Task<IResult> RejectAsync(Guid id, RejectManualDeficitCorrectionCommand? command,
        [FromServices] IManualDeficitCorrectionService service, ICurrentActor actor,
        HttpRequest request, HttpResponse response, CancellationToken cancellationToken) =>
        await ExecuteAsync(command, (body, key, token) => service.RejectAsync(id, body, key, token),
            actor, request, response, cancellationToken);

    internal static async Task<IResult> ResolveCreateAsync(Guid id,
        [FromServices] IManualDeficitCorrectionService service, ICurrentActor actor,
        HttpRequest request, HttpResponse response, CancellationToken cancellationToken) =>
        await ResolveAsync(id, "create", service, actor, request, response, cancellationToken);

    internal static async Task<IResult> ResolveDecisionAsync(Guid id, string operation,
        [FromServices] IManualDeficitCorrectionService service, ICurrentActor actor,
        HttpRequest request, HttpResponse response, CancellationToken cancellationToken) =>
        await ResolveAsync(id, operation is "approve" or "reject" ? operation : "invalid",
            service, actor, request, response, cancellationToken);

    private static async Task<IResult> ResolveAsync(Guid id, string operation,
        IManualDeficitCorrectionService service, ICurrentActor actor,
        HttpRequest request, HttpResponse response, CancellationToken cancellationToken)
    {
        response.Headers.Vary = "Idempotency-Key";
        return await RespondAsync(request, response, actor, ApiFailureOperation.ManualDeficitRecovery, async token =>
        {
            RequireQuery(request);
            var kind = operation switch
            {
                "create" => ManualDeficitCorrectionOperation.Create,
                "approve" => ManualDeficitCorrectionOperation.Approve,
                "reject" => ManualDeficitCorrectionOperation.Reject,
                _ => throw new InvalidManualDeficitQueryException()
            };
            var result = await service.ResolveIdempotencyAsync(id, kind, Key(request), token);
            return result is null ? Results.NoContent() : Results.Ok(result);
        }, cancellationToken);
    }

    private static async Task<IResult> ExecuteAsync<T>(T? body,
        Func<T, string, CancellationToken, Task<ManualDeficitCorrectionCommandResult>> command,
        ICurrentActor actor, HttpRequest request, HttpResponse response, CancellationToken cancellationToken) where T : class =>
        await RespondAsync(request, response, actor, ApiFailureOperation.ManualDeficitCommand, async token =>
        {
            RequireQuery(request);
            if (body is null) return SafeProblemResults.InvalidRequest();
            var result = await command(body, Key(request), token);
            return Results.Json(result.Correction, statusCode: result.ResponseStatusCode);
        }, cancellationToken);

    private static async Task<IResult> RespondAsync(HttpRequest request, HttpResponse response,
        ICurrentActor actor, ApiFailureOperation operation,
        Func<CancellationToken, Task<IResult>> action, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            cancellationToken.ThrowIfCancellationRequested();
            return await action(cancellationToken);
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (ManualDeficitCorrectionNotFoundException) { return SafeProblemResults.ManualDeficitNotFound(); }
        catch (InvalidIdempotencyKeyException) { return SafeProblemResults.InvalidIdempotencyKey(); }
        catch (IdempotencyConflictException) { return SafeProblemResults.ManualDeficitIdempotencyConflict(); }
        catch (ManualDeficitCorrectionConflictException) { return SafeProblemResults.ManualDeficitStateConflict(); }
        catch (InvalidManualDeficitQueryException)
        {
            ApiFailureLog.InvalidRequest(request.HttpContext, operation);
            return SafeProblemResults.InvalidRequest();
        }
        catch (DomainRuleViolationException)
        {
            ApiFailureLog.InvalidRequest(request.HttpContext, operation);
            return SafeProblemResults.ManualDeficitRuleViolation();
        }
        catch (ManualDeficitCorrectionUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(request.HttpContext, operation);
            return SafeProblemResults.ManualDeficitUnavailable();
        }
    }

    private static void RequireQuery(HttpRequest request, params string[] allowed)
    {
        if (request.Query.Keys.Any(key => !allowed.Contains(key, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidManualDeficitQueryException();
    }
    private static string? Value(HttpRequest request, string key)
    {
        if (!request.Query.TryGetValue(key, out var values)) return null;
        if (values.Count != 1 || string.IsNullOrEmpty(values[0])) throw new InvalidManualDeficitQueryException();
        return values[0];
    }
    private static int Integer(HttpRequest request, string key, int fallback) => Value(request, key) is { } text
        ? int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value : throw new InvalidManualDeficitQueryException()
        : fallback;
    private static bool Boolean(HttpRequest request, string key) => Value(request, key) switch
    {
        null or "false" => false,
        "true" => true,
        _ => throw new InvalidManualDeficitQueryException()
    };
    private static string Key(HttpRequest request)
    {
        var values = request.Headers["Idempotency-Key"];
        if (values.Count != 1) throw new InvalidIdempotencyKeyException("One idempotency key is required.");
        return values[0] ?? string.Empty;
    }
}
