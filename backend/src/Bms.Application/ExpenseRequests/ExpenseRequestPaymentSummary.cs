using Bms.Domain.Payments;

namespace Bms.Application.ExpenseRequests;

/// <summary>
/// The factual settlement state of one expense request, in the only vocabulary
/// the client is allowed to see. Deliberately carries no payment identifier,
/// no source system, no event id and no payment number: a reader who may see
/// the request may see how much of it is settled by confirmed cash plus an
/// applied overpayment, nothing about the records that settled it. The facts
/// flag intentionally describes cash facts only; an applied overpayment can
/// settle a request while this flag remains false.
/// </summary>
public sealed record ExpenseRequestPaymentSummaryDto(
    string CurrencyCode,
    decimal ObligationAmount,
    decimal PaidAmount,
    decimal RemainingAmount,
    string Progress,
    bool HasConfirmedPaymentFacts,
    DateOnly? FullyPaidDate,
    decimal? ObligationAmountKzt,
    decimal? PaidAmountKzt,
    decimal? BankPaymentAmountKzt,
    decimal? CurrencyDeviationKzt)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? ApprovedRoundingAmount { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? CashPaymentAmount { get; init; }
}

public static class ExpenseRequestPaymentProgressCode
{
    public const string Unpaid = "unpaid";
    public const string PartiallyPaid = "partiallyPaid";
    public const string Paid = "paid";

    public static string FromProgress(PaymentSettlementProgress progress) => progress switch
    {
        PaymentSettlementProgress.Unpaid => Unpaid,
        PaymentSettlementProgress.PartiallySettled => PartiallyPaid,
        PaymentSettlementProgress.FullySettled => Paid,
        _ => throw new InvalidOperationException(
            "Payment settlement progress is not defined."),
    };
}

/// <summary>
/// A database outage must never be reported as "not paid": the caller receives
/// a fixed unavailable failure and the endpoint answers 503.
/// </summary>
public sealed class ExpenseRequestPaymentSummaryUnavailableException : Exception
{
    private const string SafeMessage =
        "The expense-request payment summary is temporarily unavailable.";

    public ExpenseRequestPaymentSummaryUnavailableException()
        : base(SafeMessage)
    {
    }

    public ExpenseRequestPaymentSummaryUnavailableException(Exception innerException)
        : base(
            SafeMessage,
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}
