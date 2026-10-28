namespace Bms.Infrastructure.Persistence;

/// <summary>
/// One monotonic expense-request sequence per active CFO. The row is created
/// transactionally with the first request and never inferred from historical
/// documents.
/// </summary>
public sealed class ExpenseRequestCostCenterCounter
{
    public Guid CostCenterId { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public long LastIssuedOrdinal { get; set; }
}
