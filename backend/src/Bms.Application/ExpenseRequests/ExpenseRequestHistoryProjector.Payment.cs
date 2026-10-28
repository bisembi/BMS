using Bms.Application.Abstractions;

namespace Bms.Application.ExpenseRequests;

/// PaymentApplied records a recomputed absolute state, including after a reversal; it is not a new payment amount.
internal static partial class ExpenseRequestHistoryProjector
{
    // Publish recorded balances only, never derive a transaction amount from status or processing time.
    private static ExpenseRequestHistoryEventDto ProjectPayment(ExpenseRequestAuditSnapshotRow row, Guid? expectedRequestId)
    {
        const string kind = "payment";
        const string title = "Состояние оплаты обновлено";
        if (HasSettlementProperty482(row.AfterJson))
        {
            return TryReadBusinessSnapshot<AccountingPaymentHistory482>(row.AfterJson, out var accounting)
                && accounting.Settlement is not null
                    ? ProjectAccountingPayment482(row, expectedRequestId, accounting)
                    : Unavailable(row.OccurredAtUtc, kind, title);
        }
        if (row.BeforeJson is not null ||
            !TryReadBusinessSnapshot<PaymentHistorySnapshot>(row.AfterJson, out var payment) ||
            !MatchesHistoryRequest(payment.ExpenseRequestId, expectedRequestId) ||
            payment.PreviousStatus is not ("TO_PAY" or "PARTIALLY_PAID" or "PAID") ||
            !IsHistoryMoney(payment.ObligationAmountKzt) || payment.ObligationAmountKzt == 0m ||
            !IsHistoryMoney(payment.CashAppliedKzt) || !IsHistoryMoney(payment.ExcessAmountKzt) ||
            payment.CashAppliedKzt > payment.ObligationAmountKzt ||
            (payment.ExcessAmountKzt > 0m && payment.CashAppliedKzt != payment.ObligationAmountKzt) ||
            payment.AppliedStatus != (payment.CashAppliedKzt == 0m ? "TO_PAY" :
                payment.CashAppliedKzt == payment.ObligationAmountKzt ? "PAID" : "PARTIALLY_PAID") ||
            payment.CreatedAccrualCycle is null ||
            (payment.CreatedAccrualCycle == true && payment.AppliedStatus != "PAID") ||
            (payment.AppliedStatus == "PAID" && payment.FullyPaidAtUtc is null) ||
            (payment.FullyPaidAtUtc is { } paidAt && (paidAt == default || paidAt.Offset != TimeSpan.Zero)))
        {
            return Unavailable(row.OccurredAtUtc, kind, title);
        }

        var changes = new List<ExpenseRequestHistoryChangeDto>();
        AddChange(changes, MainSection, "Статус", payment.PreviousStatus, payment.AppliedStatus, FormatStatus);
        AddAmountChange(changes, PaymentSection, "Обязательство заявки", null, payment.ObligationAmountKzt, null, "KZT");
        AddAmountChange(changes, PaymentSection, "Применено к заявке", null, payment.CashAppliedKzt, null, "KZT");
        AddAmountChange(changes, PaymentSection, "Переплата", null, payment.ExcessAmountKzt, null, "KZT");
        if (payment.FullyPaidAtUtc is { } date)
        {
            AddDisplayChange(changes, PaymentSection, "Исходная дата полной оплаты (UTC)", null,
                date.ToString("dd.MM.yyyy HH:mm:ss 'UTC'", RussianCulture));
        }

        return CompleteBusinessEvent(row, kind, title, changes);
    }

    private sealed class PaymentHistorySnapshot
    {
        public string? ExpenseRequestId { get; init; }
        public string? PreviousStatus { get; init; }
        public string? AppliedStatus { get; init; }
        public decimal? ObligationAmountKzt { get; init; }
        public decimal? CashAppliedKzt { get; init; }
        public decimal? ExcessAmountKzt { get; init; }
        public DateTimeOffset? FullyPaidAtUtc { get; init; }
        public bool? CreatedAccrualCycle { get; init; }
    }
}
