using Bms.Api.Security;
using Bms.Application.ExpenseRequests;

namespace Bms.Api.Endpoints;

/// Route registration for the expense request resource.
///
/// The handlers live in the sibling partial files; this one is the single
/// place where a URL, an HTTP method or a route name is declared, so the
/// route manifest can be read in one screen and pinned by a test.
public static partial class ExpenseRequestEndpoints
{

    public static IEndpointRouteBuilder MapExpenseRequestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/expense-requests").WithTags("Expense requests");
        group.MapGet("/initiators", ListInitiatorsAsync).WithName("ListExpenseRequestInitiators");
        group.MapGet("/debtors/by-initiator", Debtors470Async).WithName("GetDebtorsByInitiator470");
        group.MapGet("/payment-register", GetPaymentRegisterAsync).WithName("GetExpensePaymentRegister");
        group.MapGet("/{id:guid}/approval-route", GetApprovalRouteAsync).WithName("GetExpenseRequestApprovalRoute");
        // The host's API actor boundary and the live creation-authority checks
        // apply, exactly as for the existing expense-request routes.
        var uploads = group.MapGroup("/upload-sessions/{sessionId:guid}");
        uploads.MapGet("/attachments", ListStagedAttachmentsAsync).WithName("ListStagedExpenseRequestAttachments");
        uploads.MapPost("/attachments", AddStagedAttachmentAsync)
            // Admission must precede antiforgery's possible form-token read.
            .AddEndpointFilter<AttachmentUploadAdmissionFilter412>().RequireCsrfToken()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(27L * 1024L * 1024L))
            .WithName("AddStagedExpenseRequestAttachment");
        uploads.MapDelete("/attachments/{attachmentId:guid}", WithdrawStagedAttachmentAsync).RequireCsrfToken()
            .WithName("WithdrawStagedExpenseRequestAttachment");
        uploads.MapGet("/attachments/{attachmentId:guid}/content", DownloadStagedAttachmentAsync)
            .WithName("DownloadStagedExpenseRequestAttachment");
        uploads.MapPost("/attachments/{attachmentId:guid}/preview", PreviewStagedAttachmentAsync).RequireCsrfToken()
            .WithName("PreviewStagedExpenseRequestAttachment");
        uploads.MapGet("/attachments/idempotency/add", ResolveStagedAddAsync)
            .WithName("ResolveAddStagedExpenseRequestAttachmentIdempotency");
        uploads.MapGet("/attachments/{attachmentId:guid}/idempotency/delete", ResolveStagedDeleteAsync)
            .WithName("ResolveDeleteStagedExpenseRequestAttachmentIdempotency");
        group.MapPost("/", CreateDraftAsync).RequireCsrfToken()
            .WithName("CreateExpenseRequestDraft");
        group.MapPost("/number-reservation", ReserveNumberAsync).RequireCsrfToken()
            .WithName("ReserveExpenseRequestNumber");
        group.MapPost("/number-reservation/release", ReleaseNumberAsync).RequireCsrfToken()
            .WithName("ReleaseExpenseRequestNumber");
        group.MapPost("/attachments/upload-intent", ValidateAttachmentUploadIntent)
            .RequireCsrfToken()
            .WithName("ValidateExpenseRequestAttachmentUploadIntent");
        group.MapGet("/{id:guid}/attachments", ListAttachmentsAsync)
            .WithName("ListExpenseRequestAttachments");
        group.MapPost("/{id:guid}/attachments", AddAttachmentAsync)
            .AddEndpointFilter<AttachmentUploadAdmissionFilter412>()
            .RequireCsrfToken()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(
                27L * 1024L * 1024L))
            .WithName("AddExpenseRequestAttachment");
        group.MapGet("/{id:guid}/attachments/idempotency/add", ResolveAddAttachmentIdempotencyAsync)
            .WithName("ResolveAddExpenseRequestAttachmentIdempotency");
        group.MapGet("/{id:guid}/attachments/{attachmentId:guid}/content", DownloadAttachmentAsync)
            .WithName("DownloadExpenseRequestAttachment");
        group.MapPost("/{id:guid}/attachments/{attachmentId:guid}/preview", PreviewAttachmentAsync).RequireCsrfToken()
            .WithMetadata(new AuditorNonBusinessPost())
            .WithName("PreviewExpenseRequestAttachment");
        group.MapDelete("/{id:guid}/attachments/{attachmentId:guid}", DeleteAttachmentAsync)
            .RequireCsrfToken()
            .WithName("DeleteExpenseRequestAttachment");
        group.MapGet("/{id:guid}/attachments/{attachmentId:guid}/idempotency/delete", ResolveDeleteAttachmentIdempotencyAsync)
            .WithName("ResolveDeleteExpenseRequestAttachmentIdempotency");
        group.MapPut("/{id:guid}", UpdateDraftAsync).RequireCsrfToken()
            .WithName("UpdateExpenseRequestDraft");
        group.MapGet("/{id:guid}/approval-details", GetApprovalDetailsAsync).WithName("GetExpenseRequestApprovalDetails");
        group.MapPut("/{id:guid}/approval-details", UpdateApprovalDetailsAsync).RequireCsrfToken().WithName("UpdateExpenseRequestApprovalDetails");
        group.MapGet("/{id:guid}/approval-details/idempotency", ResolveApprovalDetailsAsync).WithName("ResolveExpenseRequestApprovalDetails");
        group.MapPost("/{id:guid}/refresh-currency", RefreshCurrencyAsync).RequireCsrfToken()
            .WithName("RefreshExpenseRequestCurrency");
        group.MapPost("/{sourceId:guid}/copy", CopyDraftAsync).RequireCsrfToken()
            .WithName("CopyExpenseRequestDraft");
        group.MapPost("/{sourceId:guid}/copy-complete", (Guid sourceId, ExpenseRequestService service,
            HttpRequest request, CancellationToken token) => CopyDraftAsync(sourceId, service, request, token, true))
            .RequireCsrfToken().WithName("CopyCompleteExpenseRequestDraft");
        group.MapGet("/{sourceId:guid}/idempotency/copy-complete", (Guid sourceId, ExpenseRequestService service,
            HttpRequest request, HttpResponse response, CancellationToken token) =>
            ResolveCopyDraftIdempotencyAsync(sourceId, service, request, response, token, true))
            .WithName("ResolveCopyCompleteExpenseRequestDraft");
        group.MapDelete("/{id:guid}", DeleteDraftAsync).RequireCsrfToken().WithName("DeleteExpenseRequestDraft");
        group.MapGet("/{id:guid}/idempotency/delete-draft", ResolveDeleteDraftAsync).WithName("ResolveDeleteExpenseRequestDraft");
        group.MapPost("/{id:guid}/submit", SubmitAsync).RequireCsrfToken()
            .WithName("SubmitExpenseRequest");
        group.MapGet("/{id:guid}/commands", GetWorkflowCommandAvailabilityAsync)
            .WithName("GetExpenseRequestWorkflowCommandAvailability");
        group.MapPost("/{id:guid}/commands/{commandCode}", ExecuteWorkflowCommandAsync)
            .RequireCsrfToken()
            .WithName("ExecuteExpenseRequestWorkflowCommand");
        group.MapGet("/{id:guid}/closing", GetClosingStateAsync)
            .WithName("GetExpenseRequestClosingState");
        group.MapPost("/{id:guid}/closing/partial", ClosePartiallyAsync)
            .RequireCsrfToken()
            .WithName("CloseExpenseRequestPartially");
        group.MapPost("/{id:guid}/closing/full", CloseFullyAsync)
            .RequireCsrfToken()
            .WithName("CloseExpenseRequestFully");
        group.MapPost("/{id:guid}/closing/reopen", ReopenAsync)
            .RequireCsrfToken()
            .WithName("ReopenExpenseRequest");
        group.MapGet("/idempotency/create-draft", ResolveCreateDraftIdempotencyAsync)
            .WithName("ResolveCreateExpenseRequestDraftIdempotency");
        group.MapGet("/{id:guid}/idempotency/update-draft", ResolveUpdateDraftIdempotencyAsync)
            .WithName("ResolveUpdateExpenseRequestDraftIdempotency");
        group.MapGet("/{sourceId:guid}/idempotency/copy-draft", ResolveCopyDraftIdempotencyAsync)
            .WithName("ResolveCopyExpenseRequestDraftIdempotency");
        group.MapGet("/{id:guid}/idempotency/submit", ResolveSubmitIdempotencyAsync)
            .WithName("ResolveSubmitExpenseRequestIdempotency");
        group.MapGet("/{id:guid}/commands/{commandCode}/idempotency", ResolveWorkflowCommandIdempotencyAsync)
            .WithName("ResolveExpenseRequestWorkflowCommandIdempotency");
        group.MapGet("/{id:guid}/closing/{commandCode}/idempotency", ResolveClosingIdempotencyAsync)
            .WithName("ResolveExpenseRequestClosingIdempotency");
        group.MapGet("/{id:guid}/submission-preflight", GetSubmissionPreflightAsync)
            .WithName("GetExpenseRequestSubmissionPreflight");
        group.MapGet("/export.xlsx", ExportExcelAsync).WithName("ExportExpenseRequestsExcel");
        group.MapGet("/{id:guid}/payment-summary", GetPaymentSummaryAsync)
            .WithName("GetExpenseRequestPaymentSummary");
        group.MapGet("/{id:guid}/payment-details", GetPaymentDetailsAsync)
            .WithName("GetExpenseRequestPaymentDetails");
        group.MapGet("/{id:guid}", GetAsync).WithName("GetExpenseRequest");
        group.MapGet("/", ListAsync).WithName("ListExpenseRequests");
        return endpoints;
    }
}
