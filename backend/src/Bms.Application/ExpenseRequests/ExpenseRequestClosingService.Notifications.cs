using Bms.Application.Accruals.Notifications;
using Bms.Domain.Workflow;

namespace Bms.Application.ExpenseRequests;

internal sealed partial class ExpenseRequestClosingService
{
    private async Task CheckAccrualNotificationsAsync(Guid id, string commandCode,
        string actorExternalId, DateTimeOffset atUtc, CancellationToken cancellationToken)
    {
        // The request and new cycle are saved, but still in the SAME transaction.
        // The earlier replay return deliberately never reaches this hook.
        var outcome = await notificationControl.CheckAsync(id, actorExternalId,
            commandCode == ExpenseRequestWorkflowCodes.ReopenCommandCode
                ? AccrualNotificationTrigger.Reopened : AccrualNotificationTrigger.Closing,
            atUtc, cancellationToken);
        if (outcome.HasChanges)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
