using Bms.Api.Problems;
using Bms.Api.Security;
using Bms.Application.Budgeting;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;

namespace Bms.Api.Endpoints;

public static class BudgetActualsEndpoints
{
    public static RouteGroupBuilder MapBudgetActualsEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/actuals/preview", PreviewAsync).RequireCsrfToken().WithName("PreviewBudgetActuals");
        group.MapPost("/actuals/import", ImportAsync).RequireCsrfToken().WithName("ImportCashFlowActuals");
        return group;
    }

    private static async Task<IResult> PreviewAsync(PreviewBudgetActualsCommand? command, HttpRequest request,
        HttpResponse response, BudgetActualsService service, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            if (command is null || request.Query.Count != 0) return SafeProblemResults.InvalidRequest();
            return Results.Ok(await service.PreviewAsync(command, cancellationToken));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { return SafeResult(exception); }
    }

    private static async Task<IResult> ImportAsync(ImportCashFlowActualsCommand? command, HttpRequest request,
        HttpResponse response, BudgetActualsService service, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            if (command is null || request.Query.Count != 0) return SafeProblemResults.InvalidRequest();
            var result = await service.ImportAsync(command, cancellationToken);
            return Results.Json(result, statusCode: result.AlreadyImported ? 200 : 201);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { return SafeResult(exception); }
    }

    private static IResult SafeResult(Exception exception) => exception switch
    {
        ActorNotAuthenticatedException => SafeProblemResults.AuthenticationRequired(),
        ActorForbiddenException => SafeProblemResults.Forbidden(),
        BudgetInstanceNotFoundException => SafeProblemResults.BudgetInstanceNotFound(),
        BudgetInstanceConcurrencyConflictException => SafeProblemResults.Conflict("version-conflict"),
        DomainRuleViolationException => SafeProblemResults.InvalidRequest(),
        CashFlowActualsSourceInvalidException => ActualsConflict("actuals-source-invalid"),
        CashFlowActualsNotAvailableException => ActualsConflict("actuals-not-loaded"),
        CashFlowActualsPeriodUnavailableException => ActualsConflict("actuals-period-unavailable"),
        CashFlowActualsMappingUnavailableException => ActualsConflict("actuals-mapping-unavailable"),
        _ => Results.Problem(statusCode: 503, detail: "Cash-flow actuals are temporarily unavailable."),
    };

    private static IResult ActualsConflict(string code) => Results.Problem(statusCode: 409,
        detail: "Actual cash-flow data are unavailable for this selection.",
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
