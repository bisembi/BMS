namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Dormant immutable shape of one confirmed external payment event. Runtime
/// intake remains disabled until the payment-event Gate G1 is approved.
/// </summary>
public sealed class ExpenseRequestPaymentFact
{
    public string SourceSystem { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public Guid EventId { get; set; }
    public Guid ExpenseRequestId { get; set; }
    public Guid CurrencyId { get; set; }
    public decimal Amount { get; set; }
    public decimal AmountKzt { get; set; }
    public DateTimeOffset PaidAt { get; set; }
    public string? PaymentNumber { get; set; }
    public bool IsReversal { get; set; }
    public Guid? ReversedEventId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}

/// <summary>
/// Current paired projection of one positive overpayment source. No public
/// reader or writer consumes it yet.
/// </summary>
public sealed class OverpaymentSource
{
    public Guid OrganizationId { get; set; }
    public Guid CounterpartyId { get; set; }
    public Guid CurrencyId { get; set; }
    public Guid SourceId { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal OriginalAmountKzt { get; set; }
    public decimal ReversedAmount { get; set; }
    public decimal ReversedAmountKzt { get; set; }
    public decimal DeficitTransferredAmount { get; set; }
    public decimal DeficitTransferredAmountKzt { get; set; }
    public decimal CurrentRemainingAmount { get; set; }
    public decimal CurrentRemainingAmountKzt { get; set; }
    public decimal ReservedAmount { get; set; }
    public decimal ReservedAmountKzt { get; set; }
    public Guid SourceExpenseRequestId { get; set; }
    public Guid SourceEventId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>
/// Current reservation/application/release state linking one source slice to
/// one target expense request. Mutation history remains in BusinessAuditEntry.
/// </summary>
public sealed class OverpaymentAllocation
{
    public Guid TargetExpenseRequestId { get; set; }
    public Guid SourceId { get; set; }
    public Guid AllocationId { get; set; }
    public decimal Amount { get; set; }
    public decimal AmountKzt { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid CommandId { get; set; }
    public DateTimeOffset ReservedAt { get; set; }
    public DateTimeOffset? AppliedOrReleasedAt { get; set; }
    public Guid UserId { get; set; }
}
