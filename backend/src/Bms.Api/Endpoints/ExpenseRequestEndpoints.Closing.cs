using Bms.Api.Diagnostics;
using Bms.Api.Problems;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Endpoints;

/// <summary>
/// HTTP binding for the three approved accrual-closing commands. Every public
/// failure is fixed text; diagnostics, cycle UUIDs and provider messages never
/// cross this boundary.
/// </summary>
public static partial class ExpenseRequestEndpoints
{
    internal static async Task<IResult> GetClosingStateAsync(
        Guid id,
        [FromServices] IExpenseRequestClosingService service,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            var closing = await service.GetStateAsync(id, cancellationToken);
            return closing is null
                ? SafeProblemResults.ExpenseRequestNotFound()
                : Results.Ok(closing);
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (ExpenseRequestClosingUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                request.HttpContext,
                ApiFailureOperation.ExpenseRequestClosingState);
            return SafeProblemResults.ExpenseRequestClosingUnavailable();
        }
    }

    internal static async Task<IResult> ClosePartiallyAsync(
        Guid id,
        CloseExpenseRequestPartiallyCommand? command,
        [FromServices] IExpenseRequestClosingService service,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken) => await ExecuteClosingCommandAsync(
            command,
            (body, key, token) => service.ClosePartiallyAsync(id, body, key, token),
            request,
            response,
            cancellationToken);

    internal static async Task<IResult> CloseFullyAsync(
        Guid id,
        CloseExpenseRequestFullyCommand? command,
        [FromServices] IExpenseRequestClosingService service,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken) => await ExecuteClosingCommandAsync(
            command,
            (body, key, token) => service.CloseFullyAsync(id, body, key, token),
            request,
            response,
            cancellationToken);

    internal static async Task<IResult> ReopenAsync(
        Guid id,
        ReopenExpenseRequestCommand? command,
        [FromServices] IExpenseRequestClosingService service,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken) => await ExecuteClosingCommandAsync(
            command,
            (body, key, token) => service.ReopenAsync(id, body, key, token),
            request,
            response,
            cancellationToken);

    internal static async Task<IResult> ResolveClosingIdempotencyAsync(
        Guid id,
        string commandCode,
        [FromServices] IExpenseRequestClosingService service,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Vary = "Idempotency-Key";
        try
        {
            var idempotencyKey = RequireSingleIdempotencyKey(request);
            var resolved = await service.ResolveIdempotencyAsync(
                id,
                commandCode,
                idempotencyKey,
                cancellationToken);
            return resolved is null ? Results.NoContent() : Results.Ok(resolved);
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (ExpenseRequestNotFoundException)
        {
            return SafeProblemResults.ExpenseRequestNotFound();
        }
        catch (InvalidIdempotencyKeyException)
        {
            return SafeProblemResults.InvalidIdempotencyKey();
        }
        catch (ExpenseRequestClosingUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                request.HttpContext,
                ApiFailureOperation.ExpenseRequestClosingIdempotencyResolution);
            return SafeProblemResults.ExpenseRequestClosingUnavailable();
        }
        catch (DomainRuleViolationException)
        {
            return SafeProblemResults.DomainRuleViolation();
        }
    }

    private static async Task<IResult> ExecuteClosingCommandAsync<TCommand>(
        TCommand? command,
        Func<TCommand, string, CancellationToken, Task<ExpenseRequestClosingCommandResult>> execute,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken)
        where TCommand : class
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            var idempotencyKey = RequireSingleIdempotencyKey(request);
            if (command is null)
            {
                return SafeProblemResults.InvalidRequest();
            }

            var result = await execute(command, idempotencyKey, cancellationToken);
            return Results.Json(result.Closing, statusCode: result.ResponseStatusCode);
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (ExpenseRequestNotFoundException)
        {
            return SafeProblemResults.ExpenseRequestNotFound();
        }
        catch (InvalidIdempotencyKeyException)
        {
            return SafeProblemResults.InvalidIdempotencyKey();
        }
        catch (IdempotencyConflictException)
        {
            return ConflictProblem(ExpenseRequestProblemCodes.IdempotencyConflict);
        }
        catch (ExpenseRequestVersionConflictException)
        {
            return ConflictProblem(ExpenseRequestProblemCodes.VersionConflict);
        }
        catch (ExpenseRequestClosingBlockedException)
        {
            return SafeProblemResults.DomainRuleViolation();
        }
        catch (ExpenseRequestClosingUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                request.HttpContext,
                ApiFailureOperation.ExpenseRequestClosingCommand);
            return SafeProblemResults.ExpenseRequestClosingUnavailable();
        }
        catch (DomainRuleViolationException)
        {
            return SafeProblemResults.DomainRuleViolation();
        }
    }
}
