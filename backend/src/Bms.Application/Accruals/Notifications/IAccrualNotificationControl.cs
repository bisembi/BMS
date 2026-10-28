namespace Bms.Application.Accruals.Notifications;

public enum AccrualNotificationTrigger
{
    DailyControl,
    FullPayment,
    PaymentChanged,
    Reopened,
    Closing
}

/// <summary>
/// Called inside the financial command's existing Serializable transaction,
/// after its request/cycle rows have been saved but before the transaction commits.
/// It never starts an independent transaction or sends an external message.
/// </summary>
public interface IAccrualNotificationControl
{
    Task<AccrualNotificationCheckResult> CheckAsync(
        Guid expenseRequestId,
        string actorExternalId,
        AccrualNotificationTrigger trigger,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken);
}

public sealed record AccrualNotificationCheckResult(
    int CreatedNotifications,
    int OpenConfigurationIssues,
    bool HasChanges);

/// <summary>
/// Explicit deployment opt-out until SQL027 and the business calendar are ready.
/// Production registration selects this only when the feature is disabled.
/// </summary>
public sealed class DisabledAccrualNotificationControl : IAccrualNotificationControl
{
    public static DisabledAccrualNotificationControl Instance { get; } = new();

    private DisabledAccrualNotificationControl() { }

    public Task<AccrualNotificationCheckResult> CheckAsync(
        Guid expenseRequestId,
        string actorExternalId,
        AccrualNotificationTrigger trigger,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AccrualNotificationCheckResult(0, 0, false));
    }
}

public sealed class AccrualNotificationControlUnavailableException : Exception
{
    public AccrualNotificationControlUnavailableException()
        : base("The accrual notification control is unavailable.") { }

    public AccrualNotificationControlUnavailableException(Exception innerException)
        : base("The accrual notification control is unavailable.", innerException) { }
}
