using Bms.Api.Diagnostics;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Common;

namespace Bms.Api.Problems;

/// Shared fail-closed builders for API failures that need a stable public body.
///
/// Every string returned from here is a fixed public constant. Nothing derived
/// from an exception may pass through: no <c>Exception.Message</c>, no inner
/// exception, no SQL text or connection string, no server or database name, no
/// stack trace and no echoed user input. A caller that needs to explain a
/// failure adds a new constant here rather than forwarding the exception.
///
/// Status codes, <c>extensions["code"]</c> values and the presence or absence
/// of <c>detail</c> and <c>title</c> mirror what each endpoint returned before
/// W90B, so the wire contract is unchanged apart from the removed diagnostics.
public static class SafeProblemResults
{
    public static IResult ApprovalAllocationTotalMismatch() => Results.Problem(statusCode:400,title:"Проверьте распределение суммы",
        detail:"Сумма строк не равна сумме заявки. Сохранение не выполнено.",extensions:new Dictionary<string,object?>{{"code","allocation-total-mismatch"}});
    public static IResult ApprovalAllocationSourceMismatch() => Results.Problem(statusCode:400,title:"Проверьте распределение суммы",
        detail:"Сумма частей по каждой статье ДДС должна сохраняться. Статья ДДС и бюджет не меняются.",extensions:new Dictionary<string,object?>{{"code","allocation-source-mismatch"}});
    public static IResult InvalidApprovalDetails() => Results.Problem(statusCode: 400,
        title: "Проверьте реквизиты заявки",
        detail: "Заполните обязательные поля, проверьте длину текста и действующий договор. Для срочной заявки укажите причину срочности.");
    public static IResult ProtectedApprovalDetails() => Results.Problem(statusCode: 400,
        title: "Проверьте реквизиты заявки",
        detail: "Нельзя менять основание оплаты, вид договора или обязательные реквизиты. Для изменения статьи и суммы верните заявку на доработку.");
    public static IResult TreasuryReschedulingPaused()=>Results.Problem(statusCode:503,
        detail:"Перенос и передача в оплату временно приостановлены. Дата и заявка не изменены. Обратитесь к администратору.",
        extensions:new Dictionary<string,object?>{["code"]="treasury_rescheduling_paused"});
    public static IResult TreasuryBudgetCheckUnavailable()=>Results.Problem(statusCode:503,
        detail:"Не удалось проверить актуальный факт 1С и бюджет месяца оплаты. Заявка не передана в оплату, дата не изменена. Проверьте журнал обмена и повторите попытку после восстановления сверки.",
        extensions:new Dictionary<string,object?>{["code"]="treasury_budget_check_unavailable"});
    public static IResult TreasuryPaymentDateConfirmed() => Results.Problem(statusCode: 409,
        title: "Payment date is confirmed", detail: "Дата оплаты подтверждена в 1С и не может быть изменена.",
        extensions: new Dictionary<string, object?> { ["code"] = "treasury_payment_date_confirmed" });
    internal const string AuthenticationRequiredDetail = "Authentication is required.";
    internal const string ForbiddenDetail = "Access to this resource is forbidden.";
    internal const string CsrfTokenInvalidDetail =
        "A valid antiforgery token is required for this request.";
    internal const string CsrfTokenInvalidCode = "csrf-token-invalid";
    internal const string ExpenseRequestNotFoundDetail =
        "The expense request was not found or is not accessible.";
    internal const string InvalidRequestDetail = "The request is invalid.";
    internal const string InvalidIdempotencyKeyDetail =
        "Exactly one valid Idempotency-Key header is required.";
    internal const string DomainRuleViolationDetail =
        "The request violates a business rule of the expense request.";
    internal const string ConflictDetail =
        "The operation conflicts with the current state of the expense request.";
    internal const string SubmissionPreflightUnavailableDetail =
        "Submission preflight is temporarily unavailable.";
    internal const string ExpenseRequestWorkflowUnavailableDetail =
        "The expense-request workflow is temporarily unavailable.";
    internal const string PaymentSummaryUnavailableDetail =
        "The expense-request payment summary is temporarily unavailable.";
    internal const string PaymentDetailsUnavailableDetail =
        "The expense-request payment details are temporarily unavailable.";
    internal const string BudgetExecutionUnavailableDetail =
        "The budget execution list is temporarily unavailable.";
    internal const string BudgetExecutionExportTooLargeDetail =
        "The budget execution export is too large. Narrow the filters and try again.";
    internal const string BudgetReservationUnavailableDetail =
        "The composition of this budget reserve is temporarily unavailable.";
    internal const string PaymentCalendarUnavailableDetail =
        "The payment calendar is temporarily unavailable.";
    internal const string PaymentCalendarExportTooLargeDetail =
        "The payment calendar export is too large. Narrow the filters and try again.";
    internal const string PaymentCalendarExportPrecisionDetail =
        "The payment calendar amounts cannot be represented precisely in Excel. Narrow the filters and try again.";
    internal const string DatedPaymentCalendarScopeTooLargeDetail =
        "The dated payment calendar period is too large. Request a narrower period.";
    internal const string ExpenseRequestCreationAuthorityUnavailableDetail =
        "Expense-request creation authority is temporarily unavailable.";
    internal const string ExpenseRequestSubstitutionAuthorityUnavailableDetail =
        "Expense-request substitution authority is temporarily unavailable.";
    internal const string ExpenseRequestClosingUnavailableDetail =
        "Expense-request closing is temporarily unavailable.";
    internal const string ActiveAccrualReportUnavailableDetail =
        "The active-accrual report is temporarily unavailable.";
    internal const string ActiveAccrualExportTooLargeDetail =
        "The active-accrual export is too large. Narrow the filters and try again.";
    internal const string ApprovalMatrixUnavailableDetail =
        "The approval matrix is temporarily unavailable.";
    internal const string AccrualNotificationsUnavailableDetail =
        "Accrual notifications are temporarily unavailable.";
    internal const string ExpenseRequestAttachmentsUnavailableDetail =
        "Expense-request attachments are temporarily unavailable.";
    internal const string ExpenseRequestAttachmentRejectedDetail =
        "The attachment was rejected by the file safety policy.";
    internal const string ExpenseRequestAttachmentTooLargeDetail =
        "The attachment request is too large.";
    internal const string BusinessAuditUnavailableDetail =
        "The business audit is temporarily unavailable.";

    internal static IResult InternalServerError() => Results.Problem(
        statusCode: StatusCodes.Status500InternalServerError);

    public static IResult AuthenticationRequired() => Results.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        detail: AuthenticationRequiredDetail);

    public static IResult Forbidden() => Results.Problem(
        statusCode: StatusCodes.Status403Forbidden,
        detail: ForbiddenDetail);

    /// The one refusal the antiforgery filter returns. Missing, malformed and
    /// wrong-identity tokens are answered identically on purpose: the
    /// framework's own message distinguishes them, and that distinction is
    /// worth more to an attacker than to a client.
    public static IResult CsrfTokenInvalid() => Results.Problem(
        statusCode: StatusCodes.Status403Forbidden,
        detail: CsrfTokenInvalidDetail,
        extensions: new Dictionary<string, object?> { ["code"] = CsrfTokenInvalidCode });

    public static IResult AccrualNotificationsUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: AccrualNotificationsUnavailableDetail);

    public static IResult BusinessAuditUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: BusinessAuditUnavailableDetail);

    public static IResult ExpenseRequestNotFound() => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        detail: ExpenseRequestNotFoundDetail);

    public static IResult InvalidRequest() => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        detail: InvalidRequestDetail);

    public static IResult ActiveBmsProfileInvalid(string traceId) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "The requested BMS access profile is invalid.",
        extensions: new Dictionary<string, object?>
        {
            ["code"] = "ActiveBmsProfileInvalid",
            ["traceId"] = traceId
        });

    public static IResult ActiveBmsProfileNotAvailable(string traceId) => Results.Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "The requested BMS access profile is not available.",
        extensions: new Dictionary<string, object?>
        {
            ["code"] = "ActiveBmsProfileNotAvailable",
            ["traceId"] = traceId
        });

    public static IResult ActiveBmsProfileNotAssigned(string traceId) => Results.Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "The requested BMS access profile is not assigned.",
        extensions: new Dictionary<string, object?>
        {
            ["code"] = "ActiveBmsProfileNotAssigned",
            ["traceId"] = traceId
        });

    public static IResult ActiveBmsRoleInvalid(string traceId) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "The requested BMS role is invalid.",
        extensions: new Dictionary<string, object?>
        {
            ["code"] = "ActiveBmsRoleInvalid",
            ["traceId"] = traceId
        });

    public static IResult ActiveBmsRoleNotAssigned(string traceId) => Results.Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "The requested BMS role is not assigned.",
        extensions: new Dictionary<string, object?>
        {
            ["code"] = "ActiveBmsRoleNotAssigned",
            ["traceId"] = traceId
        });

    public static IResult ActiveBmsRoleProfileConflict(string traceId) => Results.Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "The requested BMS role is not available in the selected profile.",
        extensions: new Dictionary<string, object?>
        {
            ["code"] = "ActiveBmsRoleProfileConflict",
            ["traceId"] = traceId
        });

    public static IResult ActiveBmsRoleUnavailable(string traceId) => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "BMS role selection is temporarily unavailable.",
        extensions: new Dictionary<string, object?>
        {
            ["code"] = "ActiveBmsRoleUnavailable",
            ["traceId"] = traceId
        });

    public static IResult CashFlowAccrualSettingsNotFound() => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        detail: "The cash-flow article was not found or is not accessible.");

    public static IResult BudgetInstanceNotFound() => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        detail: "The budget instance was not found or is not accessible.");

    public static IResult BudgetDirectoryNotFound() => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        detail: "The budget cost center was not found or is not accessible.");

    public static IResult BudgetDirectoryUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: "Budget-directory authority is temporarily unavailable.");

    public static IResult AuthorizationDirectoryNotFound() => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        detail: "The authorization-directory employee was not found or is not accessible.");

    public static IResult AuthorizationDirectoryUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: "The authorization directory is temporarily unavailable.");

    public static IResult AuthorizationDirectoryActorConflict() => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        detail: "The Windows actor is already assigned to another employee.",
        extensions: new Dictionary<string, object?> { ["code"] = "authorization-actor-conflict" });

    public static IResult ExpenseItemCodeConflict() => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        detail: "An expense item with this code already exists.",
        extensions: new Dictionary<string, object?> { ["code"] = "expense-item-code-conflict" });

    public static IResult OrganizationNotFound() => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        detail: "The organization was not found or is not accessible.");

    public static IResult OrganizationCodeConflict() => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        detail: "An organization with this code already exists.",
        extensions: new Dictionary<string, object?> { ["code"] = "organization-code-conflict" });

    public static IResult OrganizationDirectoryUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: "The organization directory is temporarily unavailable.");

    public static IResult ReferenceDataNotFound() => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        detail: "The reference-data item was not found or is not accessible.");

    public static IResult ReferenceDataConflict() => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        detail: "The reference-data item conflicts with existing data.");

    public static IResult ReferenceDataUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: "The reference-data directory is temporarily unavailable.");

    public static IResult BudgetOwnerAssignmentNotFound() => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        detail: "The budget-owner and cost-center assignment was not found or is not accessible.");

    public static IResult BudgetOwnerAssignmentConflict() => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        detail: "The budget-owner and cost-center assignment already exists.");

    public static IResult BudgetPrefixConflict() => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        detail: "The budget abbreviation is already assigned to another cost center.",
        extensions: new Dictionary<string, object?> { ["code"] = "budget-prefix-conflict" });

    public static IResult CashFlowAccrualSettingsUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: "The cash-flow accrual settings are temporarily unavailable.");

    public static IResult PaymentDetailsUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: PaymentDetailsUnavailableDetail);

    public static IResult ListBusinessColumnsUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: "Expense-request list business columns are temporarily unavailable.");

    public static IResult ManualDeficitNotFound() => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        detail: "The overpayment deficit or correction was not found or is not accessible.");

    public static IResult ManualDeficitRuleViolation() => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        detail: "The correction violates an overpayment deficit business rule.");

    public static IResult ManualDeficitUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: "The overpayment deficit correction service is temporarily unavailable.");

    public static IResult ManualDeficitStateConflict() => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        detail: "The correction or deficit state has changed. Refresh before trying again.",
        extensions: new Dictionary<string, object?> { ["code"] = "state-conflict" });

    public static IResult ManualDeficitIdempotencyConflict() => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        detail: "The idempotency key belongs to a different correction command.",
        extensions: new Dictionary<string, object?> { ["code"] = "idempotency-conflict" });

    public static IResult InvalidIdempotencyKey() => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        detail: InvalidIdempotencyKeyDetail);

    public static IResult DomainRuleViolation() => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        detail: DomainRuleViolationDetail);

    public static IResult ExpenseAttachmentValidation(DomainRuleViolationReason? reason) =>
        reason == DomainRuleViolationReason.ExpenseAttachmentNewContractRequiresSave
            ? Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                detail: DomainRuleViolationDetail,
                extensions: new Dictionary<string, object?> { ["code"] = "expense-attachment-new-contract-requires-save" })
            : DomainRuleViolation();

    /// Only reviewed enum values can select public text/code. The optional row
    /// points into the submitted array; no article, employee or CFO data are
    /// read or echoed, and no internal exception text is serialized.
    public static IResult ExpenseDraftValidation(DomainRuleViolationReason? reason)
    {
        var code = reason switch
        {
            DomainRuleViolationReason.ExpenseDraftContractMismatch => "expense-draft-contract-mismatch",
            DomainRuleViolationReason.ExpenseDraftArticleCfoMismatch => "expense-draft-article-cfo-mismatch",
            DomainRuleViolationReason.ExpenseDraftLineCfoMismatch => "expense-draft-line-cfo-mismatch",
            DomainRuleViolationReason.ExpenseDraftNumberReservationExpired => "expense-draft-number-reservation-expired",
            DomainRuleViolationReason.ExpenseDraftPurposeManaged => "expense-draft-purpose-managed",
            DomainRuleViolationReason.ExpenseDraftUrgencyInvalid => "expense-draft-urgency-invalid",
            DomainRuleViolationReason.ExpenseDraftCommentInvalid => "expense-draft-comment-invalid",
            DomainRuleViolationReason.ExpenseDraftJustificationInvalid => "expense-draft-justification-invalid",
            DomainRuleViolationReason.ExpenseDraftTextInvalid => "expense-draft-text-invalid",
            DomainRuleViolationReason.ExpenseDraftCurrencyInvalid => "expense-draft-currency-invalid",
            DomainRuleViolationReason.ExpenseDraftUploadUnavailable => "expense-draft-upload-unavailable",
            _ => null,
        };
        return code is null ? DomainRuleViolation() : Results.Problem(
            statusCode: StatusCodes.Status400BadRequest, detail: DomainRuleViolationDetail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }

    public static IResult BudgetDraftValidation(DomainRuleViolationReason reason, int? submittedRowIndex = null)
    {
        var (code, detail) = reason switch
        {
            DomainRuleViolationReason.BudgetArticleUnavailable =>
                ("budget-article-unavailable", "Статья строки бюджета не найдена или больше недоступна. Обновите справочник и проверьте строку."),
            DomainRuleViolationReason.BudgetArticleCfoMismatch =>
                ("budget-article-cfo-mismatch", "Статья строки недоступна для ЦФО бюджета. Проверьте активность и назначение статьи."),
            DomainRuleViolationReason.BudgetInApproval =>
                ("budget-in-approval", "Бюджет находится на согласовании. Верните его в черновик перед изменением."),
            DomainRuleViolationReason.BudgetPeriodInvalid =>
                ("budget-period-invalid", "Период бюджета должен начинаться первым и завершаться последним днём месяца; начало не может быть позже окончания."),
            DomainRuleViolationReason.BudgetLinesInvalid =>
                ("budget-lines-invalid", "Проверьте статьи, месяцы, суммы и комментарии строк бюджета. Нужна хотя бы одна корректная строка."),
            DomainRuleViolationReason.BudgetNumberingUninitialized =>
                ("budget-numbering-uninitialized", "Для ЦФО бюджета не подготовлена нумерация. Настройте сокращение и нумерацию ЦФО в справочнике."),
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        if ((reason is DomainRuleViolationReason.BudgetArticleUnavailable
            or DomainRuleViolationReason.BudgetArticleCfoMismatch or DomainRuleViolationReason.BudgetLinesInvalid)
            && submittedRowIndex is >= 0 and <= DomainRuleViolationException.MaximumSubmittedRowIndex)
            extensions["rowIndices"] = new[] { submittedRowIndex.Value };
        return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: detail, extensions: extensions);
    }

    /// Reviewed conflict factories publish fixed codes, never exception text.
    public static IResult BudgetBaselineChangedConflict() => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        detail: "The approved budget baseline changed. Return and resubmit the budget for a new assessment.",
        extensions: new Dictionary<string, object?> { ["code"] = "budget-baseline-changed" });

    public static IResult BudgetLimitChangedConflict() => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        detail: "The budget limit changed. Return and resubmit the expense request for a new assessment.",
        extensions: new Dictionary<string, object?> { ["code"] = "budget-limit-changed" });

    public static IResult EmployeeCodeConflict() => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        detail: "An employee with this code already exists.",
        extensions: new Dictionary<string, object?> { ["code"] = "employee-code-conflict" });

    public static IResult CurrencyRateConflict() => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        detail: "The currency rate changed. Refresh the rates before trying again.",
        extensions: new Dictionary<string, object?> { ["code"] = "currency-rate-conflict" });

    public static IResult Conflict(string code)
    {
        if (!string.Equals(
                code,
                ExpenseRequestProblemCodes.IdempotencyConflict,
                StringComparison.Ordinal) &&
            !string.Equals(
                code,
                ExpenseRequestProblemCodes.VersionConflict,
                StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(code),
                "Only reviewed expense-request conflict codes may be published.");
        }

        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            detail: ConflictDetail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }

    public static IResult ExecutionFactChangedConflict() => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        detail: "Факт ДДС обновился. Обновите исполнение бюджета.",
        extensions: new Dictionary<string, object?> { ["code"] = "execution-fact-version-conflict" });

    /// A 503 that keeps both its code and the trace identifier the endpoint
    /// already published, so an operator can still correlate the request.
    public static IResult SubmissionPreflightUnavailable(string traceId)
    {
        var canonicalTraceId = ApiTraceIdentifier.RequireCanonical(traceId, nameof(traceId));

        return Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            detail: SubmissionPreflightUnavailableDetail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = SubmissionPreflightProblemCodes.Unavailable,
                ["traceId"] = canonicalTraceId,
            });
    }

    public static IResult ExpenseRequestWorkflowUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: ExpenseRequestWorkflowUnavailableDetail);

    public static IResult ExpenseRequestCreationAuthorityUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: ExpenseRequestCreationAuthorityUnavailableDetail);

    public static IResult ExpenseRequestSubstitutionAuthorityUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: ExpenseRequestSubstitutionAuthorityUnavailableDetail);

    public static IResult BudgetExecutionUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: BudgetExecutionUnavailableDetail);

    public static IResult BudgetExecutionExportTooLarge() => Results.Problem(
        statusCode: StatusCodes.Status422UnprocessableEntity,
        detail: BudgetExecutionExportTooLargeDetail);

    public static IResult BudgetReservationUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: BudgetReservationUnavailableDetail);

    public static IResult PaymentCalendarUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: PaymentCalendarUnavailableDetail);

    public static IResult PaymentCalendarExportTooLarge() => Results.Problem(
        statusCode: StatusCodes.Status422UnprocessableEntity,
        detail: PaymentCalendarExportTooLargeDetail);

    public static IResult PaymentCalendarExportPrecision() => Results.Problem(
        statusCode: StatusCodes.Status422UnprocessableEntity,
        detail: PaymentCalendarExportPrecisionDetail);

    public static IResult ExcelExportPrecision() => Results.Problem(
        statusCode: StatusCodes.Status422UnprocessableEntity,
        detail: "Суммы этой выборки превышают точность Excel. Сузьте фильтры; исходные суммы в BMS не изменены.",
        extensions: new Dictionary<string, object?> { ["code"] = "excel-export-precision" });

    public static IResult DatedPaymentCalendarScopeTooLarge() => Results.Problem(
        statusCode: StatusCodes.Status422UnprocessableEntity,
        detail: DatedPaymentCalendarScopeTooLargeDetail);

    public static IResult PaymentSummaryUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: PaymentSummaryUnavailableDetail);

    public static IResult ExpenseRequestClosingUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: ExpenseRequestClosingUnavailableDetail);

    public static IResult ActiveAccrualReportUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: ActiveAccrualReportUnavailableDetail);

    public static IResult ActiveAccrualExportTooLarge() => Results.Problem(
        statusCode: StatusCodes.Status422UnprocessableEntity,
        detail: ActiveAccrualExportTooLargeDetail);

    public static IResult ApprovalMatrixUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: ApprovalMatrixUnavailableDetail);

    public static IResult ExpenseRequestAttachmentsUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        detail: ExpenseRequestAttachmentsUnavailableDetail);

    public static IResult ExpenseRequestAttachmentRejected() => Results.Problem(
        statusCode: StatusCodes.Status422UnprocessableEntity,
        detail: ExpenseRequestAttachmentRejectedDetail);

    public static IResult ExpenseRequestAttachmentTooLarge() => Results.Problem(
        statusCode: StatusCodes.Status413PayloadTooLarge,
        detail: ExpenseRequestAttachmentTooLargeDetail);
    public static IResult AttachmentPreviewUnsupported() => Results.Problem(
        statusCode: StatusCodes.Status415UnsupportedMediaType,
        detail: "Preview is not supported for this file format or size.");
}
