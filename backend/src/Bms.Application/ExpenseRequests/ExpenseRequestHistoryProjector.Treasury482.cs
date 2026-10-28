using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

internal static partial class ExpenseRequestHistoryProjector
{
    // The treasury writer stores before as projection rows, not a request DTO.
    // Each saved row carries its request identity and full budget dimensions.
    private static ExpenseRequestHistoryEventDto ProjectTreasuryBudget482(
        ExpenseRequestAuditSnapshotRow row, Guid? expected, string title)
    {
        const string kind = "workflow";
        if (!TryReadBusinessSnapshot<TreasuryBudgetHistory482>(row.AfterJson, out var after)
            || !TryReadTreasuryBefore482(row.BeforeJson, out var before)
            || after.Month is not { Day: 1 } month || month == default
            || !IsHistoryMoney(after.Deficit) || after.RepeatedApproval is null
            || !ValidBudgetProjections482(after.Projections, expected, month, allowEmpty: false)
            || !ValidBudgetProjections482(before, expected, null, allowEmpty: true))
            return Unavailable(row.OccurredAtUtc, kind, title);

        var request = after.Projections![0]!.ExpenseRequestId;
        var previousMonths = before.Select(item => item!.BudgetMonth!.Value).Distinct().ToArray();
        if (previousMonths.Length > 1 || before.Any(item => !SameHistoryIdentifier(item!.ExpenseRequestId, request))
            || (row.Action == "TreasuryBudgetRechecked"
                && (after.RepeatedApproval != false || previousMonths.Any(previous => previous != month)))
            || (row.Action == "TreasuryBudgetMonthChanged"
                && (after.RepeatedApproval != (after.Deficit > 0m) || previousMonths.Any(previous => previous == month))))
            return Unavailable(row.OccurredAtUtc, kind, title);

        decimal previousReserve, nextReserve;
        try
        {
            previousReserve = before.Sum(item => item!.ReservedAmountKzt!.Value);
            nextReserve = after.Projections.Sum(item => item!.ReservedAmountKzt!.Value);
            if (!IsHistoryMoney(previousReserve) || !IsHistoryMoney(nextReserve))
                return Unavailable(row.OccurredAtUtc, kind, title);
        }
        catch (OverflowException) { return Unavailable(row.OccurredAtUtc, kind, title); }

        var changes = new List<ExpenseRequestHistoryChangeDto>();
        // AddDisplayChange retains an unchanged before/after value so a check
        // is distinguishable from an event with no saved business details.
        AddDisplayChange(changes, "Бюджет", "Месяц бюджета",
            previousMonths.Length == 1 ? FormatMonth(previousMonths[0]) : null, FormatMonth(month));
        AddDisplayChange(changes, "Бюджет", "Резерв заявки",
            before.Count == 0 ? null : FormatAmount(previousReserve, "KZT"), FormatAmount(nextReserve, "KZT"));
        AddAmountChange(changes, "Бюджет", "Превышение бюджета при проверке", null, after.Deficit, null, "KZT");
        AddDisplayChange(changes, WorkflowSection, "Повторное согласование", null, FormatBoolean(after.RepeatedApproval));
        return CompleteBusinessEvent(row, kind, title, changes, partial: before.Count == 0);
    }

    private static bool TryReadTreasuryBefore482(string? json, out List<BudgetProjectionHistory482?> before)
    {
        before = [];
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumBusinessSnapshotCharacters) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array || !HasUniqueJsonProperties(document.RootElement)) return false;
            var value = document.RootElement.Deserialize<List<BudgetProjectionHistory482?>>(JsonOptions);
            if (value is null) return false;
            before = value;
            return true;
        }
        catch (JsonException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    private static bool ValidBudgetProjections482(List<BudgetProjectionHistory482?>? rows,
        Guid? expected, DateOnly? month, bool allowEmpty)
    {
        if (rows is null || (!allowEmpty && rows.Count == 0) || rows.Count > ExpenseRequestAllocationPolicy.MaximumLines) return false;
        var lines = new HashSet<Guid>();
        foreach (var value in rows)
        {
            if (value is null || !MatchesHistoryRequest(value.ExpenseRequestId, expected)
                || !MatchesHistoryRequest(value.ExpenseRequestLineId, null)
                || !SameHistoryIdentifier(value.Id, value.ExpenseRequestLineId)
                || !lines.Add(Guid.Parse(value.ExpenseRequestLineId!))
                || !MatchesHistoryRequest(value.CostCenterId, null) || !MatchesHistoryRequest(value.CashFlowItemId, null)
                || value.BudgetMonth is not { Day: 1 } savedMonth || savedMonth == default
                || (month is not null && savedMonth != month)
                || !IsHistoryMoney(value.ReservedAmountKzt) || value.Key is null
                || !SameHistoryIdentifier(value.CostCenterId, value.Key.CostCenterId)
                || !SameHistoryIdentifier(value.CashFlowItemId, value.Key.CashFlowItemId)
                || value.Key.BudgetMonth != savedMonth)
                return false;
        }
        return rows.Select(value => value!.ExpenseRequestId).Distinct(StringComparer.OrdinalIgnoreCase).Count() <= 1;
    }

    private sealed class TreasuryBudgetHistory482
    {
        public DateOnly? Month { get; init; }
        public decimal? Deficit { get; init; }
        public bool? RepeatedApproval { get; init; }
        public List<BudgetProjectionHistory482?>? Projections { get; init; }
    }
    private sealed class BudgetProjectionHistory482
    {
        public string? Id { get; init; }
        public string? ExpenseRequestId { get; init; }
        public string? ExpenseRequestLineId { get; init; }
        public string? CostCenterId { get; init; }
        public string? CashFlowItemId { get; init; }
        public DateOnly? BudgetMonth { get; init; }
        public decimal? ReservedAmountKzt { get; init; }
        public BudgetKeyHistory482? Key { get; init; }
    }
    private sealed class BudgetKeyHistory482
    {
        public string? CostCenterId { get; init; }
        public string? CashFlowItemId { get; init; }
        public DateOnly? BudgetMonth { get; init; }
    }
}
