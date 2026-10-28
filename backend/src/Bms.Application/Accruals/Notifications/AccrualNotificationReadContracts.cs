namespace Bms.Application.Accruals.Notifications;

public sealed record AccrualNotificationDto(
    Guid Id,
    Guid ExpenseRequestId,
    string OrganizationName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset FullyPaidAtUtc,
    DateOnly PaymentBusinessDate,
    DateOnly AsOfDate,
    int AgeDays,
    int RedFromDays,
    decimal RemainingAmountKzt);

public sealed record AccrualNotificationPageDto(
    int Total, int Skip, int Take, IReadOnlyList<AccrualNotificationDto> Items);

public interface IAccrualNotificationReader
{
    Task<AccrualNotificationPageDto> ReadAsync(
        string actorExternalId, int skip, int take, CancellationToken cancellationToken);

    Task<AccrualNotificationIssueCounts> ReadIssueCountsAsync(CancellationToken cancellationToken);
}

public sealed record AccrualNotificationIssueCounts(int MissingInitiators, int MissingAccountants);

/// <summary>Operational counts only: no recipient, request, cycle, payload or identity.</summary>
public sealed record AccrualNotificationMonitorDto(
    bool ControlEnabled,
    string LastRunStatus,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? LastSuccessfulAtUtc,
    int CheckedRequests,
    int CreatedNotifications,
    int FailedRequests,
    int MissingInitiators,
    int MissingAccountants);

public sealed class InvalidAccrualNotificationQueryException : Exception;

public sealed class AccrualNotificationReadUnavailableException : Exception
{
    public AccrualNotificationReadUnavailableException()
        : base("Accrual notifications are temporarily unavailable.") { }

    public AccrualNotificationReadUnavailableException(Exception innerException)
        : base("Accrual notifications are temporarily unavailable.", innerException) { }
}
