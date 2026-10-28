using Bms.Domain.Budgeting;
using Bms.Application.Authorization;

namespace Bms.Application.Abstractions;

/// <summary>
/// One expense request as the reservation reader returns it, before anything is
/// published. The identifier is carried here because the reader needs a
/// deterministic tie-break for ordering; the public contract drops it.
///
/// The planned payment date is not optional here even though the column is:
/// the budget month of a projection is derived from that date, so a reserving
/// request always has one. A row without it is drift, and the reader refuses it
/// rather than publishing a participant with no date.
/// </summary>
public sealed record BudgetReservationParticipant(
    Guid ExpenseRequestId,
    string Status,
    string? Purpose,
    DateOnly PlannedPaymentDate,
    int LineCount,
    decimal ReservedAmountKzt);

/// <summary>
/// One read of a budget position and the composition of its reserve: the
/// position itself, the count and sums over every participant, and the
/// requested window of them - all captured together, so the page can never
/// describe a different area than the totals beside it.
/// </summary>
public sealed record BudgetReservationSlice(
    decimal? LimitAmountKzt,
    decimal ReservedAmountKzt,
    decimal PaidAmountKzt,
    IReadOnlyList<BudgetReservationParticipant> Page,
    int Total,
    int TotalLineCount,
    decimal TotalReservedAmountKzt)
{
    // Counts and page describe visible whole cards; the monetary total above
    // describes every projection of the authorized position, even a hidden card.
    public decimal VisibleReservedAmountKzt { get; init; } = TotalReservedAmountKzt;
}

/// <summary>
/// One reserving request of one budget position, prepared for a spreadsheet.
/// It carries the readable position beside the request, because a sheet has no
/// place to hold the position it belongs to. A catalogue entry can be missing,
/// and then the code and the name arrive as null.
/// </summary>
public sealed record BudgetReservationExportRow(
    DateOnly BudgetMonth,
    string? CostCenterCode,
    string? CostCenterName,
    string? CashFlowItemCode,
    string? CashFlowItemName,
    string Status,
    string? Purpose,
    DateOnly PlannedPaymentDate,
    int LineCount,
    decimal ReservedAmountKzt)
{
    public BudgetKey Key => BudgetKey.Create(CostCenterId, CashFlowItemId, BudgetMonth);

    /// The key travels for reconciliation only and is never published.
    internal Guid CostCenterId { get; init; }

    internal Guid CashFlowItemId { get; init; }
}

/// <summary>
/// Reads the composition of one budget position's reserve.
///
/// This is deliberately its own narrow contract rather than another method on
/// the budget repository: it answers one question about one key, and widening
/// the repository into a general-purpose reader is how a repository stops being
/// reviewable.
/// </summary>
public interface IBudgetReservationReader
{
    async Task<BudgetReservationExportCapture> ReadCaptureForExportAsync(Guid? costCenterId, Guid? cashFlowItemId,
        DateOnly? budgetMonth, BusinessDataReadScope scope, CancellationToken cancellationToken)
    {
        if (!scope.CanReadAll)
            throw new NotSupportedException("A scoped export requires complete own-key monetary totals.");
        var rows = await ReadForExportAsync(costCenterId, cashFlowItemId, budgetMonth, scope, cancellationToken);
        return BudgetReservationExportCapture.FromCompleteRows(rows, cancellationToken);
    }

    Task<BudgetReservationSlice?> ReadAsync(Guid costCenterId, Guid cashFlowItemId, DateOnly budgetMonth,
        int skip, int take, BusinessDataReadScope scope, CancellationToken cancellationToken) =>
        scope.CanReadAll ? ReadAsync(costCenterId, cashFlowItemId, budgetMonth, skip, take, cancellationToken)
            : throw new NotSupportedException("The reader must enforce CFO scope before capture.");

    Task<IReadOnlyList<BudgetReservationExportRow>> ReadForExportAsync(Guid? costCenterId, Guid? cashFlowItemId,
        DateOnly? budgetMonth, BusinessDataReadScope scope, CancellationToken cancellationToken) =>
        scope.CanReadAll ? ReadForExportAsync(costCenterId, cashFlowItemId, budgetMonth, cancellationToken)
            : throw new NotSupportedException("The reader must enforce CFO scope before export.");

    /// <summary>
    /// Returns null when no budget position exists for the exact key.
    /// </summary>
    Task<BudgetReservationSlice?> ReadAsync(
        Guid costCenterId,
        Guid cashFlowItemId,
        DateOnly budgetMonth,
        int skip,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every reserving request of every position in one filtered area, in the
    /// order the export lists the positions themselves. There is no window: an
    /// export that returned part of the area would describe less than it
    /// claims.
    /// </summary>
    Task<IReadOnlyList<BudgetReservationExportRow>> ReadForExportAsync(
        Guid? costCenterId,
        Guid? cashFlowItemId,
        DateOnly? budgetMonth,
        CancellationToken cancellationToken);
}
