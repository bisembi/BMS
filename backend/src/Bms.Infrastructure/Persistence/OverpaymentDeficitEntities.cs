namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Dormant persistence shape for the remaining applied part of a reversed
/// overpayment. No repository, command handler or endpoint writes it yet.
/// </summary>
public sealed class OverpaymentDeficit
{
    public Guid OrganizationId { get; set; }
    public Guid CounterpartyId { get; set; }
    public Guid CurrencyId { get; set; }
    public Guid DeficitId { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal OriginalAmountKzt { get; set; }
    public decimal CurrentAmount { get; set; }
    public decimal CurrentAmountKzt { get; set; }
    public Guid SourceOverpaymentId { get; set; }
    public string ExternalReversalId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? SourceExpenseRequestId { get; set; }
    public Guid CreationCommandId { get; set; }
    public Guid? ActualCorrectionExecutorId { get; set; }
    public DateTimeOffset? SettledAt { get; set; }
}

/// <summary>
/// Structured application of one real or approved manual source to one
/// deficit. Business history remains owned by BusinessAuditEntries.
/// </summary>
public sealed class OverpaymentDeficitSettlement
{
    public Guid DeficitId { get; set; }
    public Guid CommandId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? SourceReference { get; set; }
    public Guid? SourceExpenseRequestId { get; set; }
    public Guid? CorrectionId { get; set; }
    public decimal Amount { get; set; }
    public decimal AmountKzt { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
    public Guid UserId { get; set; }
}

/// <summary>
/// Non-posting manual-correction document. Only a future audited server
/// command may move it out of PendingApproval and create a settlement.
/// </summary>
public sealed class ManualOverpaymentDeficitCorrection
{
    public Guid CorrectionId { get; set; }
    public Guid DeficitId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CounterpartyId { get; set; }
    public Guid CurrencyId { get; set; }
    public decimal DeficitAmountAtCreation { get; set; }
    public decimal DeficitAmountKztAtCreation { get; set; }
    public decimal CorrectionAmount { get; set; }
    public decimal CorrectionAmountKzt { get; set; }
    public string Basis { get; set; } = string.Empty;
    public Guid ResponsibleAccountantId { get; set; }
    public Guid? FinancialDirectorId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionComment { get; set; }
    public string? RejectionReason { get; set; }
    public Guid CreationCommandId { get; set; }
    public Guid? DecisionCommandId { get; set; }
}
