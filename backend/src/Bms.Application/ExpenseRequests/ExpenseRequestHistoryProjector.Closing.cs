using System.Diagnostics.CodeAnalysis;
using Bms.Application.Abstractions;

namespace Bms.Application.ExpenseRequests;

/// Closing history reads the captured control/allocation and line balances, not today's accrual cycle.
internal static partial class ExpenseRequestHistoryProjector
{
    // Validate both snapshots before exposing a reason or amounts from the envelope.
    private static ExpenseRequestHistoryEventDto ProjectClosing(
        ExpenseRequestAuditSnapshotRow row, Guid? expectedRequestId, string commandCode, string title)
    {
        const string kind = "closing";
        if (!TryReadBusinessSnapshot<ClosingHistorySnapshot>(row.BeforeJson, out var before) ||
            !TryReadBusinessSnapshot<ClosingHistoryEnvelope>(row.AfterJson, out var envelope) ||
            !string.Equals(envelope.CommandCode, commandCode, StringComparison.Ordinal) ||
            !(envelope.FunctionalRoleCode == "RESPONSIBLE_ACCOUNTANT" ||
                envelope.FunctionalRoleCode == "CHIEF_ACCOUNTANT") ||
            !IsHistoryText(envelope.Reason) ||
            !IsClosingSnapshot(before, expectedRequestId) || !IsClosingSnapshot(envelope.Closing, expectedRequestId) ||
            !SameHistoryIdentifier(before.ExpenseRequestId, envelope.Closing.ExpenseRequestId) ||
            before.CurrencyCode != envelope.Closing.CurrencyCode ||
            !MatchesClosingBalances(before, envelope.Closing, envelope, commandCode))
        {
            return Unavailable(row.OccurredAtUtc, kind, title);
        }

        var after = envelope.Closing;
        var changes = new List<ExpenseRequestHistoryChangeDto>
        {
            new(ClosingSection, "Причина", "added", null, envelope.Reason),
            new(ClosingSection, "Функциональная роль", "added", null,
                envelope.FunctionalRoleCode == "CHIEF_ACCOUNTANT" ? "Главный бухгалтер" : "Ответственный бухгалтер"),
        };
        AddChange(changes, MainSection, "Статус", before.Status, after.Status, FormatStatus);
        AddChange(changes, ClosingSection, "Дата получения документов",
            before.LatestDocumentsReceivedDate, after.LatestDocumentsReceivedDate, FormatDate);
        AddChange(changes, ClosingSection, "Частичное закрытие",
            before.ClosedPartially, after.ClosedPartially, FormatBoolean);
        AddAmountChange(changes, ClosingSection, "Накопленная контрольная сумма",
            before.ConfirmedClosingControlAmount, after.ConfirmedClosingControlAmount, before.CurrencyCode, after.CurrencyCode);
        if (commandCode != "REOPEN")
        {
            AddAmountChange(changes, ClosingSection, "Контрольная сумма команды", null, envelope.ControlAmount, null, after.CurrencyCode);
            AddAmountChange(changes, ClosingSection, "Распределено по строкам", null, envelope.AllocatedAmount, null, after.CurrencyCode);
            AddAmountChange(changes, ClosingSection, "Сверх остатка к закрытию", null, envelope.ExcessAmount, null, after.CurrencyCode);
        }

        var beforeById = before.Lines.ToDictionary(line => line.LineId, StringComparer.OrdinalIgnoreCase);
        foreach (var line in after.Lines.OrderBy(line => line.SortOrder))
        {
            var prior = beforeById[line.LineId];
            var label = $"Строка {line.SortOrder}";
            AddAmountChange(changes, ClosingSection, $"{label} — Закрытая сумма",
                prior.ConfirmedClosedAmount, line.ConfirmedClosedAmount, before.CurrencyCode, after.CurrencyCode);
            AddAmountChange(changes, ClosingSection, $"{label} — Остаток к закрытию",
                prior.RemainingAmount, line.RemainingAmount, before.CurrencyCode, after.CurrencyCode);
            if (after.CurrencyCode != "KZT")
            {
                AddAmountChange(changes, ClosingSection, $"{label} — Закрытая сумма KZT",
                    prior.ConfirmedClosedAmountKzt, line.ConfirmedClosedAmountKzt, "KZT", "KZT");
                AddAmountChange(changes, ClosingSection, $"{label} — Остаток к закрытию KZT",
                    prior.RemainingAmountKzt, line.RemainingAmountKzt, "KZT", "KZT");
            }
        }

        return CompleteBusinessEvent(row, kind, title, changes);
    }

    // Each balance is complete, exact and identified internally; IDs never become public change values.
    private static bool IsClosingSnapshot([NotNullWhen(true)] ClosingHistorySnapshot? snapshot, Guid? expected)
    {
        if (snapshot is null || !MatchesHistoryRequest(snapshot.ExpenseRequestId, expected) ||
            snapshot.Status is not ("PAID" or "CLOSED") || !IsHistoryCurrency(snapshot.CurrencyCode) ||
            !IsHistoryMoney(snapshot.ConfirmedClosingControlAmount) || snapshot.ClosedPartially is null ||
            snapshot.Lines is null || snapshot.Lines.Any(line => line is null ||
                !MatchesHistoryRequest(line.LineId, null) || line.SortOrder <= 0 ||
                !IsHistoryMoney(line.OriginalAmount) || !IsHistoryMoney(line.OriginalAmountKzt) ||
                !IsHistoryMoney(line.ConfirmedClosedAmount) || !IsHistoryMoney(line.ConfirmedClosedAmountKzt) ||
                !IsHistoryMoney(line.RemainingAmount) || !IsHistoryMoney(line.RemainingAmountKzt) ||
                line.OriginalAmount - line.ConfirmedClosedAmount != line.RemainingAmount ||
                line.OriginalAmountKzt - line.ConfirmedClosedAmountKzt != line.RemainingAmountKzt ||
                (line.RemainingAmount == 0m && line.RemainingAmountKzt != 0m) ||
                (snapshot.CurrencyCode == "KZT" && (line.OriginalAmount != line.OriginalAmountKzt ||
                    line.ConfirmedClosedAmount != line.ConfirmedClosedAmountKzt))))
        {
            return false;
        }

        if (snapshot.Status == "CLOSED" && (snapshot.ClosedPartially != false ||
            snapshot.Lines.Any(line => line.RemainingAmount != 0m)))
        {
            return false;
        }

        return snapshot.Lines.Select(line => line.LineId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == snapshot.Lines.Count &&
            snapshot.Lines.Select(line => line.SortOrder).Distinct().Count() == snapshot.Lines.Count;
    }

    // Reopen resets balances; close commands add the recorded allocation to the same immutable lines.
    private static bool MatchesClosingBalances(
        ClosingHistorySnapshot before, ClosingHistorySnapshot after, ClosingHistoryEnvelope envelope, string command)
    {
        if (!IsHistoryMoney(envelope.AllocatedAmount) || !IsHistoryMoney(envelope.AllocatedAmountKzt) ||
            !IsHistoryMoney(envelope.ExcessAmount) || before.Lines.Count != after.Lines.Count)
        {
            return false;
        }

        var beforeById = before.Lines.ToDictionary(line => line.LineId, StringComparer.OrdinalIgnoreCase);
        foreach (var line in after.Lines)
        {
            if (!beforeById.TryGetValue(line.LineId, out var prior) || line.SortOrder != prior.SortOrder ||
                line.OriginalAmount != prior.OriginalAmount || line.OriginalAmountKzt != prior.OriginalAmountKzt ||
                (command != "REOPEN" && (line.ConfirmedClosedAmount < prior.ConfirmedClosedAmount ||
                    line.ConfirmedClosedAmountKzt < prior.ConfirmedClosedAmountKzt)))
            {
                return false;
            }
        }

        if (command == "REOPEN")
        {
            return before.Status == "CLOSED" && after.Status == "PAID" &&
                after.ConfirmedClosingControlAmount == 0m && after.ClosedPartially == false &&
                envelope.ControlAmount is null && envelope.DocumentsReceivedDate is null &&
                after.LatestDocumentsReceivedDate is null &&
                envelope.AllocatedAmount == 0m && envelope.AllocatedAmountKzt == 0m && envelope.ExcessAmount == 0m &&
                after.Lines.All(line => line.ConfirmedClosedAmount == 0m && line.ConfirmedClosedAmountKzt == 0m);
        }

        return before.Status == "PAID" &&
            (command == "CLOSE_PARTIAL" ? after.Status == "PAID" && after.ClosedPartially == true :
                after.Status == "CLOSED" && after.ClosedPartially == false && after.Lines.All(line => line.RemainingAmount == 0m)) &&
            envelope.DocumentsReceivedDate is not null && after.LatestDocumentsReceivedDate == envelope.DocumentsReceivedDate &&
            IsHistoryMoney(envelope.ControlAmount) &&
            envelope.ControlAmount == envelope.AllocatedAmount + envelope.ExcessAmount &&
            after.ConfirmedClosingControlAmount - before.ConfirmedClosingControlAmount == envelope.ControlAmount &&
            after.Lines.Sum(line => line.ConfirmedClosedAmount) - before.Lines.Sum(line => line.ConfirmedClosedAmount) == envelope.AllocatedAmount &&
            after.Lines.Sum(line => line.ConfirmedClosedAmountKzt) - before.Lines.Sum(line => line.ConfirmedClosedAmountKzt) == envelope.AllocatedAmountKzt;
    }

    private sealed class ClosingHistoryEnvelope
    {
        public string? CommandCode { get; init; }
        public string? FunctionalRoleCode { get; init; }
        public string? Reason { get; init; }
        public DateOnly? DocumentsReceivedDate { get; init; }
        public decimal? ControlAmount { get; init; }
        public decimal? AllocatedAmount { get; init; }
        public decimal? AllocatedAmountKzt { get; init; }
        public decimal? ExcessAmount { get; init; }
        public ClosingHistorySnapshot? Closing { get; init; }
    }

    private sealed class ClosingHistorySnapshot
    {
        public string? ExpenseRequestId { get; init; }
        public string? Status { get; init; }
        public string? CurrencyCode { get; init; }
        public DateOnly? LatestDocumentsReceivedDate { get; init; }
        public decimal? ConfirmedClosingControlAmount { get; init; }
        public bool? ClosedPartially { get; init; }
        public required IReadOnlyList<ClosingHistoryLine> Lines { get; init; }
    }

    private sealed class ClosingHistoryLine
    {
        public required string LineId { get; init; }
        public int SortOrder { get; init; }
        public decimal? OriginalAmount { get; init; }
        public decimal? OriginalAmountKzt { get; init; }
        public decimal? ConfirmedClosedAmount { get; init; }
        public decimal? ConfirmedClosedAmountKzt { get; init; }
        public decimal? RemainingAmount { get; init; }
        public decimal? RemainingAmountKzt { get; init; }
    }
}
