namespace Bms.Infrastructure.Persistence;

/// <summary>One immutable source-document revision and its exact BMS
/// attribution. Only ReversedAtUtc can turn once; the original fact survives.</summary>
public sealed class AccountingPaymentLink
{
    public Guid Id { get; set; }
    public string DocumentKey { get; set; } = string.Empty;
    public string FinancialHash { get; set; } = string.Empty;
    public int Revision { get; set; }
    public Guid SourceBatchId { get; set; }
    public Guid ExpenseRequestId { get; set; }
    public Guid EventId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal AmountKzt { get; set; }
    public DateOnly PaymentDay { get; set; }
    public string SourceLegsJson { get; set; } = string.Empty;
    public DateTimeOffset MatchedAtUtc { get; set; }
    public DateTimeOffset? ReversedAtUtc { get; set; }
    public Guid? ReversedByBatchId { get; set; }
}

public sealed class AccountingPaymentLinkLine
{
    public Guid LinkId { get; set; }
    public Guid ExpenseRequestLineId { get; set; }
    public Guid CostCenterId { get; set; }
    public Guid CashFlowItemId { get; set; }
    public DateOnly BudgetMonth { get; set; }
    public decimal OriginalReservePaidKzt { get; set; }
}
