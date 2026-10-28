using Bms.Api.Problems;
using Bms.Api.Exports;
using Bms.Application.Common;
using Bms.Application.Treasury;
using Bms.Application.Abstractions;

namespace Bms.Api.Endpoints;

public static class GoodsCalendarEndpoints458
{
    public static void MapGoodsCalendar458(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/treasury/goods-calendar/grid", (DateOnly? from, DateOnly? through, string? status,
            GoodsCalendarService458 service, HttpRequest request, HttpResponse response, CancellationToken token) =>
            Read(from, through, status, service, null, request, response, token)).WithTags("Treasury");
        endpoints.MapGet("/api/v1/treasury/goods-calendar/grid/export.xlsx", (DateOnly? from, DateOnly? through, string? status,
            GoodsCalendarService458 service, IClock clock, HttpRequest request, HttpResponse response, CancellationToken token) =>
            Read(from, through, status, service, clock, request, response, token)).WithTags("Treasury");
    }

    private static async Task<IResult> Read(DateOnly? from, DateOnly? through, string? status,
        GoodsCalendarService458 service, IClock? clock, HttpRequest request, HttpResponse response, CancellationToken token)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            if (from is not { } first || through is not { } last
                || request.Query.Keys.Any(k => k is not ("from" or "through" or "status"))
                || request.Query.Any(pair => pair.Value.Count != 1)) throw new InvalidPaymentCalendarQueryException();
            var result = await service.GetAsync(new(first, last, status), token);
            if (clock is null) return Results.Ok(result);
            var now = clock.UtcNow;
            if (now == default || now.Offset != TimeSpan.Zero) throw new PaymentCalendarUnavailableException();
            return Results.File(GoodsCalendarExcel458.CreateStream(result, token), DatedPaymentCalendarExcelExporter.ContentType,
                $"goods-calendar-{now:yyyyMMdd-HHmmss}Z.xlsx", enableRangeProcessing: false);
        }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (InvalidPaymentCalendarQueryException) { return SafeProblemResults.InvalidRequest(); }
        catch (DatedPaymentCalendarScopeTooLargeException) { return SafeProblemResults.DatedPaymentCalendarScopeTooLarge(); }
        catch (PaymentCalendarExcelPrecisionException) { return SafeProblemResults.PaymentCalendarExportPrecision(); }
        catch (PaymentCalendarUnavailableException) { return SafeProblemResults.PaymentCalendarUnavailable(); }
    }
}
