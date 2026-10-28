using Bms.Api.Problems;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;

namespace Bms.Api.Endpoints;

public static class ExpenseRequestHistoryEndpoints
{
    public static IEndpointRouteBuilder MapExpenseRequestHistoryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/expense-requests/{id:guid}/history",
                GetAsync)
            .WithName("ListExpenseRequestHistory")
            .WithTags("Expense requests");
        return endpoints;
    }

    internal static async Task<IResult> GetAsync(
        Guid id,
        int? skip,
        int? take,
        ExpenseRequestHistoryService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            // Explicit representation opt-in keeps already-open old clients
            // compatible with their closed generic-label response parser.
            var authors = response.HttpContext.Request.Headers["X-Bms-History-Authors"];
            var includeActorNames = authors.Count == 1
                && string.Equals(authors[0], "employee-names", StringComparison.Ordinal);
            var filter = response.HttpContext.Request.Query["commentsOnly"];
            if (filter.Count > 1 || filter.Count == 1 && filter[0] is not ("true" or "false"))
                return SafeProblemResults.InvalidRequest();
            var result = filter.Count == 1 && filter[0] == "true"
                ? await service.ListCommentsAsync(id, skip ?? 0, take ?? 20, includeActorNames, cancellationToken)
                : await service.ListAsync(
                id,
                skip ?? 0,
                take ?? 20,
                includeActorNames,
                cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
    }
}
