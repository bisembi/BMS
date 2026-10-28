using Bms.Application.ExpenseRequests;

namespace Bms.Application.Abstractions;

/// <summary>Actor-scoped pre-draft attachment operations; provider details stay private.</summary>
public interface IExpenseRequestAttachmentUploadService
{
    Task VerifyCreateAuthorityAsync(CancellationToken token);
    Task<ExpenseRequestAttachmentListDto> ListAsync(Guid sessionId, CancellationToken token);
    Task<ExpenseRequestAttachmentMutationDto> AddAsync(Guid sessionId,
        AddExpenseRequestAttachmentCommand command, string key, CancellationToken token);
    Task<ExpenseRequestAttachmentMutationDto> WithdrawAsync(Guid sessionId, Guid attachmentId, CancellationToken token);
    Task<ExpenseRequestAttachmentDownload?> DownloadAsync(Guid sessionId, Guid attachmentId, CancellationToken token);
    Task<ExpenseRequestAttachmentMutationDto?> ResolveAsync(Guid sessionId, string key,
        Guid? deletedId, CancellationToken token);
}
