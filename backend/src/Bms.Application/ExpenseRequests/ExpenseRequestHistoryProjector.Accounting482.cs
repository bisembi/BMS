using Bms.Application.Abstractions;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Payments;
using System.Text.Json;

namespace Bms.Application.ExpenseRequests;

internal static partial class ExpenseRequestHistoryProjector
{
    private static bool HasSettlementProperty482(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumBusinessSnapshotCharacters) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.EnumerateObject().Any(property =>
                    string.Equals(property.Name, "settlement", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException) { return false; }
    }

    // Current 1C exchange writers persist absolute settlement snapshots, not
    // the old flat workflow result. Validate against the same pure calculator;
    // publish saved business totals only, never a fabricated bank transaction.
    private static ExpenseRequestHistoryEventDto ProjectAccountingPayment482(
        ExpenseRequestAuditSnapshotRow row, Guid? expected, AccountingPaymentHistory482 envelope)
    {
        const string kind = "payment";
        const string title = "Состояние оплаты обновлено";
        var value = envelope.Settlement!;
        if (row.BeforeJson is not null || !MatchesHistoryRequest(envelope.ExpenseRequestId, expected)
            || !MatchesHistoryRequest(envelope.SourceCaptureId, null) || !MatchesHistoryRequest(envelope.DdsCaptureId, null)
            || envelope.PreviousStatus is not null || envelope.AppliedStatus is not null
            || envelope.ObligationAmountKzt is not null || envelope.CashAppliedKzt is not null
            || envelope.ExcessAmountKzt is not null || envelope.CreatedAccrualCycle is not null || envelope.FullyPaidAtUtc is not null
            || !IsHistoryCurrency(value.CurrencyCode) || value.Progress is not (0 or 1 or 2)
            || value.Lines is null || value.Lines.Count is 0 or > ExpenseRequestAllocationPolicy.MaximumLines
            || !IsHistoryMoney(value.ObligationAmountInRequestCurrency)
            || !IsHistoryMoney(value.SettledAmountInRequestCurrency) || !IsHistoryMoney(value.ActualSettledAmountKzt)
            || !IsHistoryMoney(value.OriginalReserveKzt) || !IsHistoryMoney(value.ReleasedReserveKzt)
            || !IsHistoryMoney(value.RemainingReserveKzt) || value.CurrencyDeviationKzt is null
            || !MoneyAmountPolicy.IsRepresentable(value.CurrencyDeviationKzt.Value)
            || value.Lines.Any(line => line is null || !MatchesHistoryRequest(line.LineId, null)
                || line.SortOrder is null || line.SortOrder <= 0
                || !IsHistoryMoney(line.ObligationAmountInRequestCurrency) || !IsHistoryMoney(line.OriginalAmountKzt)
                || !IsHistoryMoney(line.SettledAmountInRequestCurrency) || !IsHistoryMoney(line.ReleasedReserveKzt)
                || !IsHistoryMoney(line.RemainingReserveKzt)))
            return Unavailable(row.OccurredAtUtc, kind, title);

        try
        {
            var calculated = PaymentSettlementProgressCalculator.Calculate(value.CurrencyCode!,
                value.Lines.Select(line => new PaymentSettlementLineSnapshot(Guid.Parse(line!.LineId!),
                    line.SortOrder!.Value, line.ObligationAmountInRequestCurrency!.Value, line.OriginalAmountKzt!.Value)),
                value.SettledAmountInRequestCurrency!.Value, value.ActualSettledAmountKzt!.Value);
            if ((int)calculated.Progress != value.Progress
                || calculated.ObligationAmountInRequestCurrency != value.ObligationAmountInRequestCurrency
                || calculated.OriginalReserveKzt != value.OriginalReserveKzt
                || calculated.ReleasedReserveKzt != value.ReleasedReserveKzt
                || calculated.RemainingReserveKzt != value.RemainingReserveKzt
                || calculated.CurrencyDeviationKzt != value.CurrencyDeviationKzt)
                return Unavailable(row.OccurredAtUtc, kind, title);
            var lines = value.Lines.ToDictionary(line => Guid.Parse(line!.LineId!));
            foreach (var actual in calculated.Lines)
            {
                var recorded = lines[actual.LineId]!;
                if (actual.SettledAmountInRequestCurrency != recorded.SettledAmountInRequestCurrency
                    || actual.ReleasedReserveKzt != recorded.ReleasedReserveKzt
                    || actual.RemainingReserveKzt != recorded.RemainingReserveKzt)
                    return Unavailable(row.OccurredAtUtc, kind, title);
            }
            // Approved non-cash rounding is separately recorded by the newer
            // allocation writer. Do not present it as money sent by the bank.
            if ((envelope.CashAmount is null) != (envelope.ApprovedRoundingAmount is null)
                || (envelope.CashAmount is not null && (!IsHistoryMoney(envelope.CashAmount)
                    || !IsHistoryMoney(envelope.ApprovedRoundingAmount)
                    || envelope.CashAmount + envelope.ApprovedRoundingAmount != value.SettledAmountInRequestCurrency
                    || (envelope.ApprovedRoundingAmount > 0m && value.CurrencyCode != "KZT"))))
                return Unavailable(row.OccurredAtUtc, kind, title);
        }
        catch (Exception exception) when (exception is DomainRuleViolationException or OverflowException or ArgumentException)
        {
            return Unavailable(row.OccurredAtUtc, kind, title);
        }

        var changes = new List<ExpenseRequestHistoryChangeDto>();
        AddDisplayChange(changes, PaymentSection, "Состояние расчётов", null,
            value.Progress == 0 ? "Не оплачено" : value.Progress == 1 ? "Частично оплачено" : "Оплачено");
        AddAmountChange(changes, PaymentSection, "Обязательство заявки", null, value.ObligationAmountInRequestCurrency, null, value.CurrencyCode);
        AddAmountChange(changes, PaymentSection, "Погашено в валюте заявки", null, value.SettledAmountInRequestCurrency, null, value.CurrencyCode);
        AddAmountChange(changes, PaymentSection, "Погашено в эквиваленте тенге", null, value.ActualSettledAmountKzt, null, "KZT");
        AddAmountChange(changes, PaymentSection, "Высвобождено резерва", null, value.ReleasedReserveKzt, null, "KZT");
        AddAmountChange(changes, PaymentSection, "Остаток резерва", null, value.RemainingReserveKzt, null, "KZT");
        if (value.CurrencyCode != "KZT")
            AddAmountChange(changes, PaymentSection, "Курсовое отклонение", null, value.CurrencyDeviationKzt, null, "KZT");
        if (envelope.ApprovedRoundingAmount > 0m)
            AddAmountChange(changes, PaymentSection, "Неденежное округление", null, envelope.ApprovedRoundingAmount, null, "KZT");
        return CompleteBusinessEvent(row, kind, title, changes);
    }

    private sealed class AccountingPaymentHistory482
    {
        public string? ExpenseRequestId { get; init; }
        public string? SourceCaptureId { get; init; }
        public string? DdsCaptureId { get; init; }
        public SettlementHistory482? Settlement { get; init; }
        public decimal? CashAmount { get; init; }
        public decimal? ApprovedRoundingAmount { get; init; }
        // Reject a contradictory mixture of the legacy and current formats.
        public string? PreviousStatus { get; init; }
        public string? AppliedStatus { get; init; }
        public decimal? ObligationAmountKzt { get; init; }
        public decimal? CashAppliedKzt { get; init; }
        public decimal? ExcessAmountKzt { get; init; }
        public bool? CreatedAccrualCycle { get; init; }
        public DateTimeOffset? FullyPaidAtUtc { get; init; }
    }
    private sealed class SettlementHistory482
    {
        public string? CurrencyCode { get; init; }
        public int? Progress { get; init; }
        public decimal? ObligationAmountInRequestCurrency { get; init; }
        public decimal? SettledAmountInRequestCurrency { get; init; }
        public decimal? ActualSettledAmountKzt { get; init; }
        public decimal? OriginalReserveKzt { get; init; }
        public decimal? ReleasedReserveKzt { get; init; }
        public decimal? RemainingReserveKzt { get; init; }
        public decimal? CurrencyDeviationKzt { get; init; }
        public List<SettlementLineHistory482?>? Lines { get; init; }
    }
    private sealed class SettlementLineHistory482
    {
        public string? LineId { get; init; }
        public int? SortOrder { get; init; }
        public decimal? ObligationAmountInRequestCurrency { get; init; }
        public decimal? OriginalAmountKzt { get; init; }
        public decimal? SettledAmountInRequestCurrency { get; init; }
        public decimal? ReleasedReserveKzt { get; init; }
        public decimal? RemainingReserveKzt { get; init; }
    }
}
