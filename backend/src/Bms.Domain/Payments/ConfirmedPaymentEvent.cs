using Bms.Domain.Common;

namespace Bms.Domain.Payments;

/// <summary>
/// Transport-independent immutable shape of one confirmed external payment
/// event. The future Gate G1 adapter is responsible for producing this shape;
/// this contract does not approve an Inbox envelope or a runtime intake path.
/// </summary>
public sealed record ConfirmedPaymentEvent(
    string SourceSystem,
    string ExternalId,
    Guid EventId,
    Guid ExpenseRequestId,
    Guid CurrencyId,
    decimal Amount,
    decimal AmountKzt,
    DateTimeOffset PaidAt,
    string? PaymentNumber,
    bool IsReversal,
    Guid? ReversedEventId);

public static class ConfirmedPaymentEventContract
{
    public const int SourceSystemMaximumLength = 100;
    public const int ExternalIdMaximumLength = 150;
    public const int PaymentNumberMaximumLength = 100;

    public static void Validate(ConfirmedPaymentEvent paymentEvent)
    {
        ArgumentNullException.ThrowIfNull(paymentEvent);

        ValidateStructuralIdentifier(
            paymentEvent.SourceSystem,
            SourceSystemMaximumLength,
            "Payment source system");
        ValidateStructuralIdentifier(
            paymentEvent.ExternalId,
            ExternalIdMaximumLength,
            "Payment external id");

        if (paymentEvent.EventId == Guid.Empty
            || paymentEvent.ExpenseRequestId == Guid.Empty
            || paymentEvent.CurrencyId == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "A confirmed payment event requires nonempty event, request and currency ids.");
        }

        MoneyAmountPolicy.EnsurePositive(
            paymentEvent.Amount,
            "Payment amount in request currency");
        MoneyAmountPolicy.EnsurePositive(
            paymentEvent.AmountKzt,
            "Payment amount in KZT");
        EnsureMillisecondPrecision(paymentEvent.PaidAt, "Payment business time");

        if (paymentEvent.PaymentNumber is { } paymentNumber
            && (paymentNumber.Length > PaymentNumberMaximumLength
                || paymentNumber.Any(char.IsControl)))
        {
            throw new DomainRuleViolationException(
                $"Payment number cannot exceed {PaymentNumberMaximumLength} characters.");
        }

        if ((!paymentEvent.IsReversal && paymentEvent.ReversedEventId is not null)
            || (paymentEvent.IsReversal &&
                (paymentEvent.ReversedEventId is null
                 || paymentEvent.ReversedEventId == Guid.Empty
                 || paymentEvent.ReversedEventId == paymentEvent.EventId)))
        {
            throw new DomainRuleViolationException(
                "A reversal must reference a different nonempty original event, and a payment must not reference one.");
        }
    }

    public static void ValidateProcessingTime(DateTimeOffset processedAtUtc)
    {
        if (processedAtUtc == default || processedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleViolationException(
                "Payment processing time must be a nonempty UTC timestamp.");
        }

        EnsureMillisecondPrecision(processedAtUtc, "Payment processing time");
    }

    private static void ValidateStructuralIdentifier(
        string value,
        int maximumLength,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Length > maximumLength
            || value.Any(char.IsControl))
        {
            throw new DomainRuleViolationException(
                $"{fieldName} must be nonblank, exact and at most {maximumLength} characters.");
        }
    }

    private static void EnsureMillisecondPrecision(
        DateTimeOffset value,
        string fieldName)
    {
        if (value == default || value.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            throw new DomainRuleViolationException(
                $"{fieldName} must fit datetimeoffset(3) without rounding.");
        }
    }
}
