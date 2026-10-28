using Bms.Api.Problems;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Common;

namespace Bms.Api.Endpoints;

public static partial class ExpenseRequestEndpoints
{
    private static Task<IResult> ReserveNumberAsync(ExpenseRequestNumberLeaseCommand? command,
        ExpenseRequestService service, CancellationToken token) => ChangeNumberAsync(command, false, service, token);
    private static Task<IResult> ReleaseNumberAsync(ExpenseRequestNumberLeaseCommand? command,
        ExpenseRequestService service, CancellationToken token) => ChangeNumberAsync(command, true, service, token);
    private static async Task<IResult> ChangeNumberAsync(ExpenseRequestNumberLeaseCommand? command, bool release,
        ExpenseRequestService service, CancellationToken token)
    {
        try
        {
            if (command is null) return SafeProblemResults.InvalidRequest();
            var result = await service.ChangeNumberLeaseAsync(command, release, token);
            return release ? Results.NoContent() : Results.Ok(result);
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (ExpenseRequestNotFoundException) { return SafeProblemResults.ExpenseRequestNotFound(); }
        catch (BudgetOwnerCreationAuthorityUnavailableException) { return SafeProblemResults.ExpenseRequestCreationAuthorityUnavailable(); }
        catch (DomainRuleViolationException) { return SafeProblemResults.InvalidRequest(); }
    }
}
