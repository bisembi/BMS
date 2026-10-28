using Bms.Domain.Payments;

namespace Bms.Infrastructure.Persistence;

internal static class TreasuryPaymentFactLockKeys
{
    internal static IReadOnlyList<string> Create(ConfirmedPaymentEvent paymentEvent)
    {
        ConfirmedPaymentEventContract.Validate(paymentEvent);

        var resources = new List<string>
        {
            BusinessManagedLockResources.ExpenseRequest(paymentEvent.ExpenseRequestId),
            BusinessManagedLockResources.TreasuryEvent(paymentEvent.EventId),
            BusinessManagedLockResources.TreasuryPaymentIdentity(
                paymentEvent.SourceSystem,
                paymentEvent.ExternalId)
        };
        if (paymentEvent.ReversedEventId is Guid reversedEventId)
        {
            resources.Add(BusinessManagedLockResources.TreasuryEvent(reversedEventId));
        }

        return BusinessManagedLockResources.Order(resources);
    }
}
