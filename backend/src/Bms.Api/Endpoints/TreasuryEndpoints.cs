using Bms.Api.Diagnostics;
using Bms.Api.Exports;
using Bms.Api.Problems;
using Bms.Application.Common;
using Bms.Application.Treasury;

namespace Bms.Api.Endpoints;

/// <summary>
/// Read-only treasury surface. There is deliberately no POST, PUT, PATCH or
/// DELETE here: a confirmed payment fact can only arrive from the accounting
/// circuit, never from this API.
/// </summary>
public static class TreasuryEndpoints
{
    public static IEndpointRouteBuilder MapTreasuryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/treasury").WithTags("Treasury");
        group.MapGet("/payment-calendar", GetPaymentCalendarAsync)
            .WithName("GetTreasuryPaymentCalendar");
        group.MapGet("/payment-calendar/export.xlsx", ExportPaymentCalendarAsync)
            .WithName("ExportTreasuryPaymentCalendar");
        return endpoints;
    }

    private static async Task<IResult> GetPaymentCalendarAsync(
        DateOnly? from,
        DateOnly? through,
        int? skip,
        int? take,
        string? status,
        string? grouping,
        PaymentCalendarService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            response.Headers.CacheControl = "no-store";
            if (from is not { } periodFrom || through is not { } periodThrough)
            {
                throw new InvalidPaymentCalendarQueryException();
            }

            var result = await service.GetAsync(
                new PaymentCalendarQuery(
                    periodFrom,
                    periodThrough,
                    skip ?? 0,
                    take ?? 50,
                    status)
                {
                    Grouping = grouping ?? PaymentCalendarGroupingCodes.Date,
                },
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
        catch (InvalidPaymentCalendarQueryException)
        {
            return SafeProblemResults.InvalidRequest();
        }
        catch (PaymentCalendarUnavailableException)
        {
            return SafeProblemResults.PaymentCalendarUnavailable();
        }
    }

    /// <summary>
    /// The whole authorised calendar capture, not the currently visible page.
    /// The workbook is fully built before the file response starts.
    /// </summary>
    private static async Task<IResult> ExportPaymentCalendarAsync(
        DateOnly? from,
        DateOnly? through,
        string? status,
        string? grouping,
        PaymentCalendarExportService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            if (from is not { } periodFrom || through is not { } periodThrough)
            {
                throw new InvalidPaymentCalendarQueryException();
            }

            var export = await service.ExportAsync(
                new PaymentCalendarExportQuery(periodFrom, periodThrough, status)
                {
                    Grouping = grouping ?? PaymentCalendarGroupingCodes.Date,
                }, cancellationToken);
            return Results.File(
                PaymentCalendarExcelExporter.CreateStream(export, cancellationToken),
                PaymentCalendarExcelExporter.ContentType,
                PaymentCalendarExcelExporter.FileName(export.GeneratedAtUtc),
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
        catch (InvalidPaymentCalendarQueryException)
        {
            return SafeProblemResults.InvalidRequest();
        }
        catch (PaymentCalendarExportTooLargeException)
        {
            return SafeProblemResults.PaymentCalendarExportTooLarge();
        }
        catch (PaymentCalendarExcelPrecisionException)
        {
            return SafeProblemResults.PaymentCalendarExportPrecision();
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException
            && !ApiProcessFatalExceptionClassifier.IsFatal(exception))
        {
            // Formatting and storage failures cannot publish a partial file or
            // leak provider/XML diagnostics. Cancellation and fatal failures
            // keep their own propagation semantics.
            return SafeProblemResults.PaymentCalendarUnavailable();
        }
    }
}
