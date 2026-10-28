namespace Bms.Domain.Workflow;

/// <summary>
/// Canonical technical identifiers proposed for the expense-request workflow.
/// This catalogue has no persistence, endpoint, permission or transition
/// effect by itself. A separately approved migration must seed the registry
/// and move the current draft-only aggregate onto these identifiers.
/// </summary>
public static class ExpenseRequestWorkflowCodes
{
    public const string DraftStatusCode = "DRAFT";
    public const string ReturnedStatusCode = "RETURNED";
    public const string InApprovalStatusCode = "IN_APPROVAL";
    public const string ApprovedStatusCode = "APPROVED";
    public const string AwaitingNsiStatusCode = "AWAITING_NSI";
    public const string ToPayStatusCode = "TO_PAY";
    public const string PartiallyPaidStatusCode = "PARTIALLY_PAID";
    public const string PaidStatusCode = "PAID";
    public const string ClosedStatusCode = "CLOSED";
    public const string RejectedStatusCode = "REJECTED";
    public const string CancelledStatusCode = "CANCELLED";

    public const string BudgetOwnerRoleCode = "BUDGET_OWNER";
    public const string CfoInitiatorRoleCode = "CFO_INITIATOR";
    public const string FinancialControllerRoleCode = "FIN_CONTROLLER";
    public const string FinancialDirectorRoleCode = "FIN_DIRECTOR";
    public const string GeneralDirectorRoleCode = "GENERAL_DIRECTOR";
    public const string TreasurerRoleCode = "TREASURER";
    public const string AccountantRoleCode = "ACCOUNTANT";
    public const string ResponsibleAccountantRoleCode = "RESPONSIBLE_ACCOUNTANT";
    public const string NsiSpecialistRoleCode = "NSI_SPECIALIST";
    public const string AuditorRoleCode = "AUDITOR";
    public const string PayrollCalculatorRoleCode = PayrollAdvanceWorkflow372.CalculatorRole;

    public const string SubmitCommandCode = "SUBMIT";
    public const string ApproveCommandCode = "APPROVE";
    public const string ReturnCommandCode = "RETURN";
    public const string RejectCommandCode = "REJECT";
    public const string CancelCommandCode = "CANCEL";
    public const string ConfirmNsiCommandCode = "CONFIRM_NSI";
    public const string SendToPaymentCommandCode = "SEND_TO_PAYMENT";
    public const string ClosePartialCommandCode = "CLOSE_PARTIAL";
    public const string CloseFullCommandCode = "CLOSE_FULL";
    public const string ReopenCommandCode = "REOPEN";
    public const string AddAttachmentCommandCode = "ADD_ATTACHMENT";

    private static readonly IReadOnlyList<string> statusCodes = Array.AsReadOnly(
    [
        DraftStatusCode,
        ReturnedStatusCode,
        InApprovalStatusCode,
        ApprovedStatusCode,
        AwaitingNsiStatusCode,
        ToPayStatusCode,
        PartiallyPaidStatusCode,
        PaidStatusCode,
        ClosedStatusCode,
        RejectedStatusCode,
        CancelledStatusCode
    ]);

    private static readonly IReadOnlyList<string> functionalRoleCodes = Array.AsReadOnly(
    [
        BudgetOwnerRoleCode,
        CfoInitiatorRoleCode,
        FinancialControllerRoleCode,
        FinancialDirectorRoleCode,
        GeneralDirectorRoleCode,
        TreasurerRoleCode,
        AccountantRoleCode,
        ResponsibleAccountantRoleCode,
        NsiSpecialistRoleCode,
        AuditorRoleCode,
        PayrollCalculatorRoleCode
    ]);

    private static readonly IReadOnlyList<string> commandCodes = Array.AsReadOnly(
    [
        SubmitCommandCode,
        ApproveCommandCode,
        ReturnCommandCode,
        RejectCommandCode,
        CancelCommandCode,
        ConfirmNsiCommandCode,
        SendToPaymentCommandCode,
        ClosePartialCommandCode,
        CloseFullCommandCode,
        ReopenCommandCode,
        AddAttachmentCommandCode
    ]);

    public static IReadOnlyList<string> StatusCodes => statusCodes;

    public static IReadOnlyList<string> FunctionalRoleCodes => functionalRoleCodes;

    public static IReadOnlyList<string> CommandCodes => commandCodes;
}
