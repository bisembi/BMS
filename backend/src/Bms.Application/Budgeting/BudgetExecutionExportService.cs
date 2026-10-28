using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.Budgeting;

/// <summary>
/// The budget execution area prepared for a spreadsheet: every row the filter
/// matches, in the order the list pages them, and the totals of that same area.
/// </summary>
public sealed record BudgetExecutionExportData(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<BudgetExecutionExportRow> Rows,
    BudgetPositionTotalsDto Totals,
    IReadOnlyList<BudgetReservationExportRow> Reservations)
{
    public ExecutionFactSourceDto? FactSource { get; init; }
}

/// <summary>
/// A technical boundary for the synchronous workbook endpoint. The workbook
/// contains ten cells per data row and is built before the response starts;
/// accepting an unbounded area would let one request consume the process.
/// </summary>
public static class BudgetExecutionExportPolicy
{
    public const int MaximumRows = 100_000;
}

/// <summary>
/// Reads the whole filtered execution area for export.
///
/// It answers to exactly the people the execution list already answers to, uses
/// exactly the filters the list uses, and sums the area by exactly the rule the
/// list sums it by. The one thing it does differently is that it does not page:
/// an export that returned a page would describe less than it claims.
/// </summary>
public sealed class BudgetExecutionExportService(
    IBudgetRepository budgetRepository,
    IBudgetReservationReader reservationReader,
    ICurrentActor currentActor,
    IClock clock,
    IBusinessTransactionRunner transactionRunner,
    IExecutionReportReader? reportReader = null)
{
    /// <summary>
    /// A worksheet cannot hold more rows than this, and a file that quietly
    /// stopped short would be worse than no file. Both sheets are checked
    /// against it with room for their header and their totals.
    /// </summary>
    internal const int MaximumWorksheetRows = 1_048_576;

    public async Task<BudgetExecutionExportData> ExportAsync(
        Guid? costCenterId,
        Guid? cashFlowItemId,
        DateOnly? budgetMonth,
        CancellationToken cancellationToken,
        Guid? expectedFactSnapshotId = null,
        bool currentStructure = false)
    {
        ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (currentStructure && reportReader is null) throw new NotSupportedException();
        if (expectedFactSnapshotId.HasValue && reportReader is null) throw new ExecutionFactVersionConflictException();
        cancellationToken.ThrowIfCancellationRequested();
        DateOnly? normalizedMonth = budgetMonth.HasValue
            ? new DateOnly(budgetMonth.Value.Year, budgetMonth.Value.Month, 1)
            : null;

        // Amount reconciliation alone cannot detect a catalogue rename between
        // the two readers. Capture both sheets in one retryable transaction,
        // including materialization; XLSX is built after that transaction ends.
        return await transactionRunner.ExecuteAsync(
            async attemptCancellationToken =>
            {
                var scope = await currentActor.ResolveBusinessDataReadScopeAsync(attemptCancellationToken);
                if (!scope.HasAny || (costCenterId.HasValue && !scope.Contains(costCenterId.Value)))
                    throw new ActorForbiddenException();
                var slice = reportReader is null ? await budgetRepository.ReadExecutionExportAsync(
                    costCenterId,
                    cashFlowItemId,
                    normalizedMonth,
                    scope,
                    attemptCancellationToken)
                    : currentStructure
                        ? await reportReader.ReadCurrentExportAsync(costCenterId, cashFlowItemId, normalizedMonth, scope, attemptCancellationToken, expectedFactSnapshotId)
                        : await reportReader.ReadExportAsync(costCenterId, cashFlowItemId, normalizedMonth, scope, attemptCancellationToken, expectedFactSnapshotId);
                attemptCancellationToken.ThrowIfCancellationRequested();

                if (slice.Rows.Count != slice.Total)
                {
                    // A partial area must never become a complete-looking file.
                    throw new BudgetExecutionExportIncompleteException();
                }

                if (slice.Total > BudgetExecutionExportPolicy.MaximumRows)
                {
                    throw new BudgetExecutionExportTooLargeException(
                        slice.Total,
                        BudgetExecutionExportPolicy.MaximumRows);
                }

                var reservationCapture = await reservationReader.ReadCaptureForExportAsync(
                    currentStructure ? null : costCenterId,
                    cashFlowItemId,
                    normalizedMonth,
                    scope,
                    attemptCancellationToken);
                attemptCancellationToken.ThrowIfCancellationRequested();
                var reconciliationRows = slice.ReconciliationRows ?? slice.Rows;
                if (currentStructure)
                {
                    if (reservationCapture.Rows.Count > BudgetExecutionExportPolicy.MaximumRows
                        || reservationCapture.CompleteTotals.Count > BudgetExecutionExportPolicy.MaximumRows)
                        throw new BudgetExecutionExportTooLargeException(Math.Max(reservationCapture.Rows.Count,
                            reservationCapture.CompleteTotals.Count), BudgetExecutionExportPolicy.MaximumRows);
                    if (slice.ReconciliationRows is null) throw new BudgetExecutionExportIncompleteException();
                    var keys = reconciliationRows.Where(r => r.CostCenterId != Guid.Empty).Select(r => r.Key).ToHashSet();
                    reservationCapture = new(reservationCapture.Rows.Where(r => keys.Contains(r.Key)).ToArray(),
                        reservationCapture.CompleteTotals.Where(r => keys.Contains(r.Key)).ToArray());
                }
                // Reconcile also refuses oversized collections before either
                // sheet is enumerated or copied for the workbook.
                var reservations = reservationCapture.Rows;
                Reconcile(reconciliationRows, reservationCapture, attemptCancellationToken);
                var capturedPositions = slice.Rows.ToArray();
                attemptCancellationToken.ThrowIfCancellationRequested();
                var capturedReservations = reservations.ToArray();
                attemptCancellationToken.ThrowIfCancellationRequested();

                return new BudgetExecutionExportData(
                    clock.UtcNow,
                    capturedPositions,
                    BudgetPositionService.BuildTotals(
                        slice.Total,
                        slice.ConfiguredLimitCount,
                        slice.UnconfiguredLimitCount,
                        slice.ConfiguredLimitAmountKzt,
                        slice.ReservedAmountKzt,
                        slice.PaidAmountKzt),
                    capturedReservations) { FactSource = slice.FactSource };
            },
            cancellationToken);
    }

    /// <summary>
    /// The two sheets have to describe the same thing. Every position reserves
    /// exactly what its reserving requests hold, every reserving request
    /// belongs to a position that is in the file, and neither sheet may be
    /// longer than a worksheet can hold.
    ///
    /// Any failure here means the workbook would be internally inconsistent, so
    /// none is produced at all.
    /// </summary>
    private static void Reconcile(
        IReadOnlyList<BudgetExecutionExportRow> positions,
        BudgetReservationExportCapture capture,
        CancellationToken cancellationToken)
    {
        var reservations = capture.Rows;
        if (positions.Count + HeaderAndTotalsRows > MaximumWorksheetRows
            || reservations.Count + HeaderAndTotalsRows > MaximumWorksheetRows)
        {
            throw new BudgetExecutionExportIncompleteException(
                "The filtered area does not fit a worksheet. Narrow the filters.");
        }

        if (reservations.Count > BudgetExecutionExportPolicy.MaximumRows)
        {
            throw new BudgetExecutionExportTooLargeException(
                reservations.Count,
                BudgetExecutionExportPolicy.MaximumRows);
        }

        var reservedByKey = new Dictionary<BudgetKey, decimal>();
        foreach (var reservation in reservations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ExpenseRequestStatusCode.TryParse(reservation.Status, out _)
                || reservation.LineCount < 1 || reservation.ReservedAmountKzt <= 0m)
            {
                throw new BudgetProjectionDriftException(
                    "A reserving request carries a status outside the published set.");
            }

            reservedByKey[reservation.Key] =
                reservedByKey.GetValueOrDefault(reservation.Key) + reservation.ReservedAmountKzt;
        }

        if (capture.CompleteTotals.Count > BudgetExecutionExportPolicy.MaximumRows)
            throw new BudgetExecutionExportTooLargeException(capture.CompleteTotals.Count, BudgetExecutionExportPolicy.MaximumRows);
        var completeByKey = new Dictionary<BudgetKey, BudgetReservationExportTotal>();
        foreach (var total in capture.CompleteTotals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (total.RequestCount < 0 || total.LineCount < total.RequestCount
                || total.ReservedAmountKzt < 0m
                || (total.RequestCount == 0 && (total.LineCount != 0 || total.ReservedAmountKzt != 0m))
                || (total.RequestCount > 0 && total.ReservedAmountKzt <= 0m)
                || !completeByKey.TryAdd(total.Key, total))
                throw new BudgetProjectionDriftException("The complete reservation financial controls are inconsistent.");
        }

        var keys = new HashSet<BudgetKey>();
        foreach (var position in positions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position.CostCenterId == Guid.Empty && position.IsCostCenterUnassigned)
            {
                // Fact-only display bucket cannot reserve or acquire an approved limit.
                if (position.ReservedAmountKzt != 0m || position.LimitAmountKzt != 0m)
                    throw new BudgetProjectionDriftException("An unassigned fact row carries a control amount.");
                continue;
            }
            if (!keys.Add(position.Key))
                throw new BudgetProjectionDriftException("A budget key was repeated in the exported area.");
            var complete = completeByKey.GetValueOrDefault(position.Key);
            var fullReserved = complete?.ReservedAmountKzt ?? 0m;
            if (fullReserved != position.ReservedAmountKzt
                || reservedByKey.GetValueOrDefault(position.Key) > fullReserved)
            {
                throw new BudgetProjectionDriftException(
                    "A budget position reserves an amount its requests do not account for.");
            }
        }

        foreach (var key in reservedByKey.Keys.Concat(completeByKey.Keys))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!keys.Contains(key))
            {
                throw new BudgetProjectionDriftException(
                    "A reserving request belongs to a position outside the exported area.");
            }
        }
    }

    /// One header row and the block of totals under it.
    private const int HeaderAndTotalsRows = 8;
}

/// <summary>
/// Raised when the export read returned fewer rows than the area it counted.
/// The caller learns that the file could not be produced and nothing else.
/// </summary>
public sealed class BudgetExecutionExportIncompleteException : Exception
{
    public BudgetExecutionExportIncompleteException()
        : base("The budget execution export did not cover the whole filtered area.")
    {
    }

    public BudgetExecutionExportIncompleteException(string message)
        : base(message)
    {
    }

    public BudgetExecutionExportIncompleteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised before a workbook is materialized when the filtered area exceeds
/// the reviewed technical boundary. The HTTP caller receives only a fixed
/// instruction to narrow the filters.
/// </summary>
public sealed class BudgetExecutionExportTooLargeException : Exception
{
    public BudgetExecutionExportTooLargeException(int actualRows, int maximumRows)
        : base($"The budget execution export contains {actualRows} rows; maximum is {maximumRows}.")
    {
        ActualRows = actualRows;
        MaximumRows = maximumRows;
    }

    public int ActualRows { get; }

    public int MaximumRows { get; }
}
