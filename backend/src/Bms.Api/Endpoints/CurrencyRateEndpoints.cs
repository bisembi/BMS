using Bms.Api.Problems;
using Bms.Api.Security;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Infrastructure.Persistence;

namespace Bms.Api.Endpoints;

public static class CurrencyRateEndpoints
{
    public static IEndpointRouteBuilder MapCurrencyRateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reference-data/currency-rates").WithTags("Reference data");
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            try { return await next(context); }
            catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
            catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
            catch (CurrencyRateStateConflictException) { return SafeProblemResults.CurrencyRateConflict(); }
            catch (DomainRuleViolationException) { return SafeProblemResults.InvalidRequest(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (!Bms.Api.Diagnostics.ApiProcessFatalExceptionClassifier.IsFatal(exception))
            { return SafeProblemResults.ReferenceDataUnavailable(); }
        });
        group.MapGet("/", async (DateOnly? date, int? skip, int? take, CurrencyRateDirectoryService service, CancellationToken token) =>
            Results.Ok(await service.ListAsync(date, skip ?? 0, take ?? 25, token)));
        group.MapGet("/expense-draft-defaults", async (string currencyCode, CurrencyRateDirectoryService service, CancellationToken token) =>
            Results.Ok(await service.DraftDefaultsAsync(currencyCode, token)));
        group.MapPut("/manual", async (ManualCurrencyRateCommand command, CurrencyRateDirectoryService service, CancellationToken token) =>
        {
            await service.SaveManualAsync(command, token);
            return Results.NoContent();
        }).RequireCsrfToken();
        group.MapPost("/import", async (CurrencyRateDirectoryService service, CancellationToken token) =>
            Results.Ok(new { succeeded = await service.ImportAsync(false, token) })).RequireCsrfToken();
        return endpoints;
    }
}
