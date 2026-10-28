using Bms.Application.Budgeting;
using Bms.Domain.Budgeting;

namespace Bms.Application.Abstractions;

// Internal financial controls for authorized keys. Never serialize hidden-card
// counts, identifiers, purposes, dates or status to a public response/workbook.
public sealed record BudgetReservationExportTotal(BudgetKey Key, int RequestCount, int LineCount, decimal ReservedAmountKzt);

public sealed record BudgetReservationExportCapture(
    IReadOnlyList<BudgetReservationExportRow> Rows,
    IReadOnlyList<BudgetReservationExportTotal> CompleteTotals)
{
    // Compatibility for unrestricted readers only. A finite scope must implement
    // its own complete capture rather than infer a financial total from metadata.
    public static BudgetReservationExportCapture FromCompleteRows(
        IReadOnlyList<BudgetReservationExportRow> rows, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (rows.Count > BudgetExecutionExportService.MaximumWorksheetRows - 8)
            throw new BudgetExecutionExportIncompleteException("The filtered area does not fit a worksheet. Narrow the filters.");
        if (rows.Count > BudgetExecutionExportPolicy.MaximumRows)
            throw new BudgetExecutionExportTooLargeException(rows.Count, BudgetExecutionExportPolicy.MaximumRows);
        var totals = new Dictionary<BudgetKey, (int Requests, int Lines, decimal Amount)>();
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previous = totals.GetValueOrDefault(row.Key);
            totals[row.Key] = (checked(previous.Requests + 1), checked(previous.Lines + row.LineCount),
                previous.Amount + row.ReservedAmountKzt);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(rows, totals.Select(pair => new BudgetReservationExportTotal(pair.Key,
            pair.Value.Requests, pair.Value.Lines, pair.Value.Amount)).ToArray());
    }
}
