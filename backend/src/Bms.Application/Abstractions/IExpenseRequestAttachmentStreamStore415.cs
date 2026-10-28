using Bms.Application.ExpenseRequests;

namespace Bms.Application.Abstractions;

/// <summary>Optional immutable-file reader. The caller owns the returned handle;
/// it must be readable, seekable, bounded and verified against the metadata.</summary>
public interface IExpenseRequestAttachmentStreamStore415
{
    Task<StoredAttachmentStream415?> OpenContent415Async(Guid requestId, Guid attachmentId, CancellationToken token);
}

public interface IExpenseRequestAttachmentUploadStream415
{
    Task<ExpenseRequestAttachmentStreamDownload415?> OpenDownload415Async(Guid sessionId, Guid attachmentId, CancellationToken token);
}

public sealed record StoredAttachmentStream415(StoredExpenseRequestAttachment Metadata, Stream Content);

public sealed record ExpenseRequestAttachmentStreamDownload415(string FileName, Stream Content, DateTimeOffset UploadedAtUtc)
    : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
