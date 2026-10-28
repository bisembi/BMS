namespace Bms.Domain.ExpenseRequests;

public enum ExpenseRequestAttachmentRejection
{
    MissingFileName,
    EmptyFile,
    FileTooLarge,
    InvalidFileName,
    ExecutableExtension
}

public sealed record ExpenseRequestAttachmentValidationResult(
    bool IsAccepted,
    ExpenseRequestAttachmentRejection? Rejection)
{
    public static ExpenseRequestAttachmentValidationResult Accepted { get; } = new(true, null);

    public static ExpenseRequestAttachmentValidationResult Reject(
        ExpenseRequestAttachmentRejection rejection) =>
        new(false, rejection);
}

/// <summary>
/// Provider-independent server policy for the file shape accepted by the approved
/// expense-request attachment contract. Storage, malware scanning and authorization
/// remain separate boundaries.
/// </summary>
public static class ExpenseRequestAttachmentUploadPolicy
{
    public const long MaximumFileSizeBytes = 25L * 1024L * 1024L;
    public const int MaximumFileNameLength = 255;

    private static readonly HashSet<string> ExecutableExtensions = new(
        [
            "exe",
            "com",
            "bat",
            "cmd",
            "msi",
            "ps1",
            "vbs",
            "js",
            "jse",
            "wsf",
            "wsh",
            "scr",
            "pif",
            "cpl",
            "hta"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static ExpenseRequestAttachmentValidationResult Evaluate(
        string? fileName,
        long fileSizeBytes)
    {
        var normalizedFileName = fileName?.Trim();
        if (string.IsNullOrEmpty(normalizedFileName))
        {
            return ExpenseRequestAttachmentValidationResult.Reject(
                ExpenseRequestAttachmentRejection.MissingFileName);
        }

        if (fileSizeBytes <= 0)
        {
            return ExpenseRequestAttachmentValidationResult.Reject(
                ExpenseRequestAttachmentRejection.EmptyFile);
        }

        if (fileSizeBytes > MaximumFileSizeBytes)
        {
            return ExpenseRequestAttachmentValidationResult.Reject(
                ExpenseRequestAttachmentRejection.FileTooLarge);
        }

        if (normalizedFileName.Length > MaximumFileNameLength)
        {
            return ExpenseRequestAttachmentValidationResult.Reject(
                ExpenseRequestAttachmentRejection.InvalidFileName);
        }

        normalizedFileName = normalizedFileName.TrimEnd('.', ' ');
        if (normalizedFileName.Length == 0)
        {
            return ExpenseRequestAttachmentValidationResult.Reject(
                ExpenseRequestAttachmentRejection.InvalidFileName);
        }

        var lastDot = normalizedFileName.LastIndexOf('.');
        var extension = lastDot >= 0
            ? normalizedFileName[(lastDot + 1)..].Trim()
            : string.Empty;

        if (ExecutableExtensions.Contains(extension))
        {
            return ExpenseRequestAttachmentValidationResult.Reject(
                ExpenseRequestAttachmentRejection.ExecutableExtension);
        }

        // A browser normally supplies only a basename, but the public boundary
        // is also callable directly. Reject path/header metacharacters before
        // the name reaches persistence or Content-Disposition.
        return normalizedFileName.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0
            || normalizedFileName.Any(char.IsControl)
            ? ExpenseRequestAttachmentValidationResult.Reject(
                ExpenseRequestAttachmentRejection.InvalidFileName)
            : ExpenseRequestAttachmentValidationResult.Accepted;
    }
}
