using Bms.Api.Diagnostics;
using Bms.Api.Problems;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Bms.Application.Abstractions;
using Bms.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Endpoints;

/// <summary>
/// Workflow commands. The submit handler owns only HTTP binding and the fixed
/// public error contract; all route, ownership and state decisions remain in
/// <see cref="ExpenseRequestWorkflowService"/>.
/// </summary>
public static partial class ExpenseRequestEndpoints
{
    internal static async Task<IResult> GetWorkflowCommandAvailabilityAsync(
        Guid id,
        [FromServices] ExpenseRequestWorkflowService service,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.GetAvailableCommandsAsync(id, cancellationToken);
            return Results.Ok(result);
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ExpenseRequestNotFoundException)
        {
            return SafeProblemResults.ExpenseRequestNotFound();
        }
        catch (ExpenseRequestWorkflowUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                request.HttpContext,
                ApiFailureOperation.ExpenseRequestWorkflowCommandAvailability);
            return SafeProblemResults.ExpenseRequestWorkflowUnavailable();
        }
    }

    internal static async Task<IResult> ExecuteWorkflowCommandAsync(
        Guid id,
        string commandCode,
        ExpenseRequestWorkflowCommand? command,
        [FromServices] ExpenseRequestWorkflowService service,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var idempotencyKey = RequireSingleIdempotencyKey(request);
            if (command is null)
            {
                return SafeProblemResults.InvalidRequest();
            }

            var result = await service.ExecuteCommandAsync(
                id,
                commandCode,
                command,
                idempotencyKey,
                cancellationToken);
            return Results.Json(result.ExpenseRequest, statusCode: result.ResponseStatusCode);
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
        catch (ExpenseRequestBudgetChangedException)
        {
            return SafeProblemResults.BudgetLimitChangedConflict();
        }
        catch (TreasuryReschedulingPausedException) { return SafeProblemResults.TreasuryReschedulingPaused(); }
        catch (TreasuryBudgetCheckUnavailableException) { return SafeProblemResults.TreasuryBudgetCheckUnavailable(); }
        catch (ExpenseRequestWorkflowUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                request.HttpContext,
                ApiFailureOperation.ExpenseRequestWorkflowCommand);
            return SafeProblemResults.ExpenseRequestWorkflowUnavailable();
        }
        catch (DomainRuleViolationException)
        {
            return SafeProblemResults.DomainRuleViolation();
        }
    }

    internal static async Task<IResult> ResolveWorkflowCommandIdempotencyAsync(
        Guid id,
        string commandCode,
        [FromServices] ExpenseRequestWorkflowService service,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Vary = "Idempotency-Key";
        try
        {
            var idempotencyKey = RequireSingleIdempotencyKey(request);
            var resolved = await service.ResolveWorkflowCommandIdempotencyAsync(
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
        catch (ExpenseRequestWorkflowUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                request.HttpContext,
                ApiFailureOperation.ExpenseRequestWorkflowCommandIdempotencyResolution);
            return SafeProblemResults.ExpenseRequestWorkflowUnavailable();
        }
        catch (DomainRuleViolationException)
        {
            return SafeProblemResults.DomainRuleViolation();
        }
    }

    internal static async Task<IResult> SubmitAsync(
        Guid id,
        SubmitExpenseRequestCommand? command,
        [FromServices] ExpenseRequestWorkflowService service,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var idempotencyKey = RequireSingleIdempotencyKey(request);
            if (command is null)
            {
                return SafeProblemResults.InvalidRequest();
            }

            var result = await service.SubmitAsync(
                id,
                command,
                idempotencyKey,
                cancellationToken);
            return Results.Json(result.ExpenseRequest, statusCode: result.ResponseStatusCode);
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
        catch (ExpenseRequestSubmissionBlockedException)
        {
            return SafeProblemResults.DomainRuleViolation();
        }
        catch (ExpenseRequestWorkflowUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                request.HttpContext,
                ApiFailureOperation.ExpenseRequestWorkflowSubmission);
            return SafeProblemResults.ExpenseRequestWorkflowUnavailable();
        }
        catch (DomainRuleViolationException)
        {
            return SafeProblemResults.DomainRuleViolation();
        }
    }

    internal static async Task<IResult> ResolveSubmitIdempotencyAsync(
        Guid id,
        [FromServices] ExpenseRequestWorkflowService service,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Vary = "Idempotency-Key";
        try
        {
            var idempotencyKey = RequireSingleIdempotencyKey(request);
            var resolved = await service.ResolveSubmitIdempotencyAsync(
                id,
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
        catch (ExpenseRequestWorkflowUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                request.HttpContext,
                ApiFailureOperation.ExpenseRequestWorkflowIdempotencyResolution);
            return SafeProblemResults.ExpenseRequestWorkflowUnavailable();
        }
    }
}
