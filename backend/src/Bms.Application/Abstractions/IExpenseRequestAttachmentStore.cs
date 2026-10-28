using Bms.Application.ExpenseRequests;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.Abstractions;

/// <summary>
/// Provider-neutral immutable attachment storage. Mutation callers own a
/// Serializable business transaction and the parent request lock.
/// </summary>
public interface IExpenseRequestAttachmentReader
{
    Task<SubmissionAttachmentFacts> ReadSubmissionFactsAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken);
}

public interface IExpenseRequestAttachmentStore : IExpenseRequestAttachmentReader
{
    Task<IReadOnlyList<StoredExpenseRequestAttachment>> ListAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken);

    Task<StoredExpenseRequestAttachment?> FindContentAsync(
        Guid expenseRequestId,
        Guid attachmentId,
        CancellationToken cancellationToken);

    Task SealForSubmissionAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken);

    void Add(StoredExpenseRequestAttachment attachment);

    Task<StoredExpenseRequestAttachment?> RemoveAsync(
        Guid expenseRequestId,
        Guid attachmentId,
        CancellationToken cancellationToken);
}

public sealed record StoredExpenseRequestAttachment(
    Guid Id,
    Guid ExpenseRequestId,
    ExpenseRequestAttachmentKind Kind,
    string FileName,
    long FileSizeBytes,
    byte[] ContentSha256,
    byte[]? Content,
    DateTimeOffset UploadedAtUtc,
    string UploadedByExternalId,
    string? Comment,
    bool IsPostSubmission,
    bool IsSealed);
