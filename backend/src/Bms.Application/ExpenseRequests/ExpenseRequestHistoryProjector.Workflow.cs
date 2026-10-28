using System.Diagnostics.CodeAnalysis;
using Bms.Application.Abstractions;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// Read the immutable before/after DTO or workflow envelope, without re-resolving today's roles.
internal static partial class ExpenseRequestHistoryProjector
{
    // Only the action-matched envelope and its recorded business changes are public.
    private static ExpenseRequestHistoryEventDto ProjectWorkflow(
        ExpenseRequestAuditSnapshotRow row, Guid? expectedRequestId, string commandCode, string title)
    {
        const string kind = "workflow";
        AuditSnapshot? after;
        string? reason = null;
        if (commandCode == "SUBMIT")
        {
            if (!TryReadBusinessSnapshot(row.AfterJson, out after))
            {
                return Unavailable(row.OccurredAtUtc, kind, title);
            }
        }
        else
        {
            if (!TryReadBusinessSnapshot<WorkflowAuditSnapshot>(row.AfterJson, out var envelope) ||
                !string.Equals(envelope.CommandCode, commandCode, StringComparison.Ordinal))
            {
                return Unavailable(row.OccurredAtUtc, kind, title);
            }

            after = envelope.ExpenseRequest;
            reason = envelope.Reason;
        }

        if (!TryReadBusinessSnapshot<AuditSnapshot>(row.BeforeJson, out var before) ||
            !IsWorkflowSnapshot(before, expectedRequestId) || !IsWorkflowSnapshot(after, expectedRequestId) ||
            !SameHistoryIdentifier(before.Id, after.Id) ||
            !string.Equals(before.InitiatorExternalId, after.InitiatorExternalId, StringComparison.Ordinal) ||
            !MatchesRecordedWorkflowTransition(commandCode, before.Status, after.Status) ||
            (reason is not null && !IsHistoryText(reason)) ||
            (commandCode is "RETURN" or "REJECT" or "CANCEL" && reason is null)
            || (commandCode == "SUBMIT" && after.Status != (row.Action == "SubmittedToPayment" ? "TO_PAY"
                : row.Action == "SubmittedToNsi" ? "AWAITING_NSI" : row.Action == "SubmittedApproved" ? "APPROVED" : "IN_APPROVAL")))
        {
            return Unavailable(row.OccurredAtUtc, kind, title);
        }

        var changes = BuildChanges(before, after, out var partial, row.References);
        if (before.BudgetReservation is not null && after.BudgetReservation is not null)
        {
            AddAmountChange(changes, "Бюджет", "Резерв бюджета",
                before.BudgetReservation.ReservedAmountKzt, after.BudgetReservation.ReservedAmountKzt, "KZT", "KZT");
        }
        if (reason is not null)
        {
            changes.Insert(0, new ExpenseRequestHistoryChangeDto(WorkflowSection, "Причина", "added", null, reason));
        }

        var decisionTitle = commandCode == "APPROVE" && after.Status is "APPROVED" or "AWAITING_NSI"
            ? "Заявка утверждена" : title;
        return CompleteBusinessEvent(row, kind, decisionTitle, changes, partial, after.InitiatorExternalId);
    }

    // Draft cancellation may have incomplete business references, but its typed DTO and line identities exist.
    private static bool IsWorkflowSnapshot([NotNullWhen(true)] AuditSnapshot? snapshot, Guid? expected) =>
        snapshot is not null && HasRequiredSnapshotShape(snapshot) && MatchesHistoryRequest(snapshot.Id, expected) &&
        ExpenseRequestStatusCode.TryParse(snapshot.Status, out _) && IsHistoryCurrency(snapshot.CurrencyCode) &&
        IsHistoryMoney(snapshot.TotalAmount) && snapshot.Lines is not null &&
        (snapshot.BudgetReservation is null || IsHistoryMoney(snapshot.BudgetReservation.ReservedAmountKzt)) &&
        snapshot.Lines.All(line => line is not null && MatchesHistoryRequest(line.Id, null)) &&
        snapshot.Lines.Select(line => line.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == snapshot.Lines.Count;

    // This checks the historical command/status pair, not current permissions or a new workflow decision.
    private static bool MatchesRecordedWorkflowTransition(string command, string? before, string? after) => command switch
    {
        "SUBMIT" => before is "DRAFT" or "RETURNED" && after is "IN_APPROVAL" or "AWAITING_NSI" or "TO_PAY" or "APPROVED",
        "APPROVE" => before == "IN_APPROVAL" && after is "IN_APPROVAL" or "APPROVED" or "AWAITING_NSI" or "TO_PAY",
        "RETURN" => before is "IN_APPROVAL" or "APPROVED" or "TO_PAY" && after == "RETURNED",
        "REJECT" => before == "IN_APPROVAL" && after == "REJECTED",
        "CANCEL" => before is "DRAFT" or "RETURNED" or "IN_APPROVAL" or "AWAITING_NSI" or "APPROVED" or "TO_PAY" && after == "CANCELLED",
        "CONFIRM_NSI" => before == "AWAITING_NSI" && after is "APPROVED" or "TO_PAY",
        "SEND_TO_PAYMENT" => before == "APPROVED" && after == "TO_PAY",
        "RECALCULATE_BUDGET_ROUTE" => before is "IN_APPROVAL" or "APPROVED" or "AWAITING_NSI" && after == "IN_APPROVAL",
        _ => false,
    };

    private sealed class WorkflowAuditSnapshot
    {
        public string? CommandCode { get; init; }
        public string? Reason { get; init; }
        public AuditSnapshot? ExpenseRequest { get; init; }
    }
}
