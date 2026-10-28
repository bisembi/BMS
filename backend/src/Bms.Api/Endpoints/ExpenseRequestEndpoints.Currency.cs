using Bms.Api.Diagnostics;
using Bms.Api.Problems;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Common;

namespace Bms.Api.Endpoints;

public static partial class ExpenseRequestEndpoints
{
    internal static async Task<IResult> RefreshCurrencyAsync(Guid id, RefreshExpenseRequestCurrencyCommand? command,
        ExpenseRequestService service, HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var key = RequireSingleIdempotencyKey(request);
            if (command is null) return SafeProblemResults.InvalidRequest();
            return Results.Ok(await service.RefreshCurrencyAsync(id, command, key, cancellationToken));
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (ExpenseRequestNotFoundException) { return SafeProblemResults.ExpenseRequestNotFound(); }
        catch (InvalidIdempotencyKeyException) { return SafeProblemResults.InvalidIdempotencyKey(); }
        catch (IdempotencyConflictException) { return ConflictProblem(ExpenseRequestProblemCodes.IdempotencyConflict); }
        catch (ExpenseRequestVersionConflictException) { return ConflictProblem(ExpenseRequestProblemCodes.VersionConflict); }
        catch (BudgetOwnerCreationAuthorityUnavailableException) { return SafeProblemResults.ExpenseRequestCreationAuthorityUnavailable(); }
        catch (ExpenseRequestSubstitutionAuthorityUnavailableException) { return SafeProblemResults.ExpenseRequestSubstitutionAuthorityUnavailable(); }
        catch (DraftCurrencyCalculationUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(request.HttpContext, ApiFailureOperation.ExpenseRequestUpdateCurrencyCalculation);
            return Results.Problem(statusCode: 503, title: "Currency calculation is temporarily unavailable.");
        }
        catch (DomainRuleViolationException) { return SafeProblemResults.DomainRuleViolation(); }
    }
}
