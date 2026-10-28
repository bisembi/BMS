using Bms.Api.Problems;
using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Api.Endpoints;

public static partial class ExpenseRequestEndpoints
{
    private static async Task<IResult> GetPaymentRegisterAsync(
        [Microsoft.AspNetCore.Mvc.FromServices] IExpensePaymentRegisterReader reader,
        HttpRequest request,HttpResponse response,CancellationToken token)
    {
        response.Headers.CacheControl="no-store";
        try {
            if(request.Query.Count!=0) return SafeProblemResults.InvalidRequest();
            return Results.Ok(await reader.ReadAsync(token));
        }
        catch(ActorNotAuthenticatedException){return SafeProblemResults.AuthenticationRequired();}
        catch(ActorForbiddenException){return SafeProblemResults.Forbidden();}
    }
}
