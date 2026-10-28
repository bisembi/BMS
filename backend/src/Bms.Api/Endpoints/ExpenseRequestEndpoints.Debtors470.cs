using Bms.Api.Problems;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;

namespace Bms.Api.Endpoints;
public static partial class ExpenseRequestEndpoints
{
    private static async Task<IResult> Debtors470Async(Guid? organizationId,Guid? costCenterId,Guid? counterpartyId,
        DateOnly? plannedPaymentDate,bool? isUrgent,DebtorsService470 service,HttpRequest request,HttpResponse response,CancellationToken token)
    {
        response.Headers.CacheControl="no-store";
        try
        {
            if(request.Query.Keys.Any(k=>k is not("organizationId" or "costCenterId" or "counterpartyId" or "plannedPaymentDate" or "isUrgent"))
                ||request.Query.Any(p=>p.Value.Count!=1))return SafeProblemResults.InvalidRequest();
            return Results.Ok(await service.ReadAsync(new(organizationId,costCenterId,counterpartyId,plannedPaymentDate,isUrgent),token));
        }
        catch(ActorNotAuthenticatedException){return SafeProblemResults.AuthenticationRequired();}
        catch(ActorForbiddenException){return SafeProblemResults.Forbidden();}
        catch(ArgumentException){return SafeProblemResults.InvalidRequest();}
        catch(DebtorsReportUnavailable470){return SafeProblemResults.ListBusinessColumnsUnavailable();}
    }
}
