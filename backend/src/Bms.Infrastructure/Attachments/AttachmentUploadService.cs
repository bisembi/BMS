using System.Security.Cryptography;
using System.Text;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Attachments;

/// <summary>Private pre-draft uploads, bound in the draft's Serializable transaction.
/// Sessions are serialized with uploads so save cannot silently miss an in-flight file.</summary>
public sealed class AttachmentUploadService(BmsDbContext db, AttachmentFileStorage files,
    IExpenseRequestAttachmentContentScanner scanner, ICurrentActor currentActor, IClock clock,
    IBusinessTransactionRunner transactions, IUnitOfWork unitOfWork,
    IBusinessAuditSink audit, IExpenseRequestAttachmentStore attachmentStore)
    : IExpenseRequestAttachmentBinder, IExpenseRequestAttachmentUploadService, IExpenseRequestAttachmentUploadStream415
{
    private const long SessionLimit = 100L * 1024 * 1024;
    private const long ActorPendingLimit = 1024L * 1024 * 1024;

    private async Task<string> ActorAsync(CancellationToken token)
    {
        var actor = ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (currentActor.IsBmsAdministrator || !await currentActor.CanCreateExpenseRequestAsync(token))
            throw new ActorForbiddenException();
        return actor;
    }
    public async Task VerifyCreateAuthorityAsync(CancellationToken token) => _ = await ActorAsync(token);

    public async Task<ExpenseRequestAttachmentListDto> ListAsync(Guid sessionId, CancellationToken token)
    {
        var actor = await ActorAsync(token);
        return await transactions.ExecuteAsync(async ct =>
        {
            await LockAsync(sessionId, actor, ct);
            var session = await SessionAsync(sessionId, actor, false, ct);
            var rows = await db.AttachmentFiles.AsNoTracking().Where(x => x.SessionId == sessionId && !x.IsWithdrawn)
                .OrderBy(x => x.UploadedAtUtc).ThenBy(x => x.Id).ToArrayAsync(ct);
            return new ExpenseRequestAttachmentListDto(sessionId.ToString("D"), session?.BoundRequestId is null,
                false, session?.BoundRequestId is null ? rows.Select(x => Dto(x, true)).ToArray() : []);
        }, token);
    }

    public async Task<ExpenseRequestAttachmentMutationDto> AddAsync(Guid sessionId,
        AddExpenseRequestAttachmentCommand command, string key, CancellationToken token)
    {
        var actor = await ActorAsync(token);
        var id = UploadId(sessionId, actor, key);
        if (command.ExpectedVersion != sessionId || command.Content is null
            || command.Kind is not (ExpenseRequestAttachmentKind.SupportingDocument or ExpenseRequestAttachmentKind.NewContractDocument)
            || !string.IsNullOrWhiteSpace(command.Comment)
            || !ExpenseRequestAttachmentUploadPolicy.Evaluate(command.FileName, command.Content.LongLength).IsAccepted)
            throw new ExpenseRequestAttachmentContentRejectedException();
        var bytes = command.Content.ToArray();
        var hash = SHA256.HashData(bytes);
        var verdict = await scanner.ScanAsync(command.FileName, bytes, token);
        if (verdict == ExpenseRequestAttachmentScanVerdict.Unavailable) throw new ExpenseRequestAttachmentScanUnavailableException();
        if (verdict != ExpenseRequestAttachmentScanVerdict.Accepted) throw new ExpenseRequestAttachmentContentRejectedException();
        return await transactions.ExecuteAsync(async ct =>
        {
            await LockAsync(sessionId, actor, ct);
            var session = await SessionAsync(sessionId, actor, true, ct);
            if (session!.BoundRequestId is not null) throw new ExpenseRequestVersionConflictException();
            var previous = await db.AttachmentFiles.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (previous is not null)
            {
                if (previous.SessionId != sessionId || previous.IsWithdrawn || previous.Kind != command.Kind.ToCode()
                    || previous.FileName != command.FileName.Trim().TrimEnd('.', ' ')
                    || previous.FileSizeBytes != bytes.LongLength || !previous.ContentSha256.SequenceEqual(hash))
                    throw new IdempotencyConflictException();
                _ = files.Read(new AttachmentFileLocation(previous.StorageRoot, previous.StorageKey, previous.FileSizeBytes, previous.ContentSha256));
                return new ExpenseRequestAttachmentMutationDto(sessionId.ToString("D"), Dto(previous, true));
            }
            // Withdrawal changes visibility, not physical retention or quota usage.
            var sessionRows = await db.AttachmentFiles.Where(x => x.SessionId == sessionId)
                .Select(x => x.FileSizeBytes).ToArrayAsync(ct);
            var actorBytes = await db.AttachmentFiles.Where(x => x.UploadedByExternalId == actor
                && x.ExpenseRequestId == null).SumAsync(x => (long?)x.FileSizeBytes, ct) ?? 0;
            if (sessionRows.Length >= 50 || sessionRows.Sum() + bytes.LongLength > SessionLimit
                || actorBytes + bytes.LongLength > ActorPendingLimit)
                throw new DomainRuleViolationException("Pending attachment storage quota exceeded.");
            var row = new AttachmentFile { Id = id, SessionId = sessionId,
                StorageKey = files.Write(id, bytes), StorageRoot = files.Root, Kind = command.Kind.ToCode(),
                FileName = command.FileName.Trim().TrimEnd('.', ' '), FileSizeBytes = bytes.LongLength,
                ContentSha256 = hash, UploadedAtUtc = clock.UtcNow, UploadedByExternalId = actor };
            db.AttachmentFiles.Add(row);
            Audit(actor, sessionId, "FileUploaded", id);
            await unitOfWork.SaveChangesAsync(ct);
            return new ExpenseRequestAttachmentMutationDto(sessionId.ToString("D"), Dto(row, true));
        }, token);
    }

    public async Task<ExpenseRequestAttachmentMutationDto> WithdrawAsync(Guid sessionId, Guid attachmentId, CancellationToken token)
    {
        var actor = await ActorAsync(token);
        return await transactions.ExecuteAsync(async ct =>
        {
            await LockAsync(sessionId, actor, ct);
            var session = await SessionAsync(sessionId, actor, false, ct);
            if (session is null) throw new ExpenseRequestAttachmentNotFoundException();
            if (session.BoundRequestId is not null) throw new ExpenseRequestVersionConflictException();
            var row = await db.AttachmentFiles.SingleOrDefaultAsync(x => x.Id == attachmentId && x.SessionId == sessionId, ct)
                ?? throw new ExpenseRequestAttachmentNotFoundException();
            if (!row.IsWithdrawn)
            {
                row.IsWithdrawn = true;
                Audit(actor, sessionId, "FileWithdrawn", attachmentId);
                await unitOfWork.SaveChangesAsync(ct);
            }
            return new ExpenseRequestAttachmentMutationDto(sessionId.ToString("D"), null);
        }, token);
    }

    public async Task<ExpenseRequestAttachmentDownload?> DownloadAsync(Guid sessionId, Guid attachmentId, CancellationToken token)
    {
        var actor = await ActorAsync(token);
        return await transactions.ExecuteAsync(async ct =>
        {
            await LockAsync(sessionId, actor, ct);
            var session = await SessionAsync(sessionId, actor, false, ct);
            if (session is null || session.BoundRequestId is not null) return null;
            var row = await db.AttachmentFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == attachmentId
                && x.SessionId == sessionId && !x.IsWithdrawn, ct);
            return row is null ? null : new ExpenseRequestAttachmentDownload(row.FileName,
                files.Read(new AttachmentFileLocation(row.StorageRoot, row.StorageKey, row.FileSizeBytes, row.ContentSha256)), row.UploadedAtUtc);
        }, token);
    }

    public async Task<ExpenseRequestAttachmentStreamDownload415?> OpenDownload415Async(Guid sessionId, Guid attachmentId, CancellationToken token)
    {
        var actor = await ActorAsync(token);
        ExpenseRequestAttachmentStreamDownload415? opened = null;
        try
        {
            return await transactions.ExecuteAsync(async ct =>
            {
                if (opened is not null) { await opened.DisposeAsync(); opened = null; }
                await LockAsync(sessionId, actor, ct);
                var session = await SessionAsync(sessionId, actor, false, ct);
                if (session is null || session.BoundRequestId is not null) return null;
                var row = await db.AttachmentFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == attachmentId
                    && x.SessionId == sessionId && !x.IsWithdrawn, ct);
                if (row is null) return null;
                if (!ExpenseRequestAttachmentUploadPolicy.Evaluate(row.FileName, row.FileSizeBytes).IsAccepted)
                    throw new ExpenseRequestAttachmentUnavailableException();
                var content = await files.OpenVerifiedRead415Async(new(row.StorageRoot, row.StorageKey, row.FileSizeBytes, row.ContentSha256), ct);
                opened = new(row.FileName, content, row.UploadedAtUtc);
                ct.ThrowIfCancellationRequested();
                return opened;
            }, token);
        }
        catch { if (opened is not null) await opened.DisposeAsync(); throw; }
    }

    public async Task<ExpenseRequestAttachmentMutationDto?> ResolveAsync(Guid sessionId, string key,
        Guid? deletedId, CancellationToken token)
    {
        var actor = await ActorAsync(token);
        var id = deletedId ?? UploadId(sessionId, actor, key);
        return await transactions.ExecuteAsync(async ct =>
        {
            await LockAsync(sessionId, actor, ct);
            var session = await SessionAsync(sessionId, actor, false, ct);
            if (session is null || session.BoundRequestId is not null) return null;
            var row = await db.AttachmentFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.SessionId == sessionId, ct);
            if (row is null || (deletedId is null && row.IsWithdrawn) || (deletedId is not null && !row.IsWithdrawn)) return null;
            return new ExpenseRequestAttachmentMutationDto(sessionId.ToString("D"), deletedId is null ? Dto(row, true) : null);
        }, token);
    }

    public async Task BindAsync(Guid sessionId, Guid requestId, string actorExternalId, CancellationToken token)
    {
        // Called only after create-command authorization, within its existing transaction.
        await LockAsync(sessionId, actorExternalId, token);
        var session = await SessionAsync(sessionId, actorExternalId, true, token);
        if (session!.BoundRequestId is not null) throw new ExpenseRequestVersionConflictException();
        var rows = await db.AttachmentFiles.Where(x => x.SessionId == sessionId && !x.IsWithdrawn)
            .OrderBy(x => x.Id).ToArrayAsync(token);
        var request = db.ExpenseRequests.Local.SingleOrDefault(x => x.Id == requestId);
        if (request is null || db.Entry(request).State != EntityState.Added
            || !string.Equals(request.InitiatorExternalId, actorExternalId, StringComparison.Ordinal))
            throw new ActorForbiddenException();
        if (!request.IsNewContract && rows.Any(x => x.Kind == ExpenseRequestAttachmentKindCode.NewContractDocument))
            throw new DomainRuleViolationException("A new-contract attachment requires a new-contract draft.");
        foreach (var row in rows)
            attachmentStore.Add(new StoredExpenseRequestAttachment(row.Id, requestId,
                ExpenseRequestAttachmentKindCode.Parse(row.Kind), row.FileName, row.FileSizeBytes,
                row.ContentSha256, files.Read(new AttachmentFileLocation(row.StorageRoot, row.StorageKey, row.FileSizeBytes, row.ContentSha256)),
                row.UploadedAtUtc, row.UploadedByExternalId, null, false, false));
        session.BoundRequestId = requestId;
        Audit(actorExternalId, sessionId, "BoundToRequest", requestId);
    }

    private async Task LockAsync(Guid id, string actor, CancellationToken token)
    {
        if (id == Guid.Empty || db.Database.CurrentTransaction is null) throw new DomainRuleViolationException("Upload session invalid.");
        if (!string.Equals(await ActorAsync(token), actor, StringComparison.Ordinal)) throw new ActorForbiddenException();
        // Actor quota first, then session: a single lock ordering for upload/bind/delete.
        foreach (var resource in new[] { "BMS:attachments:actor:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor))),
            "BMS:attachments:session:" + id.ToString("N") })
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result=sys.sp_getapplock @Resource={resource}, @LockMode=N'Exclusive',
                    @LockOwner=N'Transaction', @LockTimeout=15000;
                IF @result<0 THROW 65410,N'Attachment upload lock unavailable.',1;
                """, token);
    }

    private async Task<AttachmentUploadSession?> SessionAsync(Guid id, string actor, bool create, CancellationToken token)
    {
        var session = await db.AttachmentUploadSessions.SingleOrDefaultAsync(x => x.Id == id, token);
        if (session is not null && !string.Equals(session.ActorExternalId, actor, StringComparison.Ordinal))
            throw new ExpenseRequestAttachmentNotFoundException();
        if (session is null && create)
        {
            session = new AttachmentUploadSession { Id = id, ActorExternalId = actor, CreatedAtUtc = clock.UtcNow };
            db.AttachmentUploadSessions.Add(session);
        }
        return session;
    }

    private static Guid UploadId(Guid session, string actor, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 127 || key.Any(char.IsControl)) throw new InvalidIdempotencyKeyException("Invalid upload idempotency key.");
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{session:D}\n{actor}\n{key}"))[..16]);
    }
    private static ExpenseRequestAttachmentDto Dto(AttachmentFile x, bool canDelete) =>
        new(x.Id, x.Kind, x.FileName, x.FileSizeBytes, x.UploadedAtUtc, null, canDelete);
    private void Audit(string actor, Guid session, string action, Guid objectId) => audit.Append(Guid.NewGuid(),
        clock.UtcNow, actor, "AttachmentUploadSession", session.ToString("D"), action, null,
        System.Text.Json.JsonSerializer.Serialize(new { objectId }), Guid.NewGuid().ToString("N"));
}
