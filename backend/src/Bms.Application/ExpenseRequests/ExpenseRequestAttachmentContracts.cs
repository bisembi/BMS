using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

public sealed record ExpenseRequestAttachmentUploadIntentCommand(
    string? FileName,
    long FileSizeBytes);

public sealed record ExpenseRequestAttachmentUploadIntentDto(
    bool IsAccepted,
    string? RejectionCode,
    long MaximumFileSizeBytes)
{
    public static ExpenseRequestAttachmentUploadIntentDto FromPolicyResult(
        ExpenseRequestAttachmentValidationResult result) =>
        new(
            result.IsAccepted,
            result.Rejection?.ToString(),
            ExpenseRequestAttachmentUploadPolicy.MaximumFileSizeBytes);
}
