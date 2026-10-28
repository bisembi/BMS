using Bms.Application.ExpenseRequests;

namespace Bms.Application.Abstractions;

public interface IExpenseRequestPaymentDetailsReader
{
    /// Exact full-card visibility, currency and all facts within the caller's
    /// Serializable transaction. Never filter out contradictory ledger facts.
    Task<ExpenseRequestPaymentDetailsSnapshot?> ReadVisibleAsync(
        Guid requestId, string actorExternalId, bool includeAll,
        CancellationToken cancellationToken);
}
