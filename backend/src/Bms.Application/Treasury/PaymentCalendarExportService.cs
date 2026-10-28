using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Application.Treasury;

/// <summary>
/// The calendar's inclusive period and optional status without a page boundary.
/// Validation is deliberately shared with its existing public read contract.
/// </summary>
public sealed record PaymentCalendarExportQuery(
    DateOnly From,
    DateOnly Through,
    string? StatusCode)
{
    public string Grouping { get; init; } = PaymentCalendarGroupingCodes.Date;

    public void Validate() =>
        new PaymentCalendarQuery(From, Through, 0, 1, StatusCode) { Grouping = Grouping }.Validate();
}

/// <summary>
/// Returns the whole authorised calendar capture: rows, count and period totals
/// must describe the same captured scope, not independently fetched pages.
/// </summary>
public interface IPaymentCalendarExportReader
{
    Task<PaymentCalendarReadResult> ReadForExportAsync(
        string actorExternalId,
        bool includeAll,
        PaymentCalendarExportQuery query,
        CancellationToken cancellationToken);
}

/// <summary>A worksheet row contains business values only, never request IDs.</summary>
public sealed record PaymentCalendarExportRow(
    DateOnly PlannedPaymentDate,
    string Status,
    string CurrencyCode,
    string? CounterpartyName,
    string? Purpose,
    decimal ObligationAmount,
    decimal PaidAmount,
    decimal RemainingAmount,
    string Progress)
{
    public string? OrganizationCode { get; init; }
    public string? OrganizationName { get; init; }
    public decimal AppliedOverpaymentAmount { get; init; }
    public decimal ApprovedRoundingAmount { get; init; }
    public decimal BankPaidAmount => PaidAmount - AppliedOverpaymentAmount - ApprovedRoundingAmount;
}

public sealed record PaymentCalendarExportData(
    DateTimeOffset GeneratedAtUtc,
    PaymentCalendarExportQuery Query,
    IReadOnlyList<PaymentCalendarExportRow> Rows,
    PaymentCalendarTotalsDto Totals)
{
    public IReadOnlyList<PaymentCalendarExportOrganizationGroup> OrganizationGroups { get; init; } = [];
}

/// <summary>
/// A contiguous section of the exported rows. FirstRowIndex is zero-based,
/// not an Excel coordinate; no technical organization ID enters the workbook.
/// </summary>
public sealed record PaymentCalendarExportOrganizationGroup(
    int FirstRowIndex,
    int Total,
    bool HasSourceOrganization,
    string? OrganizationCode,
    string? OrganizationName,
    PaymentCalendarTotalsDto Totals);

/// <summary>
/// A synchronous workbook has a bounded memory cost. This matches the existing
/// budget workbook's technical ceiling; exceeding it refuses, never truncates.
/// </summary>
public static class PaymentCalendarExportPolicy
{
    public const int MaximumRows = 100_000;
}

/// <summary>
/// Exports the existing calendar, not a second payment model. Authorisation,
/// settlement projection and filters match the page; the whole capture is
/// reconciled before any workbook can be produced. No payment is recorded.
/// </summary>
public sealed class PaymentCalendarExportService(
    IPaymentCalendarExportReader reader,
    ICurrentActor currentActor,
    IClock clock)
{
    public async Task<PaymentCalendarExportData> ExportAsync(
        PaymentCalendarExportQuery query,
        CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var capture = await reader.ReadForExportAsync(
                actorExternalId, currentActor.HasFullBusinessDataAccess, query, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            ArgumentNullException.ThrowIfNull(capture);
            ArgumentNullException.ThrowIfNull(capture.Rows);
            ArgumentNullException.ThrowIfNull(capture.Totals);
            if (capture.Total > PaymentCalendarExportPolicy.MaximumRows
                || capture.Rows.Count > PaymentCalendarExportPolicy.MaximumRows)
            {
                throw new PaymentCalendarExportTooLargeException();
            }

            if (capture.Total < 0 || capture.Rows.Count != capture.Total)
            {
                throw new PaymentCalendarUnavailableException();
            }

            var seen = new HashSet<Guid>();
            var rows = new List<PaymentCalendarExportRow>(capture.Total);
            decimal obligation = 0, paid = 0, remaining = 0, overpaid = 0, applied = 0, rounding = 0;
            foreach (var row in capture.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row is null
                    || row.ExpenseRequestId == Guid.Empty
                    || !seen.Add(row.ExpenseRequestId)
                    || row.PlannedPaymentDate < query.From
                    || row.PlannedPaymentDate > query.Through
                    || (query.StatusCode is not null && row.Status != query.StatusCode)
                    || decimal.Round(row.ObligationAmount, 2) != row.ObligationAmount
                    || decimal.Round(row.ConfirmedAmount, 2) != row.ConfirmedAmount)
                {
                    throw new PaymentCalendarUnavailableException();
                }

                // This is the same projection the page uses. Whether a card is
                // openable does not remove an already authorised calendar row;
                // the workbook exposes neither a card link nor its internal ID.
                var projected = PaymentCalendarService.ToDto(row, canOpen: false);
                rows.Add(new PaymentCalendarExportRow(
                    projected.PlannedPaymentDate, projected.Status, projected.CurrencyCode,
                    projected.CounterpartyName, projected.Purpose, projected.ObligationAmount,
                    projected.PaidAmount, projected.RemainingAmount, projected.Progress)
                {
                    OrganizationCode = projected.OrganizationCode,
                    OrganizationName = projected.OrganizationName,
                    AppliedOverpaymentAmount = projected.AppliedOverpaymentAmount,
                    ApprovedRoundingAmount = projected.ApprovedRoundingAmount,
                });

                obligation += projected.ObligationAmount;
                paid += projected.PaidAmount;
                remaining += projected.RemainingAmount;
                overpaid += row.ConfirmedAmount - projected.PaidAmount;
                applied += projected.AppliedOverpaymentAmount;
                rounding += projected.ApprovedRoundingAmount;
            }

            var totals = new PaymentCalendarTotals(obligation, paid, remaining, overpaid)
            {
                AppliedOverpaymentAmount = applied,
                ApprovedRoundingAmount = rounding,
            };
            if (rows.Count != capture.Total || totals != capture.Totals)
            {
                // Arithmetic consistency inside a totals DTO is insufficient:
                // it must equal the actual rows, including per-request excess.
                throw new PaymentCalendarUnavailableException();
            }

            var groups = PaymentCalendarService.ToOrganizationGroups(capture, query.Grouping, cancellationToken);
            var exportGroups = new List<PaymentCalendarExportOrganizationGroup>(groups.Count);
            var firstRowIndex = 0;
            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                exportGroups.Add(new PaymentCalendarExportOrganizationGroup(
                    firstRowIndex, group.Total, group.OrganizationId.HasValue,
                    group.OrganizationCode, group.OrganizationName, group.Totals));
                firstRowIndex += group.Total;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new PaymentCalendarExportData(
                clock.UtcNow, query, rows.ToArray(),
                new PaymentCalendarTotalsDto(obligation, paid, remaining, overpaid)
                {
                    AppliedOverpaymentAmount = applied,
                    ApprovedRoundingAmount = rounding,
                })
            {
                OrganizationGroups = exportGroups,
            };
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException
            and not ActorNotAuthenticatedException
            and not ActorForbiddenException
            and not InvalidPaymentCalendarQueryException
            and not PaymentCalendarUnavailableException
            and not PaymentCalendarExportTooLargeException
            && !ProcessFatalExceptionClassifier.IsFatal(exception))
        {
            throw new PaymentCalendarUnavailableException(exception);
        }
    }
}

/// <summary>Refuses an oversized capture before building or publishing a file.</summary>
public sealed class PaymentCalendarExportTooLargeException : Exception
{
    public PaymentCalendarExportTooLargeException()
        : base("The payment calendar export is too large. Narrow the filters and try again.")
    {
    }
}
