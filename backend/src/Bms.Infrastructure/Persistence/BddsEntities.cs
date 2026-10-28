namespace Bms.Infrastructure.Persistence;

public sealed class BddsSnapshot
{
    public Guid Id { get; set; }
    public DateOnly SourceVersionDate { get; set; }
    public string Scenario { get; set; } = string.Empty;
    public int BudgetYear { get; set; }
    public string SourceSystem { get; set; } = string.Empty;
    public string SourceTable { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public int SourceRowCount { get; set; }
    public int CostCenterCount { get; set; }
    public int ArticleCount { get; set; }
    public int NonZeroCellCount { get; set; }
    public decimal ReceiptsAmountKzt { get; set; }
    public decimal PaymentsAmountKzt { get; set; }
    public decimal NetCashFlowAmountKzt { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ApprovedAtUtc { get; set; }
    public Guid ApprovedByEmployeeId { get; set; }
    public string ApprovedByExternalId { get; set; } = string.Empty;
    public bool IsSealed { get; set; }
}

public sealed class BddsSnapshotRow
{
    public long Id { get; set; }
    public Guid SnapshotId { get; set; }
    public int SourceOrdinal { get; set; }
    public string SourceCostCenterName { get; set; } = string.Empty;
    public Guid MappedCostCenterId { get; set; }
    public string ActivityName { get; set; } = string.Empty;
    public string SectionName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string ArticleBusinessCode { get; set; } = string.Empty;
    public string ArticleName { get; set; } = string.Empty;
    public Guid CashFlowItemId { get; set; }
    public DateOnly BudgetMonth { get; set; }
    public decimal SignedAmountKzt { get; set; }
    public string SourceFile { get; set; } = string.Empty;
    public string SourceSheet { get; set; } = string.Empty;
    public int SourceRowNumber { get; set; }
    public DateTimeOffset SourceLoadedAtUtc { get; set; }
    public string RowHash { get; set; } = string.Empty;
}

public sealed class BddsSnapshotBudgetInstance
{
    public Guid SnapshotId { get; set; }
    public Guid BudgetInstanceId { get; set; }
    public Guid CostCenterId { get; set; }
    public string SourceCostCenterName { get; set; } = string.Empty;
}
