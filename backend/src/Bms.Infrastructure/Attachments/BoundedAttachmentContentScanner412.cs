using Bms.Application.Abstractions;

namespace Bms.Infrastructure.Attachments;

/// <summary>
/// Defence in depth for all configured scanner callers, including non-HTTP
/// callers. Reject saturation before the provider creates a copy/temp/process.
/// The DI scanner is a singleton; no queued request retains a caller's bytes.
/// </summary>
internal sealed class BoundedAttachmentContentScanner412(IExpenseRequestAttachmentContentScanner inner)
    : IExpenseRequestAttachmentContentScanner
{
    internal const int MaximumConcurrentScans = 2;
    private readonly SemaphoreSlim slots = new(MaximumConcurrentScans, MaximumConcurrentScans);

    public async Task<ExpenseRequestAttachmentScanVerdict> ScanAsync(
        string fileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        if (!await slots.WaitAsync(0, cancellationToken)) return ExpenseRequestAttachmentScanVerdict.Unavailable;
        try { return await inner.ScanAsync(fileName, content, cancellationToken); }
        finally { slots.Release(); }
    }
}
