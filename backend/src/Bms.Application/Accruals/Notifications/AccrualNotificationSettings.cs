using Bms.Application.Common;

namespace Bms.Application.Accruals.Notifications;

public sealed class AccrualNotificationSettings
{
    public AccrualNotificationSettings(bool enabled, string? systemActorExternalId)
    {
        if (enabled && !ActorIdentity.IsCanonical(systemActorExternalId))
        {
            throw new ArgumentException("Enabled accrual control requires an explicit canonical system actor.",
                nameof(systemActorExternalId));
        }
        Enabled = enabled;
        SystemActorExternalId = systemActorExternalId;
    }

    public bool Enabled { get; }
    public string? SystemActorExternalId { get; }
}

public interface IAccrualNotificationSweep
{
    Task<AccrualNotificationSweepResult> RunAsync(CancellationToken cancellationToken);
}

public sealed record AccrualNotificationSweepResult(
    int CheckedRequests,
    int CreatedNotifications,
    int FailedRequests);

public sealed record AccrualNotificationRunSnapshot(
    string Status,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? LastSuccessfulAtUtc,
    DateOnly? LastSuccessfulBusinessDate,
    int CheckedRequests,
    int CreatedNotifications,
    int FailedRequests);

/// <summary>Process-local scheduling evidence; durable deduplication lives in SQL027.</summary>
public sealed class AccrualNotificationRunState
{
    private AccrualNotificationRunSnapshot snapshot = new("neverRun", null, null, null, 0, 0, 0);
    private readonly object sync = new();

    public AccrualNotificationRunSnapshot Read()
    {
        lock (sync) { return snapshot; }
    }

    public void Started(DateTimeOffset atUtc)
    {
        EnsureUtc(atUtc);
        lock (sync)
        {
            snapshot = snapshot with
            {
                Status = "running",
                LastAttemptAtUtc = atUtc,
                CheckedRequests = 0,
                CreatedNotifications = 0,
                FailedRequests = 0
            };
        }
    }

    public void Finished(DateTimeOffset atUtc, DateOnly businessDate, AccrualNotificationSweepResult result)
    {
        EnsureUtc(atUtc);
        if (businessDate == default || result is null || result.CheckedRequests < 0 ||
            result.CreatedNotifications < 0 || result.FailedRequests < 0 ||
            result.FailedRequests > result.CheckedRequests ||
            (result.CheckedRequests == 0 && result.CreatedNotifications != 0))
        {
            throw new AccrualNotificationControlUnavailableException();
        }
        lock (sync)
        {
            var succeeded = result.FailedRequests == 0;
            snapshot = snapshot with
            {
                Status = succeeded ? "succeeded" : "failed",
                LastSuccessfulAtUtc = succeeded ? atUtc : snapshot.LastSuccessfulAtUtc,
                LastSuccessfulBusinessDate = succeeded ? businessDate : snapshot.LastSuccessfulBusinessDate,
                CheckedRequests = result.CheckedRequests,
                CreatedNotifications = result.CreatedNotifications,
                FailedRequests = result.FailedRequests
            };
        }
    }

    public void Failed(DateTimeOffset atUtc)
    {
        EnsureUtc(atUtc);
        lock (sync)
        {
            snapshot = snapshot with
            {
                Status = "failed",
                LastAttemptAtUtc = atUtc,
                CheckedRequests = 0,
                CreatedNotifications = 0,
                FailedRequests = 1
            };
        }
    }

    private static void EnsureUtc(DateTimeOffset atUtc)
    {
        if (atUtc == default || atUtc.Offset != TimeSpan.Zero ||
            atUtc.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            throw new AccrualNotificationControlUnavailableException();
        }
    }
}
