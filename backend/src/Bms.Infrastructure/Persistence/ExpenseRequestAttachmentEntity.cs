namespace Bms.Infrastructure.Persistence;

public sealed class ExpenseRequestAttachmentEntity
{
    public Guid Id { get; set; }
    public Guid ExpenseRequestId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public byte[] ContentSha256 { get; set; } = [];
    public byte[] Content { get; set; } = [];
    public DateTimeOffset UploadedAtUtc { get; set; }
    public string UploadedByExternalId { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public bool IsPostSubmission { get; set; }
    public bool IsSealed { get; set; }
}
