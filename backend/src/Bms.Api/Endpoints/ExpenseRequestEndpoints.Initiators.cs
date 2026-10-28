using Bms.Api.Problems;
using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Api.Endpoints;

public static partial class ExpenseRequestEndpoints
{
    private static async Task<IResult> ListInitiatorsAsync([Microsoft.AspNetCore.Mvc.FromServices] IExpenseRequestInitiatorDirectory directory,
        HttpResponse response, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try { return Results.Ok(await directory.ListAsync(cancellationToken)); }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
    }
}
