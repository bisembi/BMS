using Bms.Domain.ExpenseRequests;
using Bms.Api.Diagnostics;
using Bms.Api.Exports;
using Bms.Api.Problems;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;

namespace Bms.Api.Endpoints;

/// The spreadsheet export and the submission preflight read.
public static partial class ExpenseRequestEndpoints
{

    internal static async Task<IResult> ExportExcelAsync(
        bool? isUrgent,
        DateOnly? plannedPaymentDate,
        Guid? organizationId,
        string? status,
        Guid? costCenterId,
        Guid? counterpartyId,
        string? initiatorExternalId,
        bool? onlyMyTasks,
        DateTimeOffset? createdFromUtc,
        DateTimeOffset? createdBeforeUtc,
        ExpenseRequestExportService service,
        HttpResponse response,
        CancellationToken cancellationToken,
        string? search = null,
        Guid? initiatorEmployeeId = null,
        bool? hideDrafts = null)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            var export = await service.ExportAsync(
                new ExpenseRequestFilter(
                    IsUrgent: isUrgent,
                    PlannedPaymentDate: plannedPaymentDate,
                    OrganizationId: organizationId,
                    Status: ParseStatusFilter(status),
                    CostCenterId: costCenterId,
                    CounterpartyId: counterpartyId,
                    InitiatorExternalId: initiatorExternalId,
                    OnlyMyTasks: onlyMyTasks ?? false,
                    CreatedFromUtc: createdFromUtc,
                    CreatedBeforeUtc: createdBeforeUtc) { Search = search, InitiatorEmployeeId = initiatorEmployeeId, HideDrafts = hideDrafts ?? false },
                cancellationToken);
            return Results.File(
                ExpenseRequestExcelExporter.CreateStream(export, cancellationToken),
                ExpenseRequestExcelExporter.ContentType,
                ExpenseRequestExcelExporter.FileName(export.GeneratedAtUtc),
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
        catch (ExcelExportPrecisionException)
        {
            return SafeProblemResults.ExcelExportPrecision();
        }
        catch (ArgumentOutOfRangeException)
        {
            ApiFailureLog.InvalidRequest(
                response.HttpContext,
                ApiFailureOperation.ExpenseRequestExport);
            return SafeProblemResults.InvalidRequest();
        }
        catch (ArgumentException)
        {
            ApiFailureLog.InvalidRequest(
                response.HttpContext,
                ApiFailureOperation.ExpenseRequestExport);
            return SafeProblemResults.InvalidRequest();
        }
    }

    internal static async Task<IResult> GetSubmissionPreflightAsync(
        Guid id,
        ExpenseRequestSubmissionPreflightService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            var result = await service.EvaluateAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (ActorNotAuthenticatedException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                detail: "Authentication is required.");
        }
        catch (SubmissionPreflightUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.ExpenseRequestSubmissionPreflight);
            return SafeProblemResults.SubmissionPreflightUnavailable(
                response.HttpContext.TraceIdentifier);
        }
    }
}
