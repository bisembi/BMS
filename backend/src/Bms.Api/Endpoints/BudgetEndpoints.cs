using System.Globalization;
using Bms.Api.Diagnostics;
using Bms.Api.Exports;
using Bms.Api.Problems;
using Bms.Application.Abstractions;
using Bms.Application.Budgeting;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;

namespace Bms.Api.Endpoints;

/// <summary>
/// Read-only execution reports plus explicit document and directory routes.
/// Directory maintenance never writes budget limits or expense reservations;
/// those retain their own business writers and authority.
/// </summary>
public static class BudgetEndpoints
{
    public static IEndpointRouteBuilder MapBudgetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/budget").WithTags("Budget");
        group.MapGet("/positions", ListPositionsAsync).WithName("ListBudgetPositions");
        group.MapGet("/positions/export.xlsx", ExportPositionsAsync)
            .WithName("ExportBudgetPositions");
        group.MapGet("/positions/reservations", ListReservationsAsync)
            .WithName("ListBudgetPositionReservations");
        group.MapBudgetInstanceEndpoints();
        group.MapCompanyBudgetVersions();
        group.MapBudgetDirectoryEndpoints();
        group.MapBddsReportEndpoints();
        group.MapBudgetActualsEndpoints();
        group.MapExpenseDraftBudgetPreview();
        endpoints.MapRequestBudgetExplanation442();
        return endpoints;
    }

    private static async Task<IResult> ListPositionsAsync(
        HttpRequest request,
        ICurrentActor currentActor,
        BudgetPositionService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            // Set before anything can fail, so every answer this route gives -
            // page, refusal or outage - is uncacheable.
            response.Headers.CacheControl = "no-store";
            ActorIdentity.RequireBusinessDataAccess(currentActor);
            var query = ParseQuery(request, includePaging: true);
            var expectedVersion = OptionalGuid(request, "expectedRequestVersion");
            var expectedFact = ExpectedFact(request);
            var currentStructure = CurrentStructure(request);
            if (query.ExpenseRequestId.HasValue && request.Query.ContainsKey("structure"))
                throw new BudgetQueryInvalidException("structure");
            if (expectedFact.HasValue && query.ExpenseRequestId.HasValue)
                throw new BudgetQueryInvalidException("expectedFactSnapshotId");
            var completeText = OptionalSingle(request, "complete");
            var complete = false;
            if (completeText is not null && !bool.TryParse(completeText, out complete))
                throw new BudgetQueryInvalidException("complete");
            if (complete && (!query.ExpenseRequestId.HasValue || query.Skip.HasValue || query.Take.HasValue))
                throw new BudgetQueryInvalidException("complete");
            if (expectedVersion.HasValue && (!complete || expectedVersion == Guid.Empty))
                throw new BudgetQueryInvalidException("expectedRequestVersion");
            var result = await service.ListAsync(
                query.ExpenseRequestId,
                query.CostCenterId,
                query.CashFlowItemId,
                query.BudgetMonth,
                query.Skip ?? 0,
                query.Take ?? 50,
                cancellationToken,
                complete,
                expectedVersion,
                expectedFact,
                currentStructure);
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
        catch (ExpenseRequestNotFoundException)
        {
            return SafeProblemResults.ExpenseRequestNotFound();
        }
        catch (ExecutionFactVersionConflictException)
        {
            return SafeProblemResults.ExecutionFactChangedConflict();
        }
        catch (ExpenseRequestVersionConflictException)
        {
            return SafeProblemResults.Conflict(ExpenseRequestProblemCodes.VersionConflict);
        }
        catch (BudgetQueryInvalidException)
        {
            ApiFailureLog.InvalidRequest(
                response.HttpContext,
                ApiFailureOperation.BudgetExecutionList);
            return SafeProblemResults.InvalidRequest();
        }
        catch (OperationCanceledException)
        {
            // A caller who went away is not a server failure and must not be
            // reported as one.
            throw;
        }
        catch (Exception)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.BudgetExecutionList);
            // Anything else is an outage, and the caller learns only that. The
            // reason never reaches the wire: no provider text, no statement, no
            // connection detail, no identifier.
            return SafeProblemResults.BudgetExecutionUnavailable();
        }
    }

    /// <summary>
    /// The same area as the list, as a file. Same filters, same order, same
    /// people; no page, because a file that held one would describe less than
    /// it claims. If the export cannot be produced whole, none is sent.
    /// </summary>
    private static async Task<IResult> ExportPositionsAsync(
        HttpRequest request,
        ICurrentActor currentActor,
        BudgetExecutionExportService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            response.Headers.CacheControl = "no-store";
            ActorIdentity.RequireBusinessDataAccess(currentActor);
            var query = ParseQuery(request, includePaging: false);
            var export = await service.ExportAsync(
                query.CostCenterId,
                query.CashFlowItemId,
                query.BudgetMonth,
                cancellationToken,
                ExpectedFact(request),
                CurrentStructure(request));
            return Results.File(
                BudgetExecutionExcelExporter.CreateStream(export, cancellationToken),
                BudgetExecutionExcelExporter.ContentType,
                BudgetExecutionExcelExporter.FileName(export.GeneratedAtUtc),
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
        catch (ExecutionFactVersionConflictException)
        {
            return SafeProblemResults.ExecutionFactChangedConflict();
        }
        catch (ExcelExportPrecisionException)
        {
            return SafeProblemResults.ExcelExportPrecision();
        }
        catch (BudgetExecutionExportTooLargeException)
        {
            return SafeProblemResults.BudgetExecutionExportTooLarge();
        }
        catch (BudgetQueryInvalidException)
        {
            ApiFailureLog.InvalidRequest(
                response.HttpContext,
                ApiFailureOperation.BudgetExecutionExport);
            return SafeProblemResults.InvalidRequest();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.BudgetExecutionExport);
            // An export that cannot be produced whole is an outage like any
            // other, and the caller learns only that.
            return SafeProblemResults.BudgetExecutionUnavailable();
        }
    }

    private static BudgetQuery ParseQuery(HttpRequest request, bool includePaging) => new(
        includePaging ? OptionalGuid(request, "expenseRequestId") : null,
        OptionalGuid(request, "costCenterId"),
        OptionalGuid(request, "cashFlowItemId"),
        OptionalDate(request, "budgetMonth"),
        includePaging ? OptionalInt32(request, "skip") : null,
        includePaging ? OptionalInt32(request, "take") : null);

    private static bool HasRequestScopeCandidate(HttpRequest request) =>
        request.Query.TryGetValue("expenseRequestId", out var values)
        && values.Count == 1
        && !string.IsNullOrWhiteSpace(values[0]);

    private static Guid? OptionalGuid(HttpRequest request, string name)
    {
        var value = OptionalSingle(request, name);
        if (value is null)
        {
            return null;
        }

        return Guid.TryParse(value, out var result)
            ? result
            : throw new BudgetQueryInvalidException(name);
    }

    private static Guid? ExpectedFact(HttpRequest request)
    {
        if (request.Query.TryGetValue("expectedFactSnapshotId", out var values)
            && (values.Count != 1 || string.IsNullOrWhiteSpace(values[0])))
            throw new BudgetQueryInvalidException("expectedFactSnapshotId");
        var value = OptionalGuid(request, "expectedFactSnapshotId");
        if (value == Guid.Empty) throw new BudgetQueryInvalidException("expectedFactSnapshotId");
        return value;
    }

    private static bool CurrentStructure(HttpRequest request) => OptionalSingle(request, "structure") switch
    {
        null or "approved" => false, // Existing request-control clients retain their historical contract.
        "current" => true,
        _ => throw new BudgetQueryInvalidException("structure")
    };

    private static DateOnly? OptionalDate(HttpRequest request, string name)
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
            ? result
            : throw new BudgetQueryInvalidException(name);
    }

    private static int? OptionalInt32(HttpRequest request, string name)
    {
        var value = OptionalSingle(request, name);
        if (value is null)
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new BudgetQueryInvalidException(name);
    }

    private static string? OptionalSingle(HttpRequest request, string name)
    {
        if (!request.Query.TryGetValue(name, out var values) || values.Count == 0)
        {
            return null;
        }

        if (values.Count != 1)
        {
            throw new BudgetQueryInvalidException(name);
        }

        var value = values[0];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed record BudgetQuery(
        Guid? ExpenseRequestId,
        Guid? CostCenterId,
        Guid? CashFlowItemId,
        DateOnly? BudgetMonth,
        int? Skip,
        int? Take);

    /// <summary>
    /// Separates caller-owned query failures from ArgumentException instances
    /// raised by application, persistence or workbook code. Only this exact
    /// type is translated to the reviewed 400 response.
    /// </summary>
    private sealed class BudgetQueryInvalidException(string parameterName)
        : Exception($"The '{parameterName}' query value is invalid.");

    /// <summary>
    /// The composition of one budget position's current reserve: which expense
    /// requests hold it and how much of it each one holds.
    ///
    /// The key is required in full - a cost centre, a cash flow item and a
    /// month - because a reserve belongs to one exact position and to nothing
    /// wider. A malformed or missing part of that key never reaches the reader:
    /// model binding refuses it first.
    ///
    /// Only the current reserve is described. The paid part of a position is
    /// not broken down per request anywhere here: a payment fact carries a
    /// request and no budget key, a request's lines can span several keys, and
    /// no rule exists to divide one by the other.
    /// </summary>
    private static async Task<IResult> ListReservationsAsync(
        Guid costCenterId,
        Guid cashFlowItemId,
        DateOnly budgetMonth,
        int? skip,
        int? take,
        HttpRequest request,
        BudgetReservationService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            // Set before anything can fail, so the page, the refusal, the
            // absence and the outage are all uncacheable.
            response.Headers.CacheControl = "no-store";
            var page = await service.ReadAsync(
                costCenterId,
                cashFlowItemId,
                budgetMonth,
                skip ?? 0,
                take ?? 25,
                cancellationToken,
                ExpectedFact(request));
            return page is null ? Results.NotFound() : Results.Ok(page);
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (BudgetQueryInvalidException)
        {
            return SafeProblemResults.InvalidRequest();
        }
        catch (ExecutionFactVersionConflictException)
        {
            return SafeProblemResults.ExecutionFactChangedConflict();
        }
        catch (OperationCanceledException)
        {
            // A caller who went away is not a server failure.
            throw;
        }
        catch (Exception)
        {
            // A projection that does not add up is an outage of this answer,
            // and the caller learns only that. No provider text, no statement,
            // no identifier.
            return SafeProblemResults.BudgetReservationUnavailable();
        }
    }
}
