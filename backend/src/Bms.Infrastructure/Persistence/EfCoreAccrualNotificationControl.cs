using System.Data;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Accruals;
using Bms.Application.Accruals.Notifications;
using Bms.Application.Common;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

internal sealed partial class EfCoreAccrualNotificationControl(
    BmsDbContext dbContext,
    IAccrualAgingPolicy agingPolicy,
    IBusinessAuditSink auditSink,
    IOutboxSink outboxSink) : IAccrualNotificationControl
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AccrualNotificationCheckResult> CheckAsync(
        Guid expenseRequestId,
        string actorExternalId,
        AccrualNotificationTrigger trigger,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureTransactionAndArguments(expenseRequestId, actorExternalId, trigger, occurredAtUtc);
            var configured = agingPolicy.GetCurrentContext()
                ?? throw new AccrualNotificationControlUnavailableException();
            var aging = new AccrualAgingContext(occurredAtUtc,
                configured.BusinessTimeZone, configured.Thresholds);
            var resource = BusinessManagedLockResources.ExpenseRequest(expenseRequestId);
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @NotificationLockResult int;
                EXEC @NotificationLockResult = sys.sp_getapplock
                    @Resource = {resource}, @LockMode = N'Exclusive',
                    @LockOwner = N'Transaction', @LockTimeout = 15000, @DbPrincipal = N'public';
                IF @NotificationLockResult < 0
                    THROW 62320, N'BMS accrual notification request lock was not acquired.', 1;
                """, cancellationToken);
            var request = await dbContext.ExpenseRequests.AsNoTracking().Include(item => item.Lines)
                .SingleAsync(item => item.Id == expenseRequestId, cancellationToken);
            var cycle = request.Status == ExpenseRequestStatus.Paid && request.AccrualCycleId is { } cycleId
                ? await new AccrualClosingCycleSnapshotStore(dbContext)
                    .FindAsync(request.Id, cycleId, cancellationToken)
                : null;
            var candidate = AccrualRedZoneCandidate.Evaluate(request, cycle, aging);
            var issues = await dbContext.AccrualNotificationIssues
                .Where(item => item.ExpenseRequestId == expenseRequestId).ToListAsync(cancellationToken);
            if (candidate is null)
            {
                foreach (var issue in issues)
                {
                    ResolveIssue(issue, actorExternalId, trigger, occurredAtUtc);
                }
                return new AccrualNotificationCheckResult(0, 0, dbContext.ChangeTracker.HasChanges());
            }

            var recipients = await ReadRecipientsAsync(candidate, cancellationToken);
            var missingCodes = new HashSet<string>(StringComparer.Ordinal);
            if (!recipients.Any(item => item.IsInitiator))
            {
                missingCodes.Add(AccrualNotificationCodes.MissingInitiator);
            }
            if (!recipients.Any(item => item.IsResponsibleAccountant))
            {
                missingCodes.Add(AccrualNotificationCodes.MissingAccountant);
            }
            foreach (var code in missingCodes)
            {
                RecordIssue(candidate, code, issues, actorExternalId, trigger, occurredAtUtc);
            }
            foreach (var issue in issues.Where(item => !missingCodes.Contains(item.Code)))
            {
                ResolveIssue(issue, actorExternalId, trigger, occurredAtUtc);
            }

            var existing = await dbContext.AccrualNotifications.AsNoTracking()
                .Where(item => item.NotificationType == AccrualNotificationCodes.RedZone &&
                    item.ExpenseRequestId == candidate.ExpenseRequestId &&
                    item.AccrualCycleId == candidate.CycleId)
                .ToArrayAsync(cancellationToken);
            if (existing.Any(item => item.OrganizationId != candidate.OrganizationId ||
                item.FullyPaidAtUtc != candidate.FullyPaidAtUtc))
            {
                throw new AccrualNotificationControlUnavailableException();
            }
            var alreadyQueued = existing.Select(item => item.RecipientEmployeeId).ToHashSet();
            var created = 0;
            foreach (var recipient in recipients.OrderBy(item => item.EmployeeId))
            {
                if (alreadyQueued.Contains(recipient.EmployeeId))
                {
                    continue;
                }
                var notification = new AccrualNotification
                {
                    Id = Guid.NewGuid(),
                    NotificationType = AccrualNotificationCodes.RedZone,
                    ExpenseRequestId = candidate.ExpenseRequestId,
                    AccrualCycleId = candidate.CycleId,
                    RecipientEmployeeId = recipient.EmployeeId,
                    OrganizationId = candidate.OrganizationId,
                    RecipientWasInitiator = recipient.IsInitiator,
                    RecipientWasResponsibleAccountant = recipient.IsResponsibleAccountant,
                    CreatedAtUtc = occurredAtUtc,
                    FullyPaidAtUtc = candidate.FullyPaidAtUtc,
                    PaymentBusinessDate = candidate.PaymentBusinessDate,
                    AsOfDate = candidate.AsOfDate,
                    AgeDays = candidate.AgeDays,
                    RedFromDays = candidate.RedFromDays,
                    RemainingAmountKzt = candidate.RemainingAmountKzt
                };
                dbContext.AccrualNotifications.Add(notification);
                RecordEvidence(candidate.ExpenseRequestId, actorExternalId, occurredAtUtc,
                    "AccrualRedZoneQueued", "accrual-notification.queued.v1",
                    new
                    {
                        trigger = trigger.ToString(),
                        result = "queued",
                        zone = "red",
                        notification.Id,
                        notification.NotificationType,
                        notification.ExpenseRequestId,
                        notification.AccrualCycleId,
                        notification.RecipientEmployeeId,
                        notification.RecipientWasInitiator,
                        notification.RecipientWasResponsibleAccountant,
                        notification.AsOfDate,
                        notification.AgeDays,
                        notification.RedFromDays,
                        notification.FullyPaidAtUtc,
                        notification.RemainingAmountKzt
                    });
                created++;
            }
            return new AccrualNotificationCheckResult(created, missingCodes.Count,
                dbContext.ChangeTracker.HasChanges());
        }
        catch (OperationCanceledException) { throw; }
        catch (AccrualNotificationControlUnavailableException) { throw; }
        catch (Exception exception) when (!ProcessFatalExceptionClassifier.IsFatal(exception))
        {
            throw new AccrualNotificationControlUnavailableException(exception);
        }
    }

    private void EnsureTransactionAndArguments(Guid requestId, string actor,
        AccrualNotificationTrigger trigger, DateTimeOffset atUtc)
    {
        if (requestId == Guid.Empty || !ActorIdentity.IsCanonical(actor) ||
            !Enum.IsDefined(trigger) || atUtc == default || atUtc.Offset != TimeSpan.Zero ||
            atUtc.Ticks % TimeSpan.TicksPerMillisecond != 0 ||
            dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel !=
                IsolationLevel.Serializable || dbContext.ChangeTracker.HasChanges())
        {
            throw new AccrualNotificationControlUnavailableException();
        }
    }

    private void RecordEvidence(Guid requestId, string actor, DateTimeOffset atUtc,
        string action, string messageType, object value)
    {
        var payload = JsonSerializer.Serialize(value, JsonOptions);
        var correlationId = Guid.NewGuid().ToString("N");
        // Technical audit remains separate from ordinary request-history events.
        auditSink.Append(Guid.NewGuid(), atUtc, actor, "AccrualNotification",
            requestId.ToString("D"), action, null, payload, correlationId);
        outboxSink.Enqueue(Guid.NewGuid(), atUtc, messageType, payload, correlationId);
    }
}
