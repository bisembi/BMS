using Bms.Application.Abstractions;
using Bms.Application.Accruals.Notifications;
using Bms.Application.Common;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// One short request transaction at a time. The request keyset is only discovery;
/// the control locks and rereads the request, current cycle and recipients.
/// A failed request is counted, never accepted as a completed daily pass.
/// </summary>
internal sealed class EfCoreAccrualNotificationSweep(
    BmsDbContext dbContext,
    IAccrualNotificationControl control,
    IBusinessTransactionRunner transactions,
    IClock clock,
    AccrualNotificationSettings settings) : IAccrualNotificationSweep
{
    private const int BatchSize = 100;

    public async Task<AccrualNotificationSweepResult> RunAsync(CancellationToken cancellationToken)
    {
        try { return await RunCoreAsync(cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (AccrualNotificationControlUnavailableException) { throw; }
        catch (Exception exception) when (!ProcessFatalExceptionClassifier.IsFatal(exception))
        {
            throw new AccrualNotificationControlUnavailableException(exception);
        }
    }

    private async Task<AccrualNotificationSweepResult> RunCoreAsync(CancellationToken cancellationToken)
    {
        var actor = settings.SystemActorExternalId;
        if (!settings.Enabled || !ActorIdentity.IsCanonical(actor) || actor is null ||
            dbContext.Database.CurrentTransaction is not null || dbContext.ChangeTracker.HasChanges())
        {
            throw new AccrualNotificationControlUnavailableException();
        }

        var checkedRequests = 0;
        var created = 0;
        var failed = 0;
        Guid? after = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = dbContext.ExpenseRequests.AsNoTracking().Where(request =>
                request.Status == ExpenseRequestStatus.Paid ||
                dbContext.AccrualNotificationIssues.Any(issue =>
                    issue.ExpenseRequestId == request.Id && issue.ResolvedAtUtc == null));
            if (after is { } last)
            {
                query = query.Where(request => request.Id.CompareTo(last) > 0);
            }
            var ids = await query.OrderBy(request => request.Id).Select(request => request.Id)
                .Take(BatchSize).ToArrayAsync(cancellationToken);
            if (ids.Length == 0) { break; }
            foreach (var id in ids)
            {
                checkedRequests++;
                try
                {
                    var result = await transactions.ExecuteAsync(async token =>
                    {
                        var outcome = await control.CheckAsync(id, actor,
                            AccrualNotificationTrigger.DailyControl, clock.UtcNow, token);
                        if (outcome.HasChanges) { await dbContext.SaveChangesAsync(token); }
                        return outcome;
                    }, cancellationToken);
                    created = checked(created + result.CreatedNotifications);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) when (!ProcessFatalExceptionClassifier.IsFatal(exception))
                {
                    failed++;
                }
                finally
                {
                    // This scope belongs solely to the sweep; no caller's draft
                    // or unsaved mutation can be present (checked on entry).
                    dbContext.ChangeTracker.Clear();
                }
            }
            after = ids[^1];
        }
        return new AccrualNotificationSweepResult(checkedRequests, created, failed);
    }
}
