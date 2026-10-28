using Bms.Api.Problems;
using Bms.Application.Audit;
using Bms.Application.Common;

namespace Bms.Api.Endpoints;

public static class ChangeJournalEndpoints
{
    public static IEndpointRouteBuilder MapChangeJournalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/administration/change-journal", ListAsync).WithTags("Administration");
        return endpoints;
    }

    internal static async Task<IResult> ListAsync(
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? category, int? skip, int? take,
        ChangeJournalService service, HttpResponse response, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            return Results.Ok(await service.ListAsync(fromUtc, toUtc, category, skip ?? 0, take ?? 50, cancellationToken));
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (ArgumentException) { return SafeProblemResults.InvalidRequest(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return SafeProblemResults.BusinessAuditUnavailable();
        }
    }
}
