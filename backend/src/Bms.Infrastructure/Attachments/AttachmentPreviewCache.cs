using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.ExpenseRequests;
using Bms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Attachments;

/// <summary>Immutable preview files share the private attachment store and quota.
/// An append-only BusinessAudit manifest links source, context and stored preview;
/// there are no public file paths and no changes to document/attachment facts.</summary>
public sealed class AttachmentPreviewCache(BmsDbContext db, AttachmentFileStorage files) : IAttachmentPreviewCache
{
    private const string ObjectType = "ExpenseRequestAttachmentPreview";
    private const string Renderer = "SafePreviewV1";
    private sealed record Manifest(Guid ContextId, bool Staged, string SourceSha256, string OriginalName,
        string RendererVersion, string Root, string Key, long Size, string Sha256, string MediaType);

    public async Task<AttachmentPreview?> FindAsync(Guid attachmentId, string sourceHash, CancellationToken token)
    {
        var id = attachmentId.ToString("D");
        var json = await db.BusinessAuditEntries.AsNoTracking()
            .Where(e => e.ObjectType == ObjectType && e.ObjectId == id && e.Action == Renderer)
            .OrderByDescending(e => e.OccurredAtUtc).Select(e => e.AfterJson).FirstOrDefaultAsync(token);
        if (json is null) return null;
        try
        {
            var m = JsonSerializer.Deserialize<Manifest>(json) ?? throw new JsonException();
            if (m.SourceSha256 != sourceHash || m.RendererVersion != Renderer) return null;
            if (!AttachmentPreviewRenderer.IsMediaTypeAllowed(m.MediaType)) throw new JsonException();
            return new AttachmentPreview(files.Read(new AttachmentFileLocation(m.Root, m.Key,
                m.Size, Convert.FromHexString(m.Sha256))), m.MediaType);
        }
        catch (Exception e) when (e is JsonException or FormatException or ArgumentException)
        { throw new ExpenseRequestAttachmentUnavailableException(e); }
    }

    public async Task SaveAsync(Guid contextId, bool staged, Guid attachmentId, string sourceHash,
        string originalName, AttachmentPreview preview, string actor, CancellationToken token)
    {
        if (!AttachmentPreviewRenderer.IsMediaTypeAllowed(preview.MediaType) || db.ChangeTracker.HasChanges())
            throw new ExpenseRequestAttachmentUnavailableException();
        var identity = SHA256.HashData(Encoding.UTF8.GetBytes($"{Renderer}:{attachmentId:N}:{sourceHash}"));
        var key = files.Write(new Guid(identity.AsSpan(0, 16)), preview.Content);
        var m = new Manifest(contextId, staged, sourceHash, originalName, Renderer, files.Root, key,
            preview.Content.LongLength, Convert.ToHexString(SHA256.HashData(preview.Content)), preview.MediaType);
        var entry = new BusinessAuditEntry
        {
            Id = Guid.NewGuid(), OccurredAtUtc = DateTimeOffset.UtcNow, ActorExternalId = actor,
            ObjectType = ObjectType, ObjectId = attachmentId.ToString("D"), Action = Renderer,
            AfterJson = JsonSerializer.Serialize(m), CorrelationId = Guid.NewGuid().ToString("N")
        };
        db.BusinessAuditEntries.Add(entry);
        try { await db.SaveChangesAsync(token); }
        finally { db.Entry(entry).State = EntityState.Detached; }
    }
}
