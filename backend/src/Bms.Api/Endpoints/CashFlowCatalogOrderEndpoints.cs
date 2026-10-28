using Bms.Api.Diagnostics;
using Bms.Api.Problems;
using Bms.Api.Security;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Application.ReferenceData;
using Bms.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Endpoints;

public static class CashFlowCatalogOrderEndpoints
{
    public static IEndpointRouteBuilder MapCashFlowCatalogOrderEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reference-data/cash-flow-items")
            .WithTags("Reference data");
        group.MapGet("/order", ReadAsync).WithName("GetCashFlowCatalogOrder");
        group.MapPost("/order/move", MoveAsync).RequireCsrfToken()
            .WithName("MoveCashFlowCatalogOrder");
        return endpoints;
    }

    internal static async Task<IResult> ReadAsync(
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        [FromServices] CashFlowCatalogOrderService service,
        CancellationToken cancellationToken) =>
        await RespondAsync(request, response, actor, ApiFailureOperation.CashFlowCatalogOrderRead,
            async token => Results.Ok(await service.ReadAsync(token)), cancellationToken);

    internal static async Task<IResult> MoveAsync(
        MoveCashFlowCatalogOrderCommand? command,
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        [FromServices] CashFlowCatalogOrderService service,
        CancellationToken cancellationToken) =>
        await RespondAsync(request, response, actor, ApiFailureOperation.CashFlowCatalogOrderWrite, async token =>
        {
            if (command is null) throw new DomainRuleViolationException("Invalid command.");
            var values = request.Headers["Idempotency-Key"];
            if (values.Count != 1)
                throw new InvalidIdempotencyKeyException("One idempotency key is required.");
            var result = await service.MoveAsync(command, values[0] ?? string.Empty, token);
            return Results.Ok(result.Snapshot);
        }, cancellationToken);

    private static async Task<IResult> RespondAsync(
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        ApiFailureOperation operation,
        Func<CancellationToken, Task<IResult>> action,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Query.Count != 0) throw new DomainRuleViolationException("Invalid query.");
            return await action(cancellationToken);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Map(request, exception, operation);
        }
    }

    private static bool IsExpected(Exception exception) => exception is
        ActorNotAuthenticatedException
        or ActorForbiddenException
        or CashFlowCatalogOrderNotFoundException
        or CashFlowCatalogOrderVersionConflictException
        or CashFlowCatalogOrderUnavailableException
        or DomainRuleViolationException
        or InvalidIdempotencyKeyException
        or IdempotencyConflictException;

    private static IResult Map(
        HttpRequest request,
        Exception exception,
        ApiFailureOperation operation) => exception switch
        {
            ActorNotAuthenticatedException => SafeProblemResults.AuthenticationRequired(),
            ActorForbiddenException => SafeProblemResults.Forbidden(),
            CashFlowCatalogOrderNotFoundException => SafeProblemResults.ReferenceDataNotFound(),
            CashFlowCatalogOrderVersionConflictException => SafeProblemResults.Conflict("version-conflict"),
            InvalidIdempotencyKeyException => SafeProblemResults.InvalidIdempotencyKey(),
            IdempotencyConflictException => SafeProblemResults.Conflict("idempotency-conflict"),
            DomainRuleViolationException => Invalid(request, operation),
            CashFlowCatalogOrderUnavailableException => Unavailable(request, operation),
            _ => throw new InvalidOperationException("An unsupported cash-flow order failure was mapped.")
        };

    private static IResult Invalid(HttpRequest request, ApiFailureOperation operation)
    {
        ApiFailureLog.InvalidRequest(request.HttpContext, operation);
        return SafeProblemResults.InvalidRequest();
    }

    private static IResult Unavailable(HttpRequest request, ApiFailureOperation operation)
    {
        ApiFailureLog.DependencyUnavailable(request.HttpContext, operation);
        return SafeProblemResults.ReferenceDataUnavailable();
    }
}
