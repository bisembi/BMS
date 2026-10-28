using Bms.Domain.Payments;

namespace Bms.Application.ExpenseRequests;

/// Internal capture only. Source identities never belong to the public DTO.
public sealed record ExpenseRequestPaymentDetailsSnapshot(
    Guid ExpenseRequestId, Guid CurrencyId, string CurrencyCode,
    IReadOnlyList<ConfirmedPaymentEvent> Events);

public sealed record ExpenseRequestPaymentDetailDto(
    int Position, DateTimeOffset PaidAt, string? PaymentNumber,
    decimal Amount, decimal AmountKzt, bool IsReversal);

/// The complete monetary ledger, including excess; not the settled obligation.
public sealed record ExpenseRequestPaymentDetailsTotalsDto(
    decimal OriginalAmount, decimal OriginalAmountKzt,
    decimal ReversedAmount, decimal ReversedAmountKzt,
    decimal NetAmount, decimal NetAmountKzt);

public sealed record ExpenseRequestPaymentDetailsDto(
    string CurrencyCode, int Total, int Skip, int Take,
    ExpenseRequestPaymentDetailsTotalsDto Totals,
    IReadOnlyList<ExpenseRequestPaymentDetailDto> Items);

public sealed class ExpenseRequestPaymentDetailsUnavailableException : Exception
{
    public ExpenseRequestPaymentDetailsUnavailableException()
        : base("Payment details could not be read consistently.") { }
    public ExpenseRequestPaymentDetailsUnavailableException(Exception inner)
        : base("Payment details could not be read consistently.", inner) { }
}
