using System.Globalization;
using Bms.Api.Diagnostics;
using Bms.Api.Problems;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Endpoints;

public static partial class ExpenseRequestEndpoints
{
    internal static async Task<IResult> GetPaymentDetailsAsync(Guid id, HttpRequest request, HttpResponse response,
        ICurrentActor actor, [FromServices] ExpenseRequestPaymentDetailsService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Query.Keys.Any(key => key is not ("skip" or "take"))
                || !TryPaymentDetailsPageValue(request, "skip", 0, out var skip)
                || !TryPaymentDetailsPageValue(request, "take", 25, out var take)
                || take is < 1 or > 100)
            {
                ApiFailureLog.InvalidRequest(request.HttpContext, ApiFailureOperation.ExpenseRequestPaymentDetails);
                return SafeProblemResults.InvalidRequest();
            }
            var result = await service.ReadAsync(id, skip, take, cancellationToken);
            return result is null ? SafeProblemResults.ExpenseRequestNotFound() : Results.Ok(result);
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (ExpenseRequestPaymentDetailsUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(request.HttpContext, ApiFailureOperation.ExpenseRequestPaymentDetails);
            return SafeProblemResults.PaymentDetailsUnavailable();
        }
    }

    private static bool TryPaymentDetailsPageValue(HttpRequest request, string key, int fallback, out int value)
    {
        value = fallback;
        return !request.Query.TryGetValue(key, out var values)
            || (values.Count == 1 && int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out value));
    }
}
