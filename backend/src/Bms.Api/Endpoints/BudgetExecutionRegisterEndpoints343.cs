using Bms.Api.Problems;
using Bms.Application.Common;
using Bms.Application.Budgeting;

namespace Bms.Api.Endpoints;

public static class BudgetExecutionRegisterEndpoints
{
    public static IEndpointRouteBuilder MapBudgetExecutionRegister343(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/administration/registers/budget-execution",ReadAsync).WithTags("Administration");return endpoints;
    }
    internal static async Task<IResult> ReadAsync(DateOnly month,Guid? costCenterId,Guid? articleId,Guid? requestId,int? skip,int? take,
        BudgetExecutionRegisterService343 reader,HttpResponse response,CancellationToken cancellationToken)
    {
        response.Headers.CacheControl="no-store";
        try{return Results.Ok(await reader.ReadAsync(month,costCenterId,articleId,requestId,skip??0,take??100,cancellationToken));}
        catch(ActorNotAuthenticatedException){return SafeProblemResults.AuthenticationRequired();}
        catch(ActorForbiddenException){return SafeProblemResults.Forbidden();}
        catch(ArgumentException){return SafeProblemResults.InvalidRequest();}
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){throw;}
        catch(Exception){return SafeProblemResults.BusinessAuditUnavailable();}
    }
}
