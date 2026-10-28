using System.Security.Cryptography;
using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Application.ExpenseRequests;

public sealed record AttachmentPreview(byte[] Content, string MediaType);
public interface IAttachmentPreviewRenderer
{
    AttachmentPreview Render(string fileName, byte[] original, CancellationToken token);
}
public interface IAttachmentPreviewCache
{
    Task<AttachmentPreview?> FindAsync(Guid attachmentId, string sourceHash, CancellationToken token);
    Task SaveAsync(Guid contextId, bool staged, Guid attachmentId, string sourceHash,
        string originalName, AttachmentPreview preview, string actor, CancellationToken token);
}
public sealed class AttachmentPreviewUnsupportedException : Exception;
public sealed class AttachmentPreviewInvalidException : Exception;

/// <summary>Authorization is never cached. Previewing does not save or mutate the RZ.
/// The bounded process gate limits simultaneous parsing and retained-file admission.</summary>
public sealed class AttachmentPreviewService(
    ExpenseRequestAttachmentService attachments,
    IExpenseRequestAttachmentUploadService uploads,
    IAttachmentPreviewRenderer renderer,
    IAttachmentPreviewCache cache,
    ICurrentActor actor)
{
    private static readonly SemaphoreSlim Gate = new(2, 2);

    public async Task<AttachmentPreview?> PreviewAsync(Guid contextId, Guid attachmentId,
        bool staged, CancellationToken token)
    {
        var actorId = ActorIdentity.RequireBusinessDataAccess(actor);
        if (!await Gate.WaitAsync(TimeSpan.FromSeconds(5), token))
            throw new ExpenseRequestAttachmentUnavailableException();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            async Task<ExpenseRequestAttachmentDownload?> Read() => staged
                ? await uploads.DownloadAsync(contextId, attachmentId, deadline.Token)
                : await attachments.DownloadAsync(contextId, attachmentId, deadline.Token);
            var original = await Read();
            if (original is null) return null;
            var hash = Convert.ToHexString(SHA256.HashData(original.Content));
            var preview = await cache.FindAsync(attachmentId, hash, deadline.Token);
            var generated = preview is null;
            preview ??= renderer.Render(original.FileName, original.Content, deadline.Token);
            // A withdrawal, session binding or revoked grant during conversion must
            // not be bypassed by a generated/cached result.
            var current = await Read();
            if (current is null) return null;
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(current.Content),
                SHA256.HashData(original.Content))) throw new ExpenseRequestAttachmentUnavailableException();
            deadline.Token.ThrowIfCancellationRequested();
            if (generated) await cache.SaveAsync(contextId, staged, attachmentId, hash,
                original.FileName, preview, actorId, deadline.Token);
            return preview;
        }
        finally { Gate.Release(); }
    }
}
