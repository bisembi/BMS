using Bms.Api.Problems;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Microsoft.AspNetCore.Mvc;
namespace Bms.Api.Endpoints;
public static partial class ExpenseRequestEndpoints
{
    internal static Task<IResult> GetApprovalDetailsAsync(Guid id,[FromServices] ExpenseRequestWorkflowService service,HttpResponse response,bool? splitAccruals,CancellationToken token)
    { response.Headers.CacheControl="no-store";return ApprovalDetailsBoundary(async()=>Results.Ok(await service.GetApprovalDetailsAsync(id,splitAccruals==true,token))); }
    internal static Task<IResult> UpdateApprovalDetailsAsync(Guid id,ExpenseRequestApprovalDetailsCommand? command,
        [FromServices] ExpenseRequestWorkflowService service,HttpRequest request,CancellationToken token) => ApprovalDetailsBoundary(async()=>{
            var key=RequireSingleIdempotencyKey(request);if(command is null)return SafeProblemResults.InvalidRequest();
            return Results.Ok((await service.UpdateApprovalDetailsAsync(id,command,key,token)).ExpenseRequest);
        });
    internal static Task<IResult> ResolveApprovalDetailsAsync(Guid id,[FromServices] ExpenseRequestWorkflowService service,
        HttpRequest request,HttpResponse response,CancellationToken token)
    {response.Headers.CacheControl="no-store";response.Headers.Vary="Idempotency-Key";return ApprovalDetailsBoundary(async()=>{
        var value=await service.ResolveApprovalDetailsAsync(id,RequireSingleIdempotencyKey(request),token);
        return value is null?Results.NoContent():Results.Ok(value);
    });}
    private static async Task<IResult> ApprovalDetailsBoundary(Func<Task<IResult>> action)
    {
        try{return await action();}
        catch(ActorNotAuthenticatedException){return SafeProblemResults.AuthenticationRequired();}
        catch(ActorForbiddenException){return SafeProblemResults.Forbidden();}
        catch(ExpenseRequestNotFoundException){return SafeProblemResults.ExpenseRequestNotFound();}
        catch(InvalidIdempotencyKeyException){return SafeProblemResults.InvalidIdempotencyKey();}
        catch(IdempotencyConflictException){return ConflictProblem(ExpenseRequestProblemCodes.IdempotencyConflict);}
        catch(ExpenseRequestVersionConflictException){return ConflictProblem(ExpenseRequestProblemCodes.VersionConflict);}
        catch(ExpenseRequestApprovalDetailsInvalidException){return SafeProblemResults.InvalidApprovalDetails();}
        catch(ApprovalAllocationTotalMismatchException){return SafeProblemResults.ApprovalAllocationTotalMismatch();}
        catch(ApprovalAllocationSourceMismatchException){return SafeProblemResults.ApprovalAllocationSourceMismatch();}
        catch(DomainRuleViolationException){return SafeProblemResults.ProtectedApprovalDetails();}
        catch(ExpenseRequestWorkflowUnavailableException){return SafeProblemResults.ExpenseRequestWorkflowUnavailable();}
    }
}
