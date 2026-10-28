using Bms.Api.Problems;
using Bms.Api.Diagnostics;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Common;

namespace Bms.Api.Endpoints;

/// Draft commands: create, update and copy. Each one takes its idempotency
/// key from the request before the service is called, so a malformed key is
/// rejected without reaching the application layer.
public static partial class ExpenseRequestEndpoints
{

    private static async Task<IResult> CreateDraftAsync(
        CreateExpenseRequestCommand? command,
        ExpenseRequestService service,
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

            var result = await service.CreateDraftAsync(command, idempotencyKey, cancellationToken);
            return result.ResponseStatusCode == StatusCodes.Status201Created
                ? Results.Created($"/api/v1/expense-requests/{result.ExpenseRequest.Id:D}", result.ExpenseRequest)
                : Results.Json(result.ExpenseRequest, statusCode: result.ResponseStatusCode);
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ExpenseRequestNotFoundException)
        {
            return SafeProblemResults.ExpenseRequestNotFound();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (BudgetOwnerCreationAuthorityUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                request.HttpContext,
                ApiFailureOperation.ExpenseRequestCreationAuthority);
            return SafeProblemResults.ExpenseRequestCreationAuthorityUnavailable();
        }
        catch (InvalidIdempotencyKeyException)
        {
            return SafeProblemResults.InvalidIdempotencyKey();
        }
        catch (IdempotencyConflictException)
        {
            return ConflictProblem(ExpenseRequestProblemCodes.IdempotencyConflict);
        }
        catch (DraftCurrencyCalculationUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                request.HttpContext,
                ApiFailureOperation.ExpenseRequestCreateCurrencyCalculation);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Currency calculation is temporarily unavailable.");
        }
        catch (DomainRuleViolationException exception)
        {
            return SafeProblemResults.ExpenseDraftValidation(exception.Reason);
        }
    }

    private static async Task<IResult> UpdateDraftAsync(
        Guid id,
        UpdateExpenseRequestCommand? command,
        ExpenseRequestService service,
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

            var result = await service.UpdateDraftAsync(id, command, idempotencyKey, cancellationToken);
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
        catch (BudgetOwnerCreationAuthorityUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                request.HttpContext,
                ApiFailureOperation.ExpenseRequestCreationAuthority);
            return SafeProblemResults.ExpenseRequestCreationAuthorityUnavailable();
        }
        catch (ExpenseRequestSubstitutionAuthorityUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                request.HttpContext,
                ApiFailureOperation.ExpenseRequestSubstitutionAuthority);
            return SafeProblemResults.ExpenseRequestSubstitutionAuthorityUnavailable();
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
        catch (DraftCurrencyCalculationUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                request.HttpContext,
                ApiFailureOperation.ExpenseRequestUpdateCurrencyCalculation);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Currency calculation is temporarily unavailable.");
        }
        catch (DomainRuleViolationException exception)
        {
            return SafeProblemResults.ExpenseDraftValidation(exception.Reason);
        }
    }

    internal static async Task<IResult> CopyDraftAsync(
        Guid sourceId,
        ExpenseRequestService service,
        HttpRequest request,
        CancellationToken cancellationToken,
        bool fullCopy = false)
    {
        try
        {
            var idempotencyKey = RequireSingleIdempotencyKey(request);
            var result = await service.CopyDraftAsync(sourceId, idempotencyKey, cancellationToken, fullCopy);
            return Results.Created(
                $"/api/v1/expense-requests/{result.ExpenseRequest.Id:D}",
                result.ExpenseRequest);
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (BudgetOwnerCreationAuthorityUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                request.HttpContext,
                ApiFailureOperation.ExpenseRequestCreationAuthority);
            return SafeProblemResults.ExpenseRequestCreationAuthorityUnavailable();
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
        catch (DraftCurrencyCalculationUnavailableException)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Currency calculation is temporarily unavailable.");
        }
        catch (DomainRuleViolationException exception)
        {
            return SafeProblemResults.ExpenseDraftValidation(exception.Reason);
        }
    }
}
