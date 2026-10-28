using Bms.Api.Diagnostics;
using Bms.Api.Problems;
using Bms.Application.Audit;
using Bms.Application.Common;

namespace Bms.Api.Endpoints;

public static class ExchangeJournalEndpoints
{
    public static IEndpointRouteBuilder MapExchangeJournalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/administration/exchange-journal", ListAsync).WithTags("Administration").WithName("ReadExchangeJournal");
        endpoints.MapGet("/api/v1/administration/exchange-journal/{kind}/{id:guid}", DetailAsync).WithTags("Administration").WithName("ReadExchangeJournalDetails");
        return endpoints;
    }
    internal static async Task<IResult> ListAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? kind, string? status,
        int? skip, int? take, ExchangeJournalService service, HttpResponse response, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try { return Results.Ok(await service.ListAsync(fromUtc, toUtc, kind, status, skip ?? 0, take ?? 50, cancellationToken)); }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (ArgumentException) { return SafeProblemResults.InvalidRequest(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e) when (!ApiProcessFatalExceptionClassifier.IsFatal(e)) { return SafeProblemResults.BusinessAuditUnavailable(); }
    }
    internal static async Task<IResult> DetailAsync(string kind, Guid id, bool? includeExcluded, int? skip, int? take,
        ExchangeJournalService service, HttpResponse response, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try { return Results.Ok(await service.DetailAsync(kind, id, includeExcluded ?? false, skip ?? 0, take ?? 50, cancellationToken)); }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (ArgumentException) { return SafeProblemResults.InvalidRequest(); }
        catch (ExchangeJournalEntryNotFoundException) { return Results.Problem(statusCode: 404, detail: "Запись журнала обмена не найдена."); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e) when (!ApiProcessFatalExceptionClassifier.IsFatal(e)) { return SafeProblemResults.BusinessAuditUnavailable(); }
    }
}
