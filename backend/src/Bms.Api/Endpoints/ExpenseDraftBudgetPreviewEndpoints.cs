using Bms.Api.Problems;
using Bms.Api.Security;
using Bms.Application.Budgeting;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Common;

namespace Bms.Api.Endpoints;

public static class ExpenseDraftBudgetPreviewEndpoints
{
    public static RouteGroupBuilder MapExpenseDraftBudgetPreview(this RouteGroupBuilder group)
    {
        group.MapPost("/expense-draft/preview", async (ExpenseDraftBudgetCommand? command, HttpRequest request,
            HttpResponse response, ExpenseDraftBudgetPreviewService service, CancellationToken token) =>
        {
            response.Headers.CacheControl = "no-store";
            try
            {
                if (command is null || request.Query.Count != 0) return SafeProblemResults.InvalidRequest();
                return Results.Ok(await service.PreviewAsync(command, token));
            }
            catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
            catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
            catch (ExpenseRequestNotFoundException) { return SafeProblemResults.ExpenseRequestNotFound(); }
            catch (ExpenseRequestVersionConflictException) { return SafeProblemResults.Conflict("version-conflict"); }
            catch (DomainRuleViolationException) { return SafeProblemResults.InvalidRequest(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return SafeProblemResults.BudgetExecutionUnavailable(); }
        }).RequireCsrfToken().WithName("PreviewExpenseDraftBudget");
        return group;
    }
}
