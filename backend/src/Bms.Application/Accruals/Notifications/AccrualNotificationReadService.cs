using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Common;

namespace Bms.Application.Accruals.Notifications;

public sealed class AccrualNotificationReadService(
    IAccrualNotificationReader reader,
    ICurrentActor currentActor,
    AccrualNotificationSettings settings,
    AccrualNotificationRunState runState)
{
    public async Task<AccrualNotificationPageDto> ListAsync(
        int skip, int take, CancellationToken cancellationToken)
    {
        var actor = ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (skip < 0 || take is < 1 or > 100 || skip > int.MaxValue - take)
        {
            throw new InvalidAccrualNotificationQueryException();
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await reader.ReadAsync(actor, skip, take, cancellationToken);
            if (page is null || page.Skip != skip || page.Take != take || page.Total < 0 ||
                page.Items is null || page.Items.Count > take || page.Items.Count > page.Total ||
                (page.Items.Count > 0 && skip + page.Items.Count > page.Total) ||
                page.Items.Count != Math.Min(take, Math.Max(0, page.Total - skip)))
            {
                throw new AccrualNotificationReadUnavailableException();
            }
            var ids = new HashSet<Guid>();
            foreach (var item in page.Items)
            {
                if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) ||
                    item.ExpenseRequestId == Guid.Empty || string.IsNullOrWhiteSpace(item.OrganizationName) ||
                    item.OrganizationName.Length > 500 || item.OrganizationName.Trim() != item.OrganizationName ||
                    item.OrganizationName.Any(char.IsControl) || !IsUtc(item.CreatedAtUtc) ||
                    !IsUtc(item.FullyPaidAtUtc) || item.CreatedAtUtc < item.FullyPaidAtUtc ||
                    item.PaymentBusinessDate == default || item.AsOfDate == default ||
                    item.AsOfDate.DayNumber - item.PaymentBusinessDate.DayNumber != item.AgeDays ||
                    item.RedFromDays <= 0 || item.AgeDays < item.RedFromDays)
                {
                    throw new AccrualNotificationReadUnavailableException();
                }
                MoneyAmountPolicy.EnsurePositive(item.RemainingAmountKzt, "Notification remaining amount");
            }
            return page;
        }
        catch (OperationCanceledException) { throw; }
        catch (AccrualNotificationReadUnavailableException) { throw; }
        catch (Exception exception) when (!ProcessFatalExceptionClassifier.IsFatal(exception))
        {
            throw new AccrualNotificationReadUnavailableException(exception);
        }
    }

    public async Task<AccrualNotificationMonitorDto> GetMonitorAsync(CancellationToken cancellationToken)
    {
        ActorIdentity.Require(currentActor);
        if (!currentActor.IsBmsAdministrator && !currentActor.IsBmsIntegrationAdministrator)
        {
            throw new ActorForbiddenException();
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var counts = await reader.ReadIssueCountsAsync(cancellationToken);
            var state = runState.Read();
            if (counts is null || counts.MissingInitiators < 0 || counts.MissingAccountants < 0 ||
                state.CheckedRequests < 0 || state.CreatedNotifications < 0 || state.FailedRequests < 0 ||
                (state.LastAttemptAtUtc is { } attempted && !IsUtc(attempted)) ||
                (state.LastSuccessfulAtUtc is { } succeeded && !IsUtc(succeeded)))
            {
                throw new AccrualNotificationReadUnavailableException();
            }
            return new AccrualNotificationMonitorDto(settings.Enabled,
                settings.Enabled ? state.Status : "disabled", state.LastAttemptAtUtc,
                state.LastSuccessfulAtUtc, state.CheckedRequests, state.CreatedNotifications,
                state.FailedRequests, counts.MissingInitiators, counts.MissingAccountants);
        }
        catch (OperationCanceledException) { throw; }
        catch (AccrualNotificationReadUnavailableException) { throw; }
        catch (Exception exception) when (!ProcessFatalExceptionClassifier.IsFatal(exception))
        {
            throw new AccrualNotificationReadUnavailableException(exception);
        }
    }

    private static bool IsUtc(DateTimeOffset value) => value != default &&
        value.Offset == TimeSpan.Zero && value.Ticks % TimeSpan.TicksPerMillisecond == 0;
}
