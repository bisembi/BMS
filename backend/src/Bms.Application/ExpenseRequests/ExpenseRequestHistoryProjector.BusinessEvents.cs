using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Domain.Common;

namespace Bms.Application.ExpenseRequests;

/// Closed action dispatch and bounded, unambiguous parsing for persisted business events.
internal static partial class ExpenseRequestHistoryProjector
{
    private const int MaximumBusinessSnapshotCharacters = 1_000_000;
    private const string WorkflowSection = "Согласование";
    private const string ClosingSection = "Закрытие";
    private const string PaymentSection = "Оплата";

    // An unknown future action returns null so the existing safe fallback remains visible.
    private static ExpenseRequestHistoryEventDto? ProjectBusinessEvent(
        ExpenseRequestAuditSnapshotRow row, Guid? expectedRequestId) => row.Action switch
        {
            "AttachmentAdded" => ProjectAttachment474(row, expectedRequestId, false),
            "AttachmentDeleted" => ProjectAttachment474(row, expectedRequestId, true),
            "SubmittedForApproval" => ProjectWorkflow(row, expectedRequestId, "SUBMIT", "Заявка подана на согласование"),
            "BudgetRouteRecalculated" => ProjectWorkflow(row, expectedRequestId, "RECALCULATE_BUDGET_ROUTE", "Маршрут пересчитан по бюджету и факту"),
            "SubmittedApproved" => ProjectWorkflow(row, expectedRequestId, "SUBMIT", "Заявка утверждена при подаче"),
            "SubmittedToPayment" => ProjectWorkflow(row, expectedRequestId, "SUBMIT", "Заявка передана в оплату"),
            "SubmittedToNsi" => ProjectWorkflow(row, expectedRequestId, "SUBMIT", "Заявка передана на проверку реквизитов НСИ"),
            "WorkflowApproved" => ProjectWorkflow(row, expectedRequestId, "APPROVE", "Согласование выполнено"),
            "WorkflowReturned" => ProjectWorkflow(row, expectedRequestId, "RETURN", "Заявка возвращена"),
            "WorkflowRejected" => ProjectWorkflow(row, expectedRequestId, "REJECT", "Заявка отклонена"),
            "WorkflowCancelled" => ProjectWorkflow(row, expectedRequestId, "CANCEL", "Заявка отменена"),
            "WorkflowNsiConfirmed" => ProjectWorkflow(row, expectedRequestId, "CONFIRM_NSI", "НСИ подтверждена"),
            "WorkflowSentToPayment" => ProjectWorkflow(row, expectedRequestId, "SEND_TO_PAYMENT", "Заявка передана в оплату"),
            "ExpenseRequestClosedPartially" => ProjectClosing(row, expectedRequestId, "CLOSE_PARTIAL", "Заявка закрыта частично"),
            "ExpenseRequestClosedFully" => ProjectClosing(row, expectedRequestId, "CLOSE_FULL", "Заявка закрыта"),
            "ExpenseRequestReopened" => ProjectClosing(row, expectedRequestId, "REOPEN", "Заявка открыта повторно"),
            "PaymentApplied" => ProjectPayment(row, expectedRequestId),
            "TreasuryBudgetRechecked" => ProjectTreasuryBudget482(row, expectedRequestId, "Бюджет проверен перед передачей в оплату"),
            "TreasuryBudgetMonthChanged" => ProjectTreasuryBudget482(row, expectedRequestId, "Месяц бюджета изменён казначеем"),
            _ => null,
        };

    // Never accept duplicate JSON names, including case variants understood by Web deserialization.
    private static bool TryReadBusinessSnapshot<T>(
        string? json, [NotNullWhen(true)] out T? snapshot) where T : class
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumBusinessSnapshotCharacters)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !HasUniqueJsonProperties(document.RootElement))
            {
                return false;
            }

            snapshot = document.RootElement.Deserialize<T>(JsonOptions);
            return snapshot is not null;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    // JsonDocument has a depth limit; the input-length guard bounds the walk and collection size.
    private static bool HasUniqueJsonProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || !HasUniqueJsonProperties(property.Value))
                {
                    return false;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (!HasUniqueJsonProperties(item))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // Apply the same text/length contract as the public readers before returning any details.
    private static ExpenseRequestHistoryEventDto CompleteBusinessEvent(
        ExpenseRequestAuditSnapshotRow row, string kind, string title,
        List<ExpenseRequestHistoryChangeDto> changes, bool partial = false, string? initiator = null)
    {
        if (changes.Any(change => !IsHistoryText(change.Section, 200) ||
            !IsHistoryText(change.Field, 300) ||
            (change.Before is not null && !IsHistoryText(change.Before)) ||
            (change.After is not null && !IsHistoryText(change.After))))
        {
            return Unavailable(row.OccurredAtUtc, kind, title);
        }

        var omitted = Math.Max(changes.Count - MaximumChangesPerEvent, 0);
        return new ExpenseRequestHistoryEventDto(
            row.OccurredAtUtc, kind, title,
            initiator is not null && string.Equals(initiator, row.ActorExternalId, StringComparison.Ordinal)
                ? "Инициатор" : "Пользователь BMS",
            partial || omitted > 0 ? "partial" : "complete",
            changes.Take(MaximumChangesPerEvent).ToArray(), omitted);
    }

    // User-authored text is escaped by the UI; control characters are not part of the public wire contract.
    private static bool IsHistoryText(string? value, int maximumLength = 4_000) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength &&
        value.Trim() == value && !value.Any(character => character <= 0x1f || character is >= '\u007f' and <= '\u009f');

    // UUID case is immaterial, but malformed, empty or wrong-card identities cannot authorize details.
    private static bool MatchesHistoryRequest(string? value, Guid? expected) =>
        Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty &&
        (expected is null || id == expected);

    // Before and after must describe one immutable request, independent of UUID letter case.
    private static bool SameHistoryIdentifier(string? left, string? right) =>
        Guid.TryParseExact(right, "D", out var expected) && MatchesHistoryRequest(left, expected);

    // Monetary fields are read exactly; history must not silently round damaged storage values.
    private static bool IsHistoryMoney(decimal? amount) =>
        amount is { } value && value >= 0m && MoneyAmountPolicy.IsRepresentable(value);

    // The stored currency token is uppercase ISO-shaped text, never a free-form diagnostic label.
    private static bool IsHistoryCurrency(string? value) =>
        value is { Length: 3 } && value.All(character => character is >= 'A' and <= 'Z');
}
