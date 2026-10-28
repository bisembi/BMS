using System.Globalization;
using Bms.Api.Problems;
using Bms.Application.Budgeting;
using Bms.Application.Common;
using Bms.Domain.Common;

namespace Bms.Api.Endpoints;

public static class UnbudgetedArticlesEndpoints
{
    public static RouteGroupBuilder MapUnbudgetedArticlesEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/reports/unbudgeted-articles", ReadAsync).WithName("ReadUnbudgetedArticles");
        return group;
    }

    internal static UnbudgetedArticlesOptions Parse(IQueryCollection query)
    {
        if (query.Keys.Any(key => key is not ("fromMonth" or "toMonth" or "costCenterId" or "groupId" or "versionId" or "skip" or "take"))
            || query.Any(pair => pair.Value.Count != 1))
            throw new DomainRuleViolationException("Invalid report query.");
        DateOnly Month(string key)
        {
            var value = query[key].SingleOrDefault();
            if (value is not { Length: 7 } || !DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
                throw new DomainRuleViolationException("Invalid report period.");
            return month;
        }
        Guid? Id(string key)
        {
            if (!query.ContainsKey(key)) return null;
            if (!Guid.TryParseExact(query[key][0], "D", out var id) || id == Guid.Empty)
                throw new DomainRuleViolationException("Invalid report identifier.");
            return id;
        }
        int Number(string key, int fallback)
        {
            if (!query.ContainsKey(key)) return fallback;
            if (!int.TryParse(query[key][0], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                throw new DomainRuleViolationException("Invalid report pagination.");
            return value;
        }
        var onlyUngrouped = query["groupId"].SingleOrDefault() == "none";
        var result = new UnbudgetedArticlesOptions(Month("fromMonth"), Month("toMonth"),
            Id("costCenterId"), onlyUngrouped ? null : Id("groupId"), onlyUngrouped,
            Id("versionId"), Number("skip", 0), Number("take", 25));
        result.Months();
        return result;
    }

    private static async Task<IResult> ReadAsync(HttpRequest request, HttpResponse response,
        UnbudgetedArticlesService service, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try { return Results.Ok(await service.ReadAsync(Parse(request.Query), cancellationToken)); }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (DomainRuleViolationException) { return SafeProblemResults.InvalidRequest(); }
        catch (CompanyBudgetVersionNotFoundException) { return Results.NotFound(); }
        catch (CompanyBudgetOrderUnresolvedException)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                detail: "Порядок перекрывающихся черновиков требует подтверждения владельца.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                detail: "Отчёт незабюджетированных статей временно недоступен.");
        }
    }
}
