using Bms.Api.Diagnostics;
using Bms.Api.Problems;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Application.ReferenceData;
using Bms.Domain.Common;
using Microsoft.AspNetCore.Mvc;

using Bms.Api.Security;
namespace Bms.Api.Endpoints;

public static class CashFlowAccrualSettingsEndpoints
{
    public static IEndpointRouteBuilder MapCashFlowAccrualSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reference-data/cash-flow-items").WithTags("Reference data");
        group.MapGet("/{id:guid}/accrual-settings", ReadAsync).WithName("GetCashFlowAccrualSettings");
        group.MapPut("/{id:guid}/accrual-settings", UpdateAsync).RequireCsrfToken()
            .WithName("UpdateCashFlowAccrualSettings");
        return endpoints;
    }

    internal static async Task<IResult> ReadAsync(Guid id, HttpRequest request, HttpResponse response,
        ICurrentActor actor, [FromServices] CashFlowAccrualSettingsService service,
        CancellationToken cancellationToken) =>
        await RespondAsync(request, response, actor, ApiFailureOperation.CashFlowAccrualSettingsRead,
            async token => Results.Ok(await service.ReadAsync(id, token)), cancellationToken);

    internal static async Task<IResult> UpdateAsync(Guid id, UpdateCashFlowAccrualSettingsCommand? command,
        HttpRequest request, HttpResponse response, ICurrentActor actor,
        [FromServices] CashFlowAccrualSettingsService service, CancellationToken cancellationToken) =>
        await RespondAsync(request, response, actor, ApiFailureOperation.CashFlowAccrualSettingsWrite, async token =>
        {
            if (command is null) return SafeProblemResults.InvalidRequest();
            var values = request.Headers["Idempotency-Key"];
            if (values.Count != 1) throw new InvalidIdempotencyKeyException("One idempotency key is required.");
            var result = await service.UpdateAsync(id, command, values[0] ?? string.Empty, token);
            return Results.Ok(result.Settings);
        }, cancellationToken);

    private static async Task<IResult> RespondAsync(HttpRequest request, HttpResponse response,
        ICurrentActor actor, ApiFailureOperation operation, Func<CancellationToken, Task<IResult>> action,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Query.Count != 0) return SafeProblemResults.InvalidRequest();
            return await action(cancellationToken);
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (CashFlowAccrualSettingsNotFoundException) { return SafeProblemResults.CashFlowAccrualSettingsNotFound(); }
        catch (CashFlowAccrualSettingsVersionConflictException) { return SafeProblemResults.Conflict("version-conflict"); }
        catch (InvalidIdempotencyKeyException) { return SafeProblemResults.InvalidIdempotencyKey(); }
        catch (IdempotencyConflictException) { return SafeProblemResults.Conflict("idempotency-conflict"); }
        catch (DomainRuleViolationException)
        {
            ApiFailureLog.InvalidRequest(request.HttpContext, operation);
            return SafeProblemResults.InvalidRequest();
        }
        catch (CashFlowAccrualSettingsUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(request.HttpContext, operation);
            return SafeProblemResults.CashFlowAccrualSettingsUnavailable();
        }
    }
}
