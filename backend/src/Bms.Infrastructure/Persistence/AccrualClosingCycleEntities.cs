namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Immutable header of one expense-request closing cycle. It records source
/// facts only and does not authorize or represent a close command.
/// </summary>
public sealed class PersistedAccrualClosingCycleSnapshot
{
    public Guid ExpenseRequestId { get; set; }
    public Guid CycleId { get; set; }
    public int CycleNumber { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CostCenterId { get; set; }
    public Guid RequestVersion { get; set; }
    public DateTimeOffset SourceUpdatedAtUtc { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal AmountExcludingVat { get; set; }
    public decimal GrossAmountKzt { get; set; }
    public decimal VatAmountKzt { get; set; }
    public decimal AmountExcludingVatKzt { get; set; }
}

/// <summary>One immutable source line belonging to a captured closing cycle.</summary>
public sealed class PersistedAccrualClosingCycleLine
{
    public Guid ExpenseRequestId { get; set; }
    public Guid CycleId { get; set; }
    public Guid LineId { get; set; }
    public int SortOrder { get; set; }
    public Guid CashFlowItemId { get; set; }
    public Guid CostCenterId { get; set; }
    public Guid ExpenseItemId { get; set; }
    public DateOnly AccrualPeriod { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal AmountExcludingVat { get; set; }
    public decimal GrossAmountKzt { get; set; }
    public decimal VatAmountKzt { get; set; }
    public decimal AmountExcludingVatKzt { get; set; }
}
