using System.Globalization;
using Bms.Api.Diagnostics;
using Bms.Api.Exports;
using Bms.Api.Problems;
using Bms.Application.Accruals;
using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Api.Endpoints;

/// <summary>
/// Read-only managerial-accrual report. Closing remains on the reviewed
/// expense-request command routes; this group exposes no mutation verb.
/// </summary>
public static class AccrualEndpoints
{
    public static IEndpointRouteBuilder MapAccrualEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/management/accruals")
            .WithTags("Managerial accruals");
        group.MapGet("/active", ListActiveAsync).WithName("ListActiveManagerialAccruals");
        group.MapGet("/active/export.xlsx", ExportActiveAsync)
            .WithName("ExportActiveManagerialAccruals");
        group.MapGet("/opiu", OpiuAsync).WithName("AccrualOpiu");
        group.MapGet("/opiu/details", OpiuAsync).WithName("AccrualOpiuDetails");
        return endpoints;
    }

    private static async Task<IResult> OpiuAsync(HttpRequest request, ICurrentActor currentActor,
        OpiuReportService service, HttpResponse response, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(currentActor);
            string[] allowed = ["from", "through", "organizationId", "costCenterId", "expenseItemId", "skip", "take"];
            if (request.Query.Any(p => !allowed.Contains(p.Key, StringComparer.Ordinal) || p.Value.Count != 1))
                throw new InvalidActiveAccrualQueryException();
            var query = new OpiuQuery(OptionalMonth(request, "from") ?? throw new InvalidActiveAccrualQueryException(),
                OptionalMonth(request, "through") ?? throw new InvalidActiveAccrualQueryException(),
                OptionalGuid(request, "organizationId"), OptionalGuid(request, "costCenterId"),
                OptionalGuid(request, "expenseItemId"), OptionalInt32(request, "skip") ?? 0,
                OptionalInt32(request, "take") ?? 50);
            return request.Path.Value!.EndsWith("/details", StringComparison.Ordinal)
                ? Results.Ok(await service.DetailsAsync(query, cancellationToken))
                : Results.Ok(await service.GetAsync(query, cancellationToken));
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (InvalidActiveAccrualQueryException) { return SafeProblemResults.InvalidRequest(); }
        catch (ActiveAccrualExportTooLargeException)
        {
            return Results.Problem(statusCode: 422, detail: "Слишком большая область ОПиУ. Сузьте период или подразделение.");
        }
        catch (OperationCanceledException) { throw; }
        catch (ActiveAccrualReportUnavailableException) { return SafeProblemResults.ActiveAccrualReportUnavailable(); }
    }

    private static async Task<IResult> ListActiveAsync(
        HttpRequest request,
        ICurrentActor currentActor,
        ActiveAccrualReportService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            response.Headers.CacheControl = "no-store";
            ActorIdentity.RequireBusinessDataAccess(currentActor);
            var parsed = Parse(request, includePaging: true);
            var result = await service.GetAsync(
                new ActiveAccrualQuery(
                    parsed.Filter,
                    parsed.Skip ?? 0,
                    parsed.Take ?? 50),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (InvalidActiveAccrualQueryException)
        {
            ApiFailureLog.InvalidRequest(
                response.HttpContext,
                ApiFailureOperation.ActiveAccrualList);
            return SafeProblemResults.InvalidRequest();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ActiveAccrualReportUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.ActiveAccrualList);
            return SafeProblemResults.ActiveAccrualReportUnavailable();
        }
    }

    private static async Task<IResult> ExportActiveAsync(
        HttpRequest request,
        ICurrentActor currentActor,
        ActiveAccrualExportService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            response.Headers.CacheControl = "no-store";
            ActorIdentity.RequireBusinessDataAccess(currentActor);
            var parsed = Parse(request, includePaging: false);
            var export = await service.ExportAsync(parsed.Filter, cancellationToken);
            return Results.File(
                ActiveAccrualExcelExporter.CreateStream(
                    export,
                    parsed.Columns,
                    cancellationToken),
                ActiveAccrualExcelExporter.ContentType,
                ActiveAccrualExcelExporter.FileName(export.GeneratedAtUtc),
                enableRangeProcessing: false);
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (InvalidActiveAccrualQueryException)
        {
            ApiFailureLog.InvalidRequest(
                response.HttpContext,
                ApiFailureOperation.ActiveAccrualExport);
            return SafeProblemResults.InvalidRequest();
        }
        catch (ExcelExportPrecisionException)
        {
            return SafeProblemResults.ExcelExportPrecision();
        }
        catch (ActiveAccrualExportTooLargeException)
        {
            return SafeProblemResults.ActiveAccrualExportTooLarge();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ActiveAccrualReportUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.ActiveAccrualExport);
            return SafeProblemResults.ActiveAccrualReportUnavailable();
        }
    }

    private static ParsedQuery Parse(HttpRequest request, bool includePaging) => new(
        new ActiveAccrualFilter(
            OptionalGuid(request, "organizationId"),
            OptionalGuid(request, "costCenterId"),
            OptionalMonth(request, "accrualPeriodFrom"),
            OptionalMonth(request, "accrualPeriodThrough"),
            OptionalGuid(request, "counterpartyId"),
            OptionalGuid(request, "contractId"),
            OptionalDetailLevel(request),
            OptionalGrouping(request)),
        includePaging ? OptionalInt32(request, "skip") : null,
        includePaging ? OptionalInt32(request, "take") : null,
        OptionalColumns(request));

    private static IReadOnlyList<string>? OptionalColumns(HttpRequest request)
    {
        var value = OptionalSingle(request, "columns");
        if (value is null)
        {
            return null;
        }

        var columns = value.Split(',', StringSplitOptions.None);
        var approved = ActiveAccrualExcelExporter.SelectableColumnKeys.ToHashSet(StringComparer.Ordinal);
        return columns.Length > 0
            && columns.All(column => column.Length > 0 && column.Trim() == column && approved.Contains(column))
            && columns.Distinct(StringComparer.Ordinal).Count() == columns.Length
                ? columns
                : throw new InvalidActiveAccrualQueryException();
    }

    private static ActiveAccrualGrouping OptionalGrouping(HttpRequest request) =>
        OptionalSingle(request, "groupBy") switch
        {
            null or "none" => ActiveAccrualGrouping.None,
            "counterparty" => ActiveAccrualGrouping.Counterparty,
            "contract" => ActiveAccrualGrouping.Contract,
            _ => throw new InvalidActiveAccrualQueryException()
        };

    private static ActiveAccrualDetailLevel OptionalDetailLevel(HttpRequest request)
    {
        var value = OptionalSingle(request, "detail");
        return value switch
        {
            null or "line" => ActiveAccrualDetailLevel.Line,
            "request" => ActiveAccrualDetailLevel.Request,
            _ => throw new InvalidActiveAccrualQueryException()
        };
    }

    private static Guid? OptionalGuid(HttpRequest request, string name)
    {
        var value = OptionalSingle(request, name);
        if (value is null)
        {
            return null;
        }

        return Guid.TryParse(value, out var result) && result != Guid.Empty
            ? result
            : throw new InvalidActiveAccrualQueryException();
    }

    private static DateOnly? OptionalMonth(HttpRequest request, string name)
    {
        var value = OptionalSingle(request, name);
        if (value is null)
        {
            return null;
        }

        return DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var result)
            && result.Day == 1
            ? result
            : throw new InvalidActiveAccrualQueryException();
    }

    private static int? OptionalInt32(HttpRequest request, string name)
    {
        var value = OptionalSingle(request, name);
        if (value is null)
        {
            return null;
        }

        return int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var result)
            ? result
            : throw new InvalidActiveAccrualQueryException();
    }

    private static string? OptionalSingle(HttpRequest request, string name)
    {
        if (!request.Query.TryGetValue(name, out var values) || values.Count == 0)
        {
            return null;
        }

        if (values.Count != 1)
        {
            throw new InvalidActiveAccrualQueryException();
        }

        var value = values[0];
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidActiveAccrualQueryException();
    }

    private sealed record ParsedQuery(
        ActiveAccrualFilter Filter,
        int? Skip,
        int? Take,
        IReadOnlyList<string>? Columns);
}
