using Bms.Api.Problems;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Endpoints;

public static partial class ExpenseRequestEndpoints
{
    internal static async Task<IResult> ResolveDeleteDraftAsync(Guid id, ExpenseRequestService service,
        HttpRequest request, CancellationToken token)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        request.HttpContext.Response.Headers.Vary = "Idempotency-Key";
        try
        {
            return await service.ResolveDeleteDraftIdempotencyAsync(id, RequireSingleIdempotencyKey(request), token)
                ? Results.Ok(new { id, deleted = true }) : Results.NoContent();
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (BudgetOwnerCreationAuthorityUnavailableException) { return SafeProblemResults.ExpenseRequestCreationAuthorityUnavailable(); }
        catch (ExpenseRequestNotFoundException) { return SafeProblemResults.ExpenseRequestNotFound(); }
        catch (InvalidIdempotencyKeyException) { return SafeProblemResults.InvalidIdempotencyKey(); }
        catch (DomainRuleViolationException) { return SafeProblemResults.DomainRuleViolation(); }
    }

    internal static async Task<IResult> DeleteDraftAsync(Guid id, [FromBody] DeleteExpenseRequestDraftCommand command,
        ExpenseRequestService service, HttpRequest request, CancellationToken cancellationToken)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        try
        {
            var key = RequireSingleIdempotencyKey(request);
            await service.DeleteDraftAsync(id, command, key, cancellationToken);
            return Results.NoContent();
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (BudgetOwnerCreationAuthorityUnavailableException) { return SafeProblemResults.ExpenseRequestCreationAuthorityUnavailable(); }
        catch (ExpenseRequestNotFoundException) { return SafeProblemResults.ExpenseRequestNotFound(); }
        catch (InvalidIdempotencyKeyException) { return SafeProblemResults.InvalidIdempotencyKey(); }
        catch (IdempotencyConflictException) { return ConflictProblem(ExpenseRequestProblemCodes.IdempotencyConflict); }
        catch (ExpenseRequestVersionConflictException) { return ConflictProblem(ExpenseRequestProblemCodes.VersionConflict); }
        catch (DomainRuleViolationException) { return SafeProblemResults.DomainRuleViolation(); }
    }
}
