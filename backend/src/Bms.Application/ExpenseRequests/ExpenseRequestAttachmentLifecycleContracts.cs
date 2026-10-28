using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

public sealed record AddExpenseRequestAttachmentCommand(
    Guid ExpectedVersion,
    ExpenseRequestAttachmentKind Kind,
    string FileName,
    byte[] Content,
    string? Comment);

public sealed record DeleteExpenseRequestAttachmentCommand(Guid ExpectedVersion);

public sealed record ExpenseRequestAttachmentDto(
    Guid Id,
    string Kind,
    string FileName,
    long FileSizeBytes,
    DateTimeOffset UploadedAtUtc,
    string? Comment,
    bool CanDelete);

public sealed record ExpenseRequestAttachmentListDto(
    string RequestVersion,
    bool CanAdd,
    bool RequiresComment,
    IReadOnlyList<ExpenseRequestAttachmentDto> Items);

public sealed record ExpenseRequestAttachmentMutationDto(
    string RequestVersion,
    ExpenseRequestAttachmentDto? Attachment);

public sealed record ExpenseRequestAttachmentMutationResult(
    ExpenseRequestAttachmentMutationDto Response,
    bool IsReplay,
    int ResponseStatusCode);

public sealed record ExpenseRequestAttachmentDownload(
    string FileName,
    byte[] Content,
    DateTimeOffset UploadedAtUtc);

public sealed class ExpenseRequestAttachmentNotFoundException()
    : Exception("The expense-request attachment was not found.");

public sealed class ExpenseRequestAttachmentUnavailableException : Exception
{
    public ExpenseRequestAttachmentUnavailableException()
        : base("Expense-request attachment storage is temporarily unavailable.")
    {
    }

    public ExpenseRequestAttachmentUnavailableException(Exception innerException)
        : base("Expense-request attachment storage is temporarily unavailable.", innerException)
    {
    }
}

public sealed class ExpenseRequestAttachmentScanUnavailableException()
    : Exception("Expense-request attachment scanning is temporarily unavailable.");

public sealed class ExpenseRequestAttachmentContentRejectedException()
    : Exception("Expense-request attachment content was rejected.");
