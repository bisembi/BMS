using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Application.Treasury;

/// <summary>Same dated scope and filters, deliberately without a page.</summary>
public sealed record DatedPaymentCalendarExportQuery(
    DateOnly From, DateOnly Through, string? StatusCode = null, string? CurrencyCode = null)
{
    public string Grouping { get; init; } = PaymentCalendarGroupingCodes.Date;

    internal DatedPaymentCalendarQuery ToReadQuery() =>
        new(From, Through, 0, 1, StatusCode, CurrencyCode) { Grouping = Grouping };
}

/// <summary>Business values only: no request, event, allocation or organization IDs.</summary>
public sealed record DatedPaymentCalendarExportRow(
    DateOnly BusinessDate,
    string Kind,
    string Status,
    string CurrencyCode,
    string? CounterpartyName,
    string? Purpose,
    string? OrganizationCode,
    string? OrganizationName,
    decimal? PlanAmount,
    decimal? PlanAmountKzt,
    decimal? SignedAmount,
    decimal? SignedAmountKzt);

/// <summary>A contiguous business section; FirstRowIndex is not a persisted identifier.</summary>
public sealed record DatedPaymentCalendarExportOrganizationGroup(
    int FirstRowIndex,
    int Total,
    bool HasSourceOrganization,
    string? OrganizationCode,
    string? OrganizationName,
    IReadOnlyList<DatedPaymentCalendarCurrencyTotalsDto> CurrencyTotals,
    DatedPaymentCalendarKztTotalsDto KztTotals);

public sealed record DatedPaymentCalendarExportData(
    DateTimeOffset GeneratedAtUtc,
    DatedPaymentCalendarExportQuery Query,
    string BusinessTimeZoneId,
    IReadOnlyList<DatedPaymentCalendarExportRow> Rows,
    IReadOnlyList<DatedPaymentCalendarCurrencyTotalsDto> CurrencyTotals,
    DatedPaymentCalendarKztTotalsDto KztTotals,
    IReadOnlyList<DatedPaymentCalendarExportOrganizationGroup> OrganizationGroups);

/// <summary>
/// The page and export consume the SAME authorized, bounded, fully reconciled
/// report. There is no second reader, repeated page loop, currency summation or
/// settlement cap here. Workbook rendering consumes only this business model.
/// </summary>
public sealed class DatedPaymentCalendarExportService(DatedPaymentCalendarService calendar, IClock clock)
{
    public async Task<DatedPaymentCalendarExportData> ExportAsync(
        DatedPaymentCalendarExportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        // Reauthorization, input validation, transaction retries, evidence
        // completeness and capacity checks remain in the common read service.
        var report = await calendar.ReadReportAsync(query.ToReadQuery(), cancellationToken);
        try
        {
            var rows = new List<DatedPaymentCalendarExportRow>(report.Rows.Count);
            foreach (var row in report.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                rows.Add(new DatedPaymentCalendarExportRow(
                    row.BusinessDate, row.Kind, row.Status, row.CurrencyCode,
                    row.CounterpartyName, row.Purpose, row.OrganizationCode, row.OrganizationName,
                    row.PlanAmount, row.PlanAmountKzt, row.SignedAmount, row.SignedAmountKzt));
            }

            var groups = new List<DatedPaymentCalendarExportOrganizationGroup>(report.OrganizationGroups.Count);
            var offset = 0;
            foreach (var group in report.OrganizationGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                groups.Add(new DatedPaymentCalendarExportOrganizationGroup(
                    offset, group.Total, group.OrganizationId.HasValue, group.OrganizationCode,
                    group.OrganizationName, group.CurrencyTotals, group.KztTotals));
                offset = checked(offset + group.Total);
            }

            if (query.Grouping == PaymentCalendarGroupingCodes.Organization && offset != rows.Count)
            {
                throw new PaymentCalendarUnavailableException();
            }
            var generated = clock.UtcNow;
            cancellationToken.ThrowIfCancellationRequested();
            return new DatedPaymentCalendarExportData(generated, query, report.BusinessTimeZoneId,
                rows.AsReadOnly(), report.CurrencyTotals, report.KztTotals, groups.AsReadOnly());
        }
        catch (Exception exception) when (!ProcessFatalExceptionClassifier.IsFatal(exception)
            && exception is not (OperationCanceledException or PaymentCalendarUnavailableException))
        {
            throw new PaymentCalendarUnavailableException(exception);
        }
    }
}
