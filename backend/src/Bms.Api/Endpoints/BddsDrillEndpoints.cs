using Bms.Api.Problems;
using Bms.Api.Security;
using Bms.Application.Budgeting;
using Bms.Application.Common;
using Bms.Domain.Common;

namespace Bms.Api.Endpoints;

public static class BddsDrillEndpoints
{
    public static RouteGroupBuilder MapBddsDrillEndpoints(this RouteGroupBuilder group)
    {
        // Read-only POST: the authorized article set can exceed URL limits.
        group.MapPost("/reports/bdds/drill", ReadAsync).RequireCsrfToken().WithName("ReadBddsActualDrill");
        return group;
    }
    private static async Task<IResult> ReadAsync(BddsDrillQuery query, HttpRequest request,
        HttpResponse response, BddsDrillService service, CancellationToken token)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            if (request.Query.Count != 0) return SafeProblemResults.InvalidRequest();
            return Results.Ok(await service.ReadAsync(query, token));
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (DomainRuleViolationException) { return SafeProblemResults.InvalidRequest(); }
        catch (BddsDrillChangedException) { return Results.Problem(statusCode: 409, detail: "Факт обновился. Обновите БДДС и откройте расшифровку заново."); }
        catch (BddsDrillControlException) { return Results.Problem(statusCode: 409, detail: "Сумма расшифровки не совпала с БДДС. Данные не показаны; требуется проверка обмена."); }
        catch (BddsDrillUnavailableException) { return Results.Problem(statusCode: 503, detail: "Расшифровка этого снимка ещё не загружена. Проверьте журнал обмена или повторите позже."); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Results.Problem(statusCode: 503, detail: "Расшифровка временно недоступна. Обновите БДДС; при повторении ошибки обратитесь к администратору."); }
    }
}
