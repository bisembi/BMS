using Bms.Application.Abstractions;

namespace Bms.Infrastructure.Attachments;

internal sealed class UnavailableAttachmentContentScanner :
    IExpenseRequestAttachmentContentScanner
{
    internal static UnavailableAttachmentContentScanner Instance { get; } = new();

    private UnavailableAttachmentContentScanner()
    {
    }

    public Task<ExpenseRequestAttachmentScanVerdict> ScanAsync(
        string fileName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ExpenseRequestAttachmentScanVerdict.Unavailable);
    }
}
