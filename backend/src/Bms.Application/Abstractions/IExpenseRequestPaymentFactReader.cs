using Bms.Domain.Payments;
using Bms.Application.ExpenseRequests;

namespace Bms.Application.Abstractions;

/// <summary>
/// Reads the confirmed payment ledger of one expense request. Read-only by
/// contract: no manual payment path exists anywhere in the application, so
/// nothing here writes, and the confirmed facts remain the single source.
/// </summary>
public interface IExpenseRequestPaymentFactReader
{
    Task<IReadOnlyList<ApprovedPaymentRounding400>> ReadApprovedRoundingAsync(
        Guid expenseRequestId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ApprovedPaymentRounding400>>([]);

    Task<IReadOnlyList<ConfirmedPaymentEvent>> ReadForRequestAsync(
        Guid expenseRequestId,
        Guid currencyId,
        CancellationToken cancellationToken);

    Task<Guid?> FindCurrencyIdAsync(
        string currencyCode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads every allocation state for the target request together with the
    /// exact source scope. Filtering to Applied here would hide corrupt or
    /// foreign rows from the fail-closed settlement projector.
    /// </summary>
    Task<IReadOnlyList<ExpenseRequestAppliedOverpaymentSnapshot>>
        ReadOverpaymentAllocationsForRequestAsync(
            Guid expenseRequestId,
            CancellationToken cancellationToken);
}
