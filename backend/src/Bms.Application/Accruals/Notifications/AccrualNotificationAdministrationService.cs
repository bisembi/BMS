using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.Workflow;

namespace Bms.Application.Accruals.Notifications;

public sealed class AccrualNotificationAdministrationService(
    IAccrualNotificationAdministrationReader reader, ICurrentActor currentActor)
{
    public static void RequireFinancialReader(ICurrentActor actor)
    {
        ActorIdentity.RequireBusinessDataAccess(actor);
        if (actor.IsBmsAdministrator || actor.IsBmsIntegrationAdministrator ||
            actor.ActiveRoleCode is not (ExpenseRequestWorkflowCodes.FinancialControllerRoleCode or
                ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode or ExpenseRequestWorkflowCodes.AuditorRoleCode))
        {
            throw new ActorForbiddenException();
        }
    }

    public async Task<AdministrativeAccrualNotificationPageDto> ListQueueAsync(
        int skip, int take, CancellationToken cancellationToken)
    {
        RequireFinancialReader(currentActor);
        RequireQuery(skip, take);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await reader.ReadQueueAsync(skip, take, cancellationToken);
            if (page is null || page.Skip != skip || page.Take != take || page.Items is null ||
                !IsPage(page.Total, skip, take, page.Items.Count))
            {
                throw new AccrualNotificationReadUnavailableException();
            }
            var ids = new HashSet<Guid>();
            DateTimeOffset? previousCreated = null;
            foreach (var row in page.Items)
            {
                if (row is null || row.Id == Guid.Empty || !ids.Add(row.Id) ||
                    row.ExpenseRequestId == Guid.Empty || !IsText(row.OrganizationName, 500) ||
                    !IsText(row.RecipientCode, 20) || !IsText(row.RecipientName, 150) ||
                    (!row.RecipientWasInitiator && !row.RecipientWasResponsibleAccountant) ||
                    !IsUtc(row.CreatedAtUtc) || !IsUtc(row.FullyPaidAtUtc) ||
                    row.CreatedAtUtc < row.FullyPaidAtUtc || previousCreated < row.CreatedAtUtc ||
                    row.PaymentBusinessDate == default || row.AsOfDate == default ||
                    row.AsOfDate.DayNumber - row.PaymentBusinessDate.DayNumber != row.AgeDays ||
                    row.RedFromDays <= 0 || row.AgeDays < row.RedFromDays)
                {
                    throw new AccrualNotificationReadUnavailableException();
                }
                MoneyAmountPolicy.EnsurePositive(row.RemainingAmountKzt, "Notification remaining amount");
                previousCreated = row.CreatedAtUtc;
            }
            return page;
        }
        catch (OperationCanceledException) { throw; }
        catch (ActorForbiddenException) { throw; }
        catch (AccrualNotificationReadUnavailableException) { throw; }
        catch (Exception exception) when (!ProcessFatalExceptionClassifier.IsFatal(exception))
        {
            throw new AccrualNotificationReadUnavailableException(exception);
        }
    }

    public async Task<AccrualNotificationIssuePageDto> ListIssuesAsync(
        int skip, int take, CancellationToken cancellationToken)
    {
        RequireFinancialReader(currentActor);
        RequireQuery(skip, take);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await reader.ReadIssuesAsync(skip, take, cancellationToken);
            if (page is null || page.Skip != skip || page.Take != take || page.Items is null ||
                !IsPage(page.Total, skip, take, page.Items.Count))
            {
                throw new AccrualNotificationReadUnavailableException();
            }
            var keys = new HashSet<(Guid, string)>();
            DateTimeOffset? previousDetected = null;
            foreach (var row in page.Items)
            {
                if (row is null || row.ExpenseRequestId == Guid.Empty || !IsText(row.OrganizationName, 500) ||
                    row.Code is not (AccrualNotificationCodes.MissingInitiator or AccrualNotificationCodes.MissingAccountant) ||
                    !keys.Add((row.ExpenseRequestId, row.Code)) || !IsUtc(row.FirstDetectedAtUtc) ||
                    !IsUtc(row.LastDetectedAtUtc) || row.LastDetectedAtUtc < row.FirstDetectedAtUtc ||
                    previousDetected < row.LastDetectedAtUtc)
                {
                    throw new AccrualNotificationReadUnavailableException();
                }
                previousDetected = row.LastDetectedAtUtc;
            }
            return page;
        }
        catch (OperationCanceledException) { throw; }
        catch (ActorForbiddenException) { throw; }
        catch (AccrualNotificationReadUnavailableException) { throw; }
        catch (Exception exception) when (!ProcessFatalExceptionClassifier.IsFatal(exception))
        {
            throw new AccrualNotificationReadUnavailableException(exception);
        }
    }

    private static void RequireQuery(int skip, int take)
    {
        if (skip < 0 || take is < 1 or > 100 || skip > int.MaxValue - take)
        {
            throw new InvalidAccrualNotificationQueryException();
        }
    }

    private static bool IsPage(int total, int skip, int take, int count) =>
        total >= 0 && count == Math.Min(take, Math.Max(0, total - skip));

    private static bool IsUtc(DateTimeOffset value) => value != default &&
        value.Offset == TimeSpan.Zero && value.Ticks % TimeSpan.TicksPerMillisecond == 0;

    private static bool IsText(string value, int maximum) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum && value.Trim() == value && !value.Any(char.IsControl);
}
