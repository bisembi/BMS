using Bms.Api.Diagnostics;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Budgeting;
using Bms.Application.Common;

namespace Bms.Api.Endpoints;
public static partial class BudgetDirectoryEndpoints
{
    private static async Task<IResult> SetPurpose457Async(Guid id, SetCostCenterPurpose457Command? command,
        HttpRequest request, HttpResponse response, ICurrentActor actor, BudgetDirectoryService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            if (command is null || request.Query.Count != 0) return InvalidRequest(request, ApiFailureOperation.BudgetDirectoryWrite);
            return Results.Ok(await service.SetPurpose457Async(id, command, RequireIdempotencyKey(request), cancellationToken));
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        { return MapException(request, exception, ApiFailureOperation.BudgetDirectoryWrite); }
    }
}
