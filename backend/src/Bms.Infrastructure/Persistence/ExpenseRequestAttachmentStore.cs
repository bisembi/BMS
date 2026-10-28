using System.Data;
using Bms.Application.Abstractions;
using Bms.Application.ExpenseRequests;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Bms.Infrastructure.Attachments;

namespace Bms.Infrastructure.Persistence;

public sealed class ExpenseRequestAttachmentStore(BmsDbContext dbContext, AttachmentFileStorage? fileStorage = null) :
    IExpenseRequestAttachmentStore, IExpenseRequestAttachmentStreamStore415
{
    public async Task<StoredAttachmentStream415?> OpenContent415Async(Guid requestId, Guid attachmentId, CancellationToken token)
    {
        // Project metadata only: do not materialize the SQL compatibility blob
        // merely to discover that the immutable private file is available.
        var metadata = await dbContext.ExpenseRequestAttachments.AsNoTracking()
            .Where(x => x.ExpenseRequestId == requestId && x.Id == attachmentId)
            .Select(x => new StoredExpenseRequestAttachment(x.Id, x.ExpenseRequestId,
                ExpenseRequestAttachmentKindCode.Parse(x.Kind), x.FileName, x.FileSizeBytes, x.ContentSha256,
                null, x.UploadedAtUtc, x.UploadedByExternalId, x.Comment, x.IsPostSubmission, x.IsSealed))
            .SingleOrDefaultAsync(token);
        if (metadata is null) return null;
        if (metadata.FileSizeBytes <= 0 || metadata.FileSizeBytes > ExpenseRequestAttachmentUploadPolicy.MaximumFileSizeBytes
            || metadata.ContentSha256.Length != 32
            || !ExpenseRequestAttachmentUploadPolicy.Evaluate(metadata.FileName, metadata.FileSizeBytes).IsAccepted)
            throw new ExpenseRequestAttachmentUnavailableException();
        var file = await dbContext.AttachmentFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == attachmentId
            && x.ExpenseRequestId == requestId && !x.IsWithdrawn, token);
        if (file is not null)
        {
            var content = await (fileStorage ?? throw new ExpenseRequestAttachmentUnavailableException())
                .OpenVerifiedRead415Async(new(file.StorageRoot, file.StorageKey, metadata.FileSizeBytes, metadata.ContentSha256), token);
            return new(metadata, content);
        }
        // Genuine old SQL-only rows stay readable. The HTTP admission boundary
        // bounds concurrent compatibility buffers as well as file downloads.
        var legacy = await dbContext.ExpenseRequestAttachments.AsNoTracking()
            .Where(x => x.ExpenseRequestId == requestId && x.Id == attachmentId
                && x.FileSizeBytes == metadata.FileSizeBytes && x.Content.Length == metadata.FileSizeBytes)
            .Select(x => x.Content).SingleOrDefaultAsync(token);
        if (legacy is null || legacy.LongLength != metadata.FileSizeBytes
            || legacy.LongLength > ExpenseRequestAttachmentUploadPolicy.MaximumFileSizeBytes)
            throw new ExpenseRequestAttachmentUnavailableException();
        return new(metadata, new MemoryStream(legacy, writable: false));
    }

    public async Task<IReadOnlyList<StoredExpenseRequestAttachment>> ListAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.ExpenseRequestAttachments
            .AsNoTracking()
            .Where(attachment => attachment.ExpenseRequestId == expenseRequestId)
            .OrderBy(attachment => attachment.UploadedAtUtc)
            .ThenBy(attachment => attachment.Id)
            .Select(attachment => new StoredExpenseRequestAttachment(
                attachment.Id,
                attachment.ExpenseRequestId,
                ExpenseRequestAttachmentKindCode.Parse(attachment.Kind),
                attachment.FileName,
                attachment.FileSizeBytes,
                attachment.ContentSha256,
                null,
                attachment.UploadedAtUtc,
                attachment.UploadedByExternalId,
                attachment.Comment,
                attachment.IsPostSubmission,
                attachment.IsSealed))
            .ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(rows);
    }

    public async Task<StoredExpenseRequestAttachment?> FindContentAsync(
        Guid expenseRequestId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.ExpenseRequestAttachments
            .AsNoTracking()
            .SingleOrDefaultAsync(attachment =>
                attachment.ExpenseRequestId == expenseRequestId
                && attachment.Id == attachmentId,
                cancellationToken);
        if (row is null) return null;
        var file = await dbContext.AttachmentFiles.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == attachmentId && x.ExpenseRequestId == expenseRequestId && !x.IsWithdrawn, cancellationToken);
        if (file is not null)
            row.Content = (fileStorage ?? throw new ExpenseRequestAttachmentUnavailableException())
                .Read(new AttachmentFileLocation(file.StorageRoot, file.StorageKey, row.FileSizeBytes, row.ContentSha256));
        return ToStored(row, includeContent: true);
    }

    public async Task<SubmissionAttachmentFacts> ReadSubmissionFactsAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken)
    {
        var kinds = await dbContext.ExpenseRequestAttachments
            .AsNoTracking()
            .Where(attachment => attachment.ExpenseRequestId == expenseRequestId
                && !attachment.IsPostSubmission)
            .Select(attachment => attachment.Kind)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        // Binding a staged upload happens before the create transaction's final
        // SaveChanges. Those verified rows are already tracked, not yet in SQL.
        kinds = kinds.Concat(dbContext.ChangeTracker.Entries<ExpenseRequestAttachmentEntity>()
            .Where(entry => entry.State == EntityState.Added
                && entry.Entity.ExpenseRequestId == expenseRequestId && !entry.Entity.IsPostSubmission)
            .Select(entry => entry.Entity.Kind)).Distinct(StringComparer.Ordinal).ToArray();
        var hasSupporting = kinds.Contains(
                ExpenseRequestAttachmentKindCode.SupportingDocument,
                StringComparer.Ordinal)
            || kinds.Contains(
                ExpenseRequestAttachmentKindCode.NewContractDocument,
                StringComparer.Ordinal);
        return new SubmissionAttachmentFacts(
            hasSupporting,
            kinds.Contains(
                ExpenseRequestAttachmentKindCode.NewContractDocument,
                StringComparer.Ordinal));
    }

    public void Add(StoredExpenseRequestAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        EnsureBusinessTransaction();
        if (attachment.Content is null)
        {
            throw new InvalidOperationException("Attachment content is required for persistence.");
        }

        if (fileStorage is not null)
        {
            var key = fileStorage.Write(attachment.Id, attachment.Content);
            var file = dbContext.AttachmentFiles.Local.SingleOrDefault(x => x.Id == attachment.Id);
            if (file is null)
                dbContext.AttachmentFiles.Add(new AttachmentFile { Id = attachment.Id,
                    ExpenseRequestId = attachment.ExpenseRequestId, StorageKey = key, StorageRoot = fileStorage.Root,
                    Kind = attachment.Kind.ToCode(), FileName = attachment.FileName,
                    FileSizeBytes = attachment.FileSizeBytes, ContentSha256 = attachment.ContentSha256,
                    UploadedAtUtc = attachment.UploadedAtUtc, UploadedByExternalId = attachment.UploadedByExternalId });
            else
            {
                if (file.StorageKey != key || file.IsWithdrawn) throw new ExpenseRequestAttachmentUnavailableException();
                file.ExpenseRequestId = attachment.ExpenseRequestId;
            }
        }
        // Compatibility copy keeps the previous release able to read files during an app-only rollback.
        dbContext.ExpenseRequestAttachments.Add(new ExpenseRequestAttachmentEntity
        {
            Id = attachment.Id,
            ExpenseRequestId = attachment.ExpenseRequestId,
            Kind = attachment.Kind.ToCode(),
            FileName = attachment.FileName,
            FileSizeBytes = attachment.FileSizeBytes,
            ContentSha256 = attachment.ContentSha256,
            Content = attachment.Content,
            UploadedAtUtc = attachment.UploadedAtUtc,
            UploadedByExternalId = attachment.UploadedByExternalId,
            Comment = attachment.Comment,
            IsPostSubmission = attachment.IsPostSubmission,
            IsSealed = attachment.IsSealed
        });
    }

    public async Task SealForSubmissionAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken)
    {
        EnsureBusinessTransaction();
        // Sealing is metadata-only. Materializing entities here would read every
        // compatibility BLOB into the submitting request's tracked graph.
        await dbContext.ExpenseRequestAttachments
            .Where(attachment => attachment.ExpenseRequestId == expenseRequestId
                && !attachment.IsSealed)
            .ExecuteUpdateAsync(setters => setters.SetProperty(attachment => attachment.IsSealed, true), cancellationToken);
        // ExecuteUpdate does not synchronize tracked entities. Include staged
        // Added rows and prevent a later SaveChanges from restoring an old flag
        // on Modified rows; no content is fetched or replaced by this operation.
        foreach (var entry in dbContext.ChangeTracker.Entries<ExpenseRequestAttachmentEntity>()
                     .Where(entry => entry.State is not (EntityState.Deleted or EntityState.Detached)
                         && entry.Entity.ExpenseRequestId == expenseRequestId))
        {
            entry.Entity.IsSealed = true;
        }
    }

    public async Task<StoredExpenseRequestAttachment?> RemoveAsync(
        Guid expenseRequestId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        EnsureBusinessTransaction();
        var row = await dbContext.ExpenseRequestAttachments
            .FromSqlInterpolated($$"""
                SELECT [attachment].*
                FROM [bms_web].[ExpenseRequestAttachments] AS [attachment]
                    WITH (UPDLOCK, HOLDLOCK, INDEX([PK_bms_web_ExpenseRequestAttachments]))
                WHERE [attachment].[ExpenseRequestId] = {{expenseRequestId}}
                  AND [attachment].[Id] = {{attachmentId}}
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        if (!row.IsSealed)
        {
            dbContext.ExpenseRequestAttachments.Remove(row);
            var file = await dbContext.AttachmentFiles.SingleOrDefaultAsync(x => x.Id == attachmentId, cancellationToken);
            if (file is not null) file.IsWithdrawn = true; // Physical bytes are retained, not deleted.
        }
        return ToStored(row, includeContent: false);
    }

    private static StoredExpenseRequestAttachment ToStored(
        ExpenseRequestAttachmentEntity row,
        bool includeContent) => new(
        row.Id,
        row.ExpenseRequestId,
        ExpenseRequestAttachmentKindCode.Parse(row.Kind),
        row.FileName,
        row.FileSizeBytes,
        row.ContentSha256,
        includeContent ? row.Content : null,
        row.UploadedAtUtc,
        row.UploadedByExternalId,
        row.Comment,
        row.IsPostSubmission,
        row.IsSealed);

    private void EnsureBusinessTransaction()
    {
        var transaction = dbContext.Database.CurrentTransaction;
        if (transaction is null
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
        {
            throw new InvalidOperationException(
                "Attachment mutation requires a Serializable business transaction.");
        }
    }
}
