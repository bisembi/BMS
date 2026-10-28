namespace Bms.Application.Abstractions;

/// <summary>
/// Scans the exact bytes that will be persisted. A missing or failed provider
/// is distinguishable from a rejected file and always fails closed.
/// </summary>
public interface IExpenseRequestAttachmentContentScanner
{
    Task<ExpenseRequestAttachmentScanVerdict> ScanAsync(
        string fileName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);
}

public enum ExpenseRequestAttachmentScanVerdict
{
    Accepted = 1,
    Rejected = 2,
    Unavailable = 3
}
