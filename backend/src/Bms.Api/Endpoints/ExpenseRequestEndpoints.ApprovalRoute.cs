using Bms.Api.Problems;
using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Api.Endpoints;

public static partial class ExpenseRequestEndpoints
{
    private static async Task<IResult> GetApprovalRouteAsync(Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] IExpenseApprovalRoutePresentation service,
        HttpRequest request, HttpResponse response, CancellationToken token)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            if (id == Guid.Empty || request.Query.Count != 0) return SafeProblemResults.InvalidRequest();
            var result = await service.ReadAsync(id, token);
            return result is null ? SafeProblemResults.ExpenseRequestNotFound() : Results.Ok(result);
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
    }
}
