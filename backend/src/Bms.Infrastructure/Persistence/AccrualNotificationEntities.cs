namespace Bms.Infrastructure.Persistence;

/// <summary>Immutable notification event; personal access is revalidated when read.</summary>
public sealed class AccrualNotification
{
    public Guid Id { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public Guid ExpenseRequestId { get; set; }
    public Guid AccrualCycleId { get; set; }
    public Guid RecipientEmployeeId { get; set; }
    public Guid OrganizationId { get; set; }
    public bool RecipientWasInitiator { get; set; }
    public bool RecipientWasResponsibleAccountant { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset FullyPaidAtUtc { get; set; }
    public DateOnly PaymentBusinessDate { get; set; }
    public DateOnly AsOfDate { get; set; }
    public int AgeDays { get; set; }
    public int RedFromDays { get; set; }
    public decimal RemainingAmountKzt { get; set; }
}

/// <summary>
/// Separate operational issue, never a delivery with an empty recipient. Audit
/// preserves issue transitions; this row records the current configuration state.
/// </summary>
public sealed class AccrualNotificationIssue
{
    public Guid ExpenseRequestId { get; set; }
    public string Code { get; set; } = string.Empty;
    public Guid? AccrualCycleId { get; set; }
    public DateTimeOffset FirstDetectedAtUtc { get; set; }
    public DateTimeOffset LastDetectedAtUtc { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
}
