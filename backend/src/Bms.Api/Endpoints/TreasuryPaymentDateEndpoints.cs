using Bms.Api.Problems;
using Bms.Api.Security;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Common;
using Bms.Application.Abstractions;

namespace Bms.Api.Endpoints;

public static class TreasuryPaymentDateEndpoints
{
    public static IEndpointRouteBuilder MapTreasuryPaymentDateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/expense-requests");
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            try { return await next(context); }
            catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
            catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
            catch (ExpenseRequestNotFoundException) { return SafeProblemResults.ExpenseRequestNotFound(); }
            catch (ExpenseRequestVersionConflictException) { return SafeProblemResults.Conflict(ExpenseRequestProblemCodes.VersionConflict); }
            catch (IdempotencyConflictException) { return SafeProblemResults.Conflict(ExpenseRequestProblemCodes.IdempotencyConflict); }
            catch (InvalidIdempotencyKeyException) { return SafeProblemResults.InvalidIdempotencyKey(); }
            catch (TreasuryPaymentDateConfirmedException) { return SafeProblemResults.TreasuryPaymentDateConfirmed(); }
            catch (TreasuryReschedulingPausedException) { return SafeProblemResults.TreasuryReschedulingPaused(); }
            catch (TreasuryBudgetCheckUnavailableException) { return SafeProblemResults.TreasuryBudgetCheckUnavailable(); }
            catch (DomainRuleViolationException) { return SafeProblemResults.InvalidRequest(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (!Bms.Api.Diagnostics.ApiProcessFatalExceptionClassifier.IsFatal(exception))
            { return SafeProblemResults.ExpenseRequestWorkflowUnavailable(); }
        });
        group.MapGet("/{id:guid}/treasury-payment-date", ReadAsync).WithName("GetTreasuryPaymentDate");
        group.MapPut("/{id:guid}/treasury-payment-date", SetAsync).RequireCsrfToken().WithName("SetTreasuryPaymentDate");
        return endpoints;
    }
    private static async Task<IResult> ReadAsync(Guid id, HttpRequest request, ITreasuryPaymentDateService service, CancellationToken token)
    {
        if (request.Query.Count != 0) return SafeProblemResults.InvalidRequest();
        return Results.Ok(await service.ReadAsync(id, token));
    }
    private static async Task<IResult> SetAsync(Guid id, SetTreasuryPaymentDateCommand? command, HttpRequest request,
        ITreasuryPaymentDateService service, CancellationToken token)
    {
        if (command is null) return SafeProblemResults.InvalidRequest();
        if (request.Query.Count != 0 || !request.Headers.TryGetValue("Idempotency-Key", out var key) || key.Count != 1)
            return SafeProblemResults.InvalidIdempotencyKey();
        return Results.Ok(await service.SetAsync(id, command, key[0] ?? string.Empty, token));
    }
}
