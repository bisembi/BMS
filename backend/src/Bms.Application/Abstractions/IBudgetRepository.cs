using Bms.Domain.Budgeting;
using Bms.Application.Authorization;

namespace Bms.Application.Abstractions;

/// <summary>
/// One budget position as the execution list reads it. This is a read
/// projection, not the entity: nothing here invents a concurrency stamp or any
/// other state the reader did not actually load.
/// </summary>
public sealed record BudgetPositionSnapshot(
    Guid CostCenterId,
    Guid CashFlowItemId,
    DateOnly BudgetMonth,
    decimal? LimitAmountKzt,
    decimal ReservedAmountKzt,
    decimal PaidAmountKzt)
{
    public IReadOnlyList<Bms.Application.Budgeting.ExecutionPlanCostCenterDto>? PlanCostCenters { get; init; }
    public decimal? AvailableAmountKzt => BudgetPosition.CalculateAvailableAmountKzt(
        LimitAmountKzt,
        ReservedAmountKzt,
        PaidAmountKzt);
}

/// <summary>
/// One read of the budget execution area: the requested page, how many
/// positions the filter matches, and the sums over all of them. All three come
/// from the same captured slice, so a page can never disagree with the count or
/// the totals beside it.
///
/// <see cref="ConfiguredLimitAmountKzt"/> is the sum over the positions that
/// have a limit and nothing else. How many have none is reported separately; it
/// is the caller that decides what may be shown as a total for the whole area.
/// </summary>
public sealed record BudgetPositionSlice(
    IReadOnlyList<BudgetPositionSnapshot> Page,
    int Total,
    int ConfiguredLimitCount,
    int UnconfiguredLimitCount,
    decimal ConfiguredLimitAmountKzt,
    decimal ReservedAmountKzt,
    decimal PaidAmountKzt)
{
    public Bms.Application.Budgeting.ExecutionFactSourceDto? FactSource { get; init; }
}

/// <summary>
/// One row of the budget execution export. The export is read by a person in a
/// spreadsheet, so it carries the readable cost centre and cash flow item
/// rather than the identifiers the API returns. A catalogue entry can be
/// missing - nothing forces a position to have one - and then the code and the
/// name arrive as null; the exporter says so in words instead of falling back
/// to an identifier.
/// </summary>
public sealed record BudgetExecutionExportRow(
    DateOnly BudgetMonth,
    string? CostCenterCode,
    string? CostCenterName,
    string? CashFlowItemCode,
    string? CashFlowItemName,
    decimal? LimitAmountKzt,
    decimal ReservedAmountKzt,
    decimal PaidAmountKzt)
{
    // Presentation only; never an admission/reservation key.
    public bool IsCostCenterUnassigned { get; init; }
    public IReadOnlyList<Bms.Application.Budgeting.ExecutionPlanCostCenterDto>? PlanCostCenters { get; init; }
    public decimal? AvailableAmountKzt => BudgetPosition.CalculateAvailableAmountKzt(
        LimitAmountKzt,
        ReservedAmountKzt,
        PaidAmountKzt);

    /// The key travels for reconciliation between the two sheets and is never
    /// published in either of them.
    internal Guid CostCenterId { get; init; }

    internal Guid CashFlowItemId { get; init; }

    internal BudgetKey Key => BudgetKey.Create(CostCenterId, CashFlowItemId, BudgetMonth);
}

/// <summary>
/// The whole filtered execution area and its sums, captured together. There is
/// no page here: the export describes everything the filter matches.
/// <see cref="Total"/> is counted over the same capture the rows come from, so
/// a reader that returned fewer rows than it counted can be caught instead of
/// producing a spreadsheet that silently omits part of the area.
/// </summary>
public sealed record BudgetExecutionExportSlice(
    IReadOnlyList<BudgetExecutionExportRow> Rows,
    int Total,
    int ConfiguredLimitCount,
    int UnconfiguredLimitCount,
    decimal ConfiguredLimitAmountKzt,
    decimal ReservedAmountKzt,
    decimal PaidAmountKzt)
{
    public bool IncludesSourceCashConsumption { get; init; }
    // Reconcile against immutable original control keys, never against display keys.
    internal IReadOnlyList<BudgetExecutionExportRow>? ReconciliationRows { get; init; }
    public Bms.Application.Budgeting.ExecutionFactSourceDto? FactSource { get; init; }
}

public interface IBudgetRepository
{
    Task<IReadOnlyDictionary<BudgetKey,Bms.Application.Budgeting.RequestBudgetControl343Dto>?> ReadRequestControl343Async(
        Guid requestId,CancellationToken token) => Task.FromResult<IReadOnlyDictionary<BudgetKey,Bms.Application.Budgeting.RequestBudgetControl343Dto>?>(null);
    /// <summary>Null only for a legacy adapter. An active register returns its
    /// complete authorized-request result set independently of list paging.</summary>
    Task<IReadOnlyDictionary<Guid,BudgetExecutionDecision343>?> ReadFifoDecisions343Async(
        IReadOnlyCollection<Guid> requestIds,CancellationToken token) =>
        Task.FromResult<IReadOnlyDictionary<Guid,BudgetExecutionDecision343>?>(null);
    /// Consumption only: no company-version selection and no published limit.
    /// Adjustments compare these amounts with their own approved parent cells.
    /// Unsupported adapters fail closed rather than falling back to versioned limits.
    Task<IReadOnlyList<BudgetPosition>> ListConsumptionPositionsAsync(
        IReadOnlyCollection<BudgetKey> keys, CancellationToken token) =>
        throw new NotSupportedException("Independent budget consumption is unavailable.");

    Task<BudgetPositionSlice> ReadExecutionSliceAsync(Guid? costCenterId, Guid? cashFlowItemId,
        DateOnly? budgetMonth, int skip, int take, BusinessDataReadScope scope, CancellationToken cancellationToken) =>
        scope.CanReadAll ? ReadExecutionSliceAsync(costCenterId, cashFlowItemId, budgetMonth, skip, take, cancellationToken)
            : throw new NotSupportedException("The reader must enforce CFO scope before paging.");

    Task<BudgetExecutionExportSlice> ReadExecutionExportAsync(Guid? costCenterId, Guid? cashFlowItemId,
        DateOnly? budgetMonth, BusinessDataReadScope scope, CancellationToken cancellationToken) =>
        scope.CanReadAll ? ReadExecutionExportAsync(costCenterId, cashFlowItemId, budgetMonth, cancellationToken)
            : throw new NotSupportedException("The reader must enforce CFO scope before export.");

    Task<IReadOnlyList<BudgetPosition>> ListPositionsAsync(
        IReadOnlyCollection<BudgetKey> keys,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BudgetPosition>> ListPositionsAsync(
        Guid? costCenterId,
        Guid? cashFlowItemId,
        DateOnly? budgetMonth,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<int> CountPositionsAsync(
        Guid? costCenterId,
        Guid? cashFlowItemId,
        DateOnly? budgetMonth,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the page, the count and the sums of one filtered area in a single
    /// captured slice.
    /// </summary>
    Task<BudgetPositionSlice> ReadExecutionSliceAsync(
        Guid? costCenterId,
        Guid? cashFlowItemId,
        DateOnly? budgetMonth,
        int skip,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the whole filtered area and its sums in a single captured slice,
    /// in the order the execution list pages it.
    /// </summary>
    Task<BudgetExecutionExportSlice> ReadExecutionExportAsync(
        Guid? costCenterId,
        Guid? cashFlowItemId,
        DateOnly? budgetMonth,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BudgetRequestProjection>> ListRequestProjectionsAsync(
        IReadOnlyCollection<Guid> expenseRequestIds,
        CancellationToken cancellationToken);

    /// <summary>Latest immutable submission result for cancelled, paid and closed requests.
    /// For paid/closed, captured no later than the immutable full-payment timestamp.
    /// Legacy method name retained for compatibility.
    /// Legacy/in-memory stores without submission history leave the result unknown.</summary>
    Task<IReadOnlyDictionary<Guid, bool>> ReadCancelledSubmissionBudgetIndicatorsAsync(
        IReadOnlyCollection<Guid> expenseRequestIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, bool>>(new Dictionary<Guid, bool>());

    /// <summary>
    /// Replaces one request's reservation and returns its previous projections
    /// captured under the same settled request/budget locks. A caller that needs
    /// a before-audit must use this snapshot, not a preceding Serializable list
    /// read whose retained range locks can invert the budget-lock order.
    /// The caller owns the transaction; a failure publishes no replacement.
    /// </summary>
    Task<IReadOnlyList<BudgetRequestProjection>> ReplaceForRequestAsync(
        Guid expenseRequestId,
        IReadOnlyCollection<BudgetRequestProjection> desired,
        CancellationToken cancellationToken);

    /// <summary>Rebind split lines only; every budget-key total must stay exact.</summary>
    Task<IReadOnlyList<BudgetRequestProjection>> RedistributeForApprovalAsync(Guid expenseRequestId,
        IReadOnlyCollection<BudgetRequestProjection> desired,CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Approval allocation persistence is unavailable.");
}
