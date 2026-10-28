using Bms.Api.Problems;
using Bms.Application.Budgeting;
using Bms.Application.Common;

namespace Bms.Api.Endpoints;

public static class RequestBudgetExplanationEndpoints442
{
    public static IEndpointRouteBuilder MapRequestBudgetExplanation442(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/budget/expense-requests/{requestId:guid}/explanation",ReadAsync).WithTags("Budget");return endpoints;
    }
    internal static async Task<IResult> ReadAsync(Guid requestId,Guid version,Guid articleId,int? factSkip,int? reserveSkip,long? revision,
        RequestBudgetExplanationService442 service,HttpResponse response,CancellationToken token)
    {
        response.Headers.CacheControl="no-store";
        try{return Results.Ok(await service.ReadAsync(requestId,version,articleId,factSkip??0,reserveSkip??0,revision,token));}
        catch(ActorNotAuthenticatedException){return SafeProblemResults.AuthenticationRequired();}
        catch(ActorForbiddenException){return SafeProblemResults.Forbidden();}
        catch(ExpenseRequestNotFoundException){return Results.NotFound();}
        catch(ExpenseRequestVersionConflictException){return Results.StatusCode(409);}
        catch(ArgumentException){return SafeProblemResults.InvalidRequest();}
        catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
        catch(Exception){return SafeProblemResults.BusinessAuditUnavailable();}
    }
}
