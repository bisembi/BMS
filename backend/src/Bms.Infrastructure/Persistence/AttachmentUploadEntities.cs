namespace Bms.Infrastructure.Persistence;

public sealed class AttachmentUploadSession
{
    public Guid Id { get; set; }
    public string ActorExternalId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? BoundRequestId { get; set; }
}

public sealed class AttachmentFile
{
    public Guid Id { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? ExpenseRequestId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string StorageRoot { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public byte[] ContentSha256 { get; set; } = [];
    public DateTimeOffset UploadedAtUtc { get; set; }
    public string UploadedByExternalId { get; set; } = string.Empty;
    public bool IsWithdrawn { get; set; }
}
