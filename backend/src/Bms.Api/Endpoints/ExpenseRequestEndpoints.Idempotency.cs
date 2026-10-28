using Bms.Api.Problems;
using Bms.Api.Diagnostics;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;

namespace Bms.Api.Endpoints;

/// Resolution of a previously sent command. A resolved command answers with
/// the stored result; an unknown one answers 204 so the caller can safely
/// send the command for the first time.
public static partial class ExpenseRequestEndpoints
{

    internal static async Task<IResult> ResolveCreateDraftIdempotencyAsync(
        ExpenseRequestService service,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Vary = "Idempotency-Key";
        try
        {
            var idempotencyKey = RequireSingleIdempotencyKey(request);
            var resolved = await service.ResolveCreateDraftIdempotencyAsync(
                idempotencyKey,
                cancellationToken);
            return resolved is null ? Results.NoContent() : Results.Ok(resolved);
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
    }

    internal static async Task<IResult> ResolveUpdateDraftIdempotencyAsync(
        Guid id,
        ExpenseRequestService service,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Vary = "Idempotency-Key";
        try
        {
            var idempotencyKey = RequireSingleIdempotencyKey(request);
            var resolved = await service.ResolveUpdateDraftIdempotencyAsync(
                id,
                idempotencyKey,
                cancellationToken);
            return resolved is null ? Results.NoContent() : Results.Ok(resolved);
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
        catch (ExpenseRequestSubstitutionAuthorityUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                request.HttpContext,
                ApiFailureOperation.ExpenseRequestSubstitutionAuthority);
            return SafeProblemResults.ExpenseRequestSubstitutionAuthorityUnavailable();
        }
        catch (InvalidIdempotencyKeyException)
        {
            return SafeProblemResults.InvalidIdempotencyKey();
        }
    }

    internal static async Task<IResult> ResolveCopyDraftIdempotencyAsync(
        Guid sourceId,
        ExpenseRequestService service,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken,
        bool fullCopy = false)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Vary = "Idempotency-Key";
        try
        {
            var idempotencyKey = RequireSingleIdempotencyKey(request);
            var resolved = await service.ResolveCopyDraftIdempotencyAsync(
                sourceId,
                idempotencyKey,
                cancellationToken, fullCopy);
            return resolved is null ? Results.NoContent() : Results.Ok(resolved);
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
    }
}
