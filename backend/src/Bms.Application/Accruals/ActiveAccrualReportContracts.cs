using System.Text.Json.Serialization;

namespace Bms.Application.Accruals;

/// <summary>
/// Optional filters for the current active managerial-accrual report. Period
/// values are accounting months and therefore must be their first calendar day.
/// </summary>
public sealed record ActiveAccrualFilter(
    Guid? OrganizationId,
    Guid? CostCenterId,
    DateOnly? AccrualPeriodFrom,
    DateOnly? AccrualPeriodThrough,
    Guid? CounterpartyId = null,
    Guid? ContractId = null,
    ActiveAccrualDetailLevel DetailLevel = ActiveAccrualDetailLevel.Line,
    ActiveAccrualGrouping Grouping = ActiveAccrualGrouping.None)
{
    public void Validate()
    {
        if (OrganizationId == Guid.Empty
            || CostCenterId == Guid.Empty
            || CounterpartyId == Guid.Empty
            || ContractId == Guid.Empty
            || !Enum.IsDefined(DetailLevel)
            || !Enum.IsDefined(Grouping)
            || (AccrualPeriodFrom is { Day: not 1 })
            || (AccrualPeriodThrough is { Day: not 1 })
            || (AccrualPeriodFrom is { } from
                && AccrualPeriodThrough is { } through
                && from > through))
        {
            throw new InvalidActiveAccrualQueryException();
        }
    }
}

public enum ActiveAccrualDetailLevel
{
    Line = 0,
    Request = 1
}

public enum ActiveAccrualGrouping
{
    None = 0,
    Counterparty = 1,
    Contract = 2
}

public sealed record ActiveAccrualQuery(
    ActiveAccrualFilter Filter,
    int Skip,
    int Take)
{
    public const int MinimumTake = 1;
    public const int MaximumTake = 100;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Filter);
        Filter.Validate();
        if (Skip < 0 || Take is < MinimumTake or > MaximumTake)
        {
            throw new InvalidActiveAccrualQueryException();
        }
    }
}

/// <summary>
/// Persistence projection for one active line. The optional group identity is
/// server-only: it distinguishes equal display names but never enters a DTO.
/// No cycle UUID, employee identity or audit text is included.
/// </summary>
public sealed record ActiveAccrualRow(
    Guid ExpenseRequestId,
    int LineSortOrder,
    DateTimeOffset FullyPaidAtUtc,
    DateOnly AccrualPeriod,
    string CurrencyCode,
    string? OrganizationName,
    string? CostCenterName,
    string? CashFlowItemName,
    string? ExpenseItemName,
    bool CounterpartyWasSpecified,
    string? CounterpartyName,
    string? Purpose,
    decimal OriginalAmount,
    decimal ConfirmedClosedAmount,
    decimal RemainingAmount,
    decimal OriginalAmountKzt,
    decimal ConfirmedClosedAmountKzt,
    decimal RemainingAmountKzt,
    DateOnly? AccrualPeriodThrough = null,
    bool ContractWasSpecified = false,
    string? ContractName = null,
    string? InitiatorName = null,
    int LineCount = 1,
    bool IsRequestSummary = false,
    [property: JsonIgnore] Guid? GroupIdentity = null,
    [property: JsonIgnore] Guid? CostCenterIdentity = null,
    [property: JsonIgnore] Guid? ExpenseItemIdentity = null,
    [property: JsonIgnore] decimal? OriginalNetAmountKzt = null,
    [property: JsonIgnore] decimal? OriginalVatAmountKzt = null);

public sealed record ActiveAccrualTotals(
    decimal OriginalAmountKzt,
    decimal ConfirmedClosedAmountKzt,
    decimal RemainingAmountKzt);

public sealed record ActiveAccrualReadResult(
    IReadOnlyList<ActiveAccrualRow> Rows,
    int Total,
    ActiveAccrualTotals Totals,
    bool HasInvariantViolation);

public interface IActiveAccrualReportReader
{
    /// <summary>
    /// Reads one page of the active accrual scope.
    ///
    /// <paramref name="maximumScopeRows"/> is the caller's own ceiling on how
    /// large a scope it is prepared to receive. When the scope is larger, the
    /// read stops as soon as it knows the count: it returns the true total with
    /// no rows, and never reads the payment ledger or the page. A caller that
    /// has no ceiling passes null and is unaffected.
    /// </summary>
    Task<ActiveAccrualReadResult> ReadAsync(
        string actorExternalId,
        bool includeAll,
        ActiveAccrualFilter filter,
        int skip,
        int take,
        int? maximumScopeRows,
        AccrualAgingContext aging,
        CancellationToken cancellationToken);
}

public sealed record ActiveAccrualRowDto(
    Guid ExpenseRequestId,
    int? LineNumber,
    DateTimeOffset FullyPaidAtUtc,
    DateOnly AccrualPeriod,
    string CurrencyCode,
    string OrganizationName,
    string CostCenterName,
    string CashFlowItemName,
    string ExpenseItemName,
    string? CounterpartyName,
    string? Purpose,
    string ClosingProgress,
    decimal OriginalAmount,
    decimal ConfirmedClosedAmount,
    decimal RemainingAmount,
    decimal OriginalAmountKzt,
    decimal ConfirmedClosedAmountKzt,
    decimal RemainingAmountKzt,
    DateOnly FullyPaidBusinessDate,
    int AgeDays,
    string AgeZone,
    DateOnly? AccrualPeriodThrough = null,
    int LineCount = 1,
    bool IsRequestSummary = false,
    string? ContractName = null,
    string? InitiatorName = null,
    // Begins a section on THIS page, not a whole-scope subtotal or group ordinal.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? StartsGroup = null);

public sealed record ActiveAccrualTotalsDto(
    decimal OriginalAmountKzt,
    decimal ConfirmedClosedAmountKzt,
    decimal RemainingAmountKzt);

public sealed record ActiveAccrualPageDto(
    IReadOnlyList<ActiveAccrualRowDto> Items,
    int Skip,
    int Take,
    int Total,
    ActiveAccrualTotalsDto Totals,
    ActiveAccrualAgingDto Aging,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Grouping = null);

public sealed record ActiveAccrualAgingDto(
    DateOnly AsOfDate,
    int YellowFromDays,
    int OrangeFromDays,
    int RedFromDays);

public sealed class InvalidActiveAccrualQueryException : Exception
{
    private const string SafeMessage =
        "The active-accrual query is outside its public contract.";

    public InvalidActiveAccrualQueryException()
        : base(SafeMessage)
    {
    }
}

public sealed class ActiveAccrualReportUnavailableException : Exception
{
    private const string SafeMessage =
        "The active-accrual report is temporarily unavailable.";

    public ActiveAccrualReportUnavailableException()
        : base(SafeMessage)
    {
    }

    public ActiveAccrualReportUnavailableException(Exception innerException)
        : base(
            SafeMessage,
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}

public sealed class ActiveAccrualExportTooLargeException(int actualRows, int maximumRows)
    : Exception($"The active-accrual export contains {actualRows} rows; the limit is {maximumRows}.")
{
    public int ActualRows { get; } = actualRows;

    public int MaximumRows { get; } = maximumRows;
}
