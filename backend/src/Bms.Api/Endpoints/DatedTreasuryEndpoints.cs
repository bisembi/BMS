using Bms.Api.Diagnostics;
using Bms.Api.Exports;
using Bms.Api.Problems;
using Bms.Application.Common;
using Bms.Application.Treasury;

namespace Bms.Api.Endpoints;

/// <summary>
/// Additive read-only dated movements; never replaces the existing capped
/// settlement view and never accepts payment facts or other business writes.
/// </summary>
public static class DatedTreasuryEndpoints
{
    public static IEndpointRouteBuilder MapDatedTreasuryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGoodsCalendar458();
        var group = endpoints.MapGroup("/api/v1/treasury").WithTags("Treasury");
        group.MapGet("/payment-calendar/dated", GetDatedPaymentCalendarAsync)
            .WithName("GetDatedTreasuryPaymentCalendar");
        group.MapGet("/payment-calendar/dated/export.xlsx", ExportDatedPaymentCalendarAsync)
            .WithName("ExportDatedTreasuryPaymentCalendar");
        group.MapGet("/payment-calendar/grid", GetGridAsync).WithName("GetTreasuryPaymentCalendarGrid");
        group.MapGet("/payment-calendar/grid/export.xlsx", ExportGridAsync).WithName("ExportTreasuryPaymentCalendarGrid");
        return endpoints;
    }

    private static async Task<IResult> GetGridAsync(DateOnly? from, DateOnly? through, string? status,
        DatedPaymentCalendarService service, HttpRequest request, HttpResponse response, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            if (from is not { } first || through is not { } last
                || request.Query.Keys.Any(key => key is not ("from" or "through" or "status"))
                || request.Query.Any(pair => pair.Value.Count != 1)) throw new InvalidPaymentCalendarQueryException();
            return Results.Ok(await service.GetGridAsync(new(first, last, status), cancellationToken));
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (InvalidPaymentCalendarQueryException) { return SafeProblemResults.InvalidRequest(); }
        catch (DatedPaymentCalendarScopeTooLargeException) { return SafeProblemResults.DatedPaymentCalendarScopeTooLarge(); }
        catch (PaymentCalendarUnavailableException) { return SafeProblemResults.PaymentCalendarUnavailable(); }
    }

    private static async Task<IResult> ExportGridAsync(DateOnly? from, DateOnly? through, string? status,
        DatedPaymentCalendarService service, Bms.Application.Abstractions.IClock clock,
        HttpRequest request, HttpResponse response, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            if (from is not { } first || through is not { } last
                || request.Query.Keys.Any(key => key is not ("from" or "through" or "status"))
                || request.Query.Any(pair => pair.Value.Count != 1)) throw new InvalidPaymentCalendarQueryException();
            var result = await service.GetGridAsync(new(first, last, status), cancellationToken);
            var now = clock.UtcNow;
            if (now == default || now.Offset != TimeSpan.Zero) throw new PaymentCalendarUnavailableException();
            var name = "payment-calendar-grid-" + now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "Z.xlsx";
            return Results.File(PaymentCalendarGridExcelExporter434.CreateStream(result, cancellationToken),
                DatedPaymentCalendarExcelExporter.ContentType, name, enableRangeProcessing: false);
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (InvalidPaymentCalendarQueryException) { return SafeProblemResults.InvalidRequest(); }
        catch (DatedPaymentCalendarScopeTooLargeException) { return SafeProblemResults.DatedPaymentCalendarScopeTooLarge(); }
        catch (PaymentCalendarExcelPrecisionException) { return SafeProblemResults.PaymentCalendarExportPrecision(); }
        catch (PaymentCalendarUnavailableException) { return SafeProblemResults.PaymentCalendarUnavailable(); }
    }

    private static async Task<IResult> GetDatedPaymentCalendarAsync(
        DateOnly? from,
        DateOnly? through,
        int? skip,
        int? take,
        string? status,
        string? currency,
        string? grouping,
        DatedPaymentCalendarService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            if (from is not { } periodFrom || through is not { } periodThrough)
                throw new InvalidPaymentCalendarQueryException();
            var page = await service.GetAsync(new DatedPaymentCalendarQuery(
                periodFrom, periodThrough, skip ?? 0, take ?? 50, status, currency)
            {
                Grouping = grouping ?? PaymentCalendarGroupingCodes.Date,
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Never serialize the internal capture or full report.
            return Results.Ok(page);
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (InvalidPaymentCalendarQueryException) { return SafeProblemResults.InvalidRequest(); }
        catch (DatedPaymentCalendarScopeTooLargeException) { return SafeProblemResults.DatedPaymentCalendarScopeTooLarge(); }
        catch (PaymentCalendarUnavailableException) { return SafeProblemResults.PaymentCalendarUnavailable(); }
    }

    private static async Task<IResult> ExportDatedPaymentCalendarAsync(
        DateOnly? from,
        DateOnly? through,
        string? status,
        string? currency,
        string? grouping,
        DatedPaymentCalendarExportService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            if (from is not { } periodFrom || through is not { } periodThrough)
                throw new InvalidPaymentCalendarQueryException();
            var export = await service.ExportAsync(new DatedPaymentCalendarExportQuery(
                periodFrom, periodThrough, status, currency)
            {
                Grouping = grouping ?? PaymentCalendarGroupingCodes.Date,
            }, cancellationToken);
            // Render completely before starting an HTTP file response. Paging
            // is deliberately absent: this exports the whole authorized report.
            return Results.File(
                DatedPaymentCalendarExcelExporter.CreateStream(export, cancellationToken),
                DatedPaymentCalendarExcelExporter.ContentType,
                DatedPaymentCalendarExcelExporter.FileName(export.GeneratedAtUtc),
                enableRangeProcessing: false);
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (InvalidPaymentCalendarQueryException) { return SafeProblemResults.InvalidRequest(); }
        catch (DatedPaymentCalendarScopeTooLargeException) { return SafeProblemResults.DatedPaymentCalendarScopeTooLarge(); }
        catch (PaymentCalendarExcelPrecisionException) { return SafeProblemResults.PaymentCalendarExportPrecision(); }
        catch (Exception exception) when (exception is not OperationCanceledException
            && !ApiProcessFatalExceptionClassifier.IsFatal(exception))
        {
            return SafeProblemResults.PaymentCalendarUnavailable();
        }
    }
}
