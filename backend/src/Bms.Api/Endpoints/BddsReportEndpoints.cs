using System.Globalization;
using Bms.Api.Problems;
using Bms.Api.Security;
using Bms.Application.Budgeting;
using Bms.Application.Common;
using Bms.Domain.Common;

namespace Bms.Api.Endpoints;

public static class BddsReportEndpoints
{
    public static RouteGroupBuilder MapBddsReportEndpoints(this RouteGroupBuilder group)
    {
        group.MapUnbudgetedArticlesEndpoints();
        group.MapBddsDrillEndpoints();
        group.MapGet("/reports/bdds", ReadAsync).WithName("ReadBddsReport");
        group.MapPost("/reports/bdds/import", ImportAsync)
            .RequireCsrfToken()
            .WithName("ImportBddsSnapshot");
        return group;
    }

    private static async Task<IResult> ReadAsync(
        HttpRequest request,
        HttpResponse response,
        BddsReportService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            if (request.Query.Keys.Any(key => key is not ("year" or "costCenterId" or "fromMonth" or "toMonth" or "includeActuals" or "actualsYear" or "versionId" or "compareVersionId"))
                || request.Query["year"].Count != 1
                || !int.TryParse(request.Query["year"][0], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var year) || year is < 2000 or > 2100)
            {
                return SafeProblemResults.InvalidRequest();
            }

            Guid? costCenterId = null;
            if (request.Query.TryGetValue("costCenterId", out var values))
            {
                if (values.Count != 1 || !Guid.TryParse(values[0], out var parsed) || parsed == Guid.Empty)
                {
                    return SafeProblemResults.InvalidRequest();
                }
                costCenterId = parsed;
            }

            DateOnly? Month(string key, DateOnly fallback)
            {
                if (!request.Query.TryGetValue(key, out var months)) return fallback;
                return months.Count == 1 && months[0]?.Length == 7
                    && DateOnly.TryParseExact(months[0] + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var parsed) ? parsed : null;
            }
            var from = Month("fromMonth", new(year, 1, 1));
            var to = Month("toMonth", new(year, 12, 1));
            var includeActuals = false;
            if (request.Query.TryGetValue("includeActuals", out var actualValues))
            {
                if (actualValues.Count != 1 || actualValues[0] is not ("true" or "false"))
                    return SafeProblemResults.InvalidRequest();
                includeActuals = actualValues[0] == "true";
            }
            if (!from.HasValue || !to.HasValue) return SafeProblemResults.InvalidRequest();
            var actualsYear = year;
            if (request.Query.TryGetValue("actualsYear", out var factYears))
            {
                if (!includeActuals || factYears.Count != 1 || !int.TryParse(factYears[0], NumberStyles.None, CultureInfo.InvariantCulture, out var requestedFactYear) || !AccountingPeriod467.IsReportYear(requestedFactYear))
                    return SafeProblemResults.InvalidRequest();
                actualsYear = int.Parse(factYears[0]!, CultureInfo.InvariantCulture);
            }
            Guid? Version(string key)
            {
                if (!request.Query.TryGetValue(key, out var ids)) return null;
                if (ids.Count != 1 || !Guid.TryParse(ids[0], out var value) || value == Guid.Empty)
                    throw new DomainRuleViolationException("Invalid company version filter.");
                return value;
            }
            return Results.Ok(await service.ReadPeriodAsync(year, costCenterId,
                new(from.Value, to.Value, includeActuals, Version("versionId"), Version("compareVersionId"), actualsYear), cancellationToken));
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (DomainRuleViolationException) { return SafeProblemResults.InvalidRequest(); }
        catch (CompanyBudgetVersionNotFoundException) { return Results.NotFound(); }
        catch (CompanyBudgetOrderUnresolvedException)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                detail: "Порядок перекрывающихся черновиков требует подтверждения владельца.");
        }
        catch (BddsSnapshotNotFoundException)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound,
                detail: "An approved BDDS snapshot was not found.");
        }
        catch (CashFlowActualsMappingUnavailableException)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                detail: "The actuals article mapping requires review.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                detail: "The BDDS report is temporarily unavailable.");
        }
    }

    private static async Task<IResult> ImportAsync(
        ImportBddsSnapshotCommand? command,
        HttpRequest request,
        HttpResponse response,
        BddsReportService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            if (command is null || request.Query.Count != 0)
            {
                return SafeProblemResults.InvalidRequest();
            }

            var result = await service.ImportAsync(command, cancellationToken);
            return Results.Json(result,
                statusCode: result.AlreadyImported
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status201Created);
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (BddsImportConflictException exception)
        {
            loggerFactory.CreateLogger("Bms.Api.Endpoints.BddsReportEndpoints")
                .LogWarning(exception, "BDDS import was rejected by a control check.");
            return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                detail: "The BDDS source differs from the approved control set or is already represented by conflicting data.");
        }
        catch (DomainRuleViolationException) { return SafeProblemResults.InvalidRequest(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("Bms.Api.Endpoints.BddsReportEndpoints")
                .LogError(exception, "BDDS import failed unexpectedly.");
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                detail: "The BDDS import is temporarily unavailable.");
        }
    }
}
