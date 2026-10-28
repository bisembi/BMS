namespace Bms.Infrastructure.Persistence;

public sealed class CashFlowActualsSnapshot
{
    public Guid Id { get; set; }
    public int SourceYear { get; set; } = 2026;
    public string SourceHash { get; set; } = string.Empty;
    public string SourceGeneration { get; set; } = string.Empty;
    public DateTimeOffset CapturedAtUtc { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }
    public Guid ImportedByEmployeeId { get; set; }
    public string ImportedByExternalId { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public int ArticleCount { get; set; }
    public decimal ReceiptsAmountKzt { get; set; }
    public decimal PaymentsAmountKzt { get; set; }
    public decimal NetCashFlowAmountKzt { get; set; }
    public bool IsSealed { get; set; }
}

public sealed class CashFlowActualsMonth
{
    public Guid SnapshotId { get; set; }
    public DateOnly BudgetMonth { get; set; }
}

public sealed class CashFlowActualsArticle
{
    public Guid SnapshotId { get; set; }
    public int ArticleOrdinal { get; set; }
    public string ArticleName { get; set; } = string.Empty;
    public Guid? CashFlowItemId { get; set; }
}

public sealed class CashFlowActualsRow
{
    public Guid SnapshotId { get; set; }
    public int SourceOrdinal { get; set; }
    public int ArticleOrdinal { get; set; }
    public DateOnly BudgetMonth { get; set; }
    public decimal SignedAmountKzt { get; set; }
}
