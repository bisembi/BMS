namespace Bms.Domain.ExpenseRequests;

using Bms.Domain.Common;
using Bms.Domain.Workflow;

// The enum is the aggregate state; the persisted/public token is always the
// exact workflow-registry code, never Enum.ToString(). This keeps a Russian UI
// label, the SQL registry and HTTP filters from silently diverging.
public enum ExpenseRequestStatus
{
    Draft = 1,
    Returned = 2,
    InApproval = 3,
    Approved = 4,
    AwaitingNsi = 5,
    ToPay = 6,
    PartiallyPaid = 7,
    Paid = 8,
    Closed = 9,
    Rejected = 10,
    Cancelled = 11
}

public static class ExpenseRequestStatusCode
{
    private static readonly IReadOnlyDictionary<ExpenseRequestStatus, string> codeByStatus =
        new Dictionary<ExpenseRequestStatus, string>
        {
            [ExpenseRequestStatus.Draft] = ExpenseRequestWorkflowCodes.DraftStatusCode,
            [ExpenseRequestStatus.Returned] = ExpenseRequestWorkflowCodes.ReturnedStatusCode,
            [ExpenseRequestStatus.InApproval] = ExpenseRequestWorkflowCodes.InApprovalStatusCode,
            [ExpenseRequestStatus.Approved] = ExpenseRequestWorkflowCodes.ApprovedStatusCode,
            [ExpenseRequestStatus.AwaitingNsi] = ExpenseRequestWorkflowCodes.AwaitingNsiStatusCode,
            [ExpenseRequestStatus.ToPay] = ExpenseRequestWorkflowCodes.ToPayStatusCode,
            [ExpenseRequestStatus.PartiallyPaid] = ExpenseRequestWorkflowCodes.PartiallyPaidStatusCode,
            [ExpenseRequestStatus.Paid] = ExpenseRequestWorkflowCodes.PaidStatusCode,
            [ExpenseRequestStatus.Closed] = ExpenseRequestWorkflowCodes.ClosedStatusCode,
            [ExpenseRequestStatus.Rejected] = ExpenseRequestWorkflowCodes.RejectedStatusCode,
            [ExpenseRequestStatus.Cancelled] = ExpenseRequestWorkflowCodes.CancelledStatusCode
        };

    private static readonly IReadOnlyDictionary<string, ExpenseRequestStatus> statusByCode =
        codeByStatus.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<ExpenseRequestStatus, string> displayNameByStatus =
        new Dictionary<ExpenseRequestStatus, string>
        {
            [ExpenseRequestStatus.Draft] = "Черновик",
            [ExpenseRequestStatus.Returned] = "Возвращена",
            [ExpenseRequestStatus.InApproval] = "На согласовании",
            [ExpenseRequestStatus.Approved] = "Утверждено",
            [ExpenseRequestStatus.AwaitingNsi] = "Ожидает НСИ",
            [ExpenseRequestStatus.ToPay] = "К оплате",
            [ExpenseRequestStatus.PartiallyPaid] = "Оплачена частично",
            [ExpenseRequestStatus.Paid] = "Оплачена",
            [ExpenseRequestStatus.Closed] = "Закрыта",
            [ExpenseRequestStatus.Rejected] = "Отклонена",
            [ExpenseRequestStatus.Cancelled] = "Отменена"
        };

    public static IReadOnlyCollection<string> All =>
        Array.AsReadOnly(codeByStatus.Values.ToArray());

    public static string ToCode(this ExpenseRequestStatus status) =>
        codeByStatus.TryGetValue(status, out var code)
            ? code
            : throw new DomainRuleViolationException("Expense-request status is not defined.");

    public static string ToDisplayName(this ExpenseRequestStatus status) =>
        displayNameByStatus.TryGetValue(status, out var displayName)
            ? displayName
            : throw new DomainRuleViolationException("Expense-request status display name is not defined.");

    public static bool TryParse(string? code, out ExpenseRequestStatus status)
    {
        if (code is not null && statusByCode.TryGetValue(code, out status))
        {
            return true;
        }

        status = default;
        return false;
    }

    public static ExpenseRequestStatus Parse(string? code)
    {
        if (TryParse(code, out var status))
        {
            return status;
        }

        throw new DomainRuleViolationException("Expense-request status code is not canonical.");
    }

    public static bool IsEditableDraft(this ExpenseRequestStatus status) =>
        status is ExpenseRequestStatus.Draft or ExpenseRequestStatus.Returned;

    /// <summary>
    /// Additional evidence may be appended while the submitted request is
    /// still in its business process. Terminal outcomes are immutable.
    /// </summary>
    public static bool CanAcceptAdditionalAttachment(this ExpenseRequestStatus status) =>
        status is ExpenseRequestStatus.InApproval
            or ExpenseRequestStatus.Approved
            or ExpenseRequestStatus.AwaitingNsi
            or ExpenseRequestStatus.ToPay
            or ExpenseRequestStatus.PartiallyPaid
            or ExpenseRequestStatus.Paid;
}
