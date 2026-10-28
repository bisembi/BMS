using Bms.Api.Problems;

namespace Bms.Api.Endpoints;

internal static class AttachmentDownloadAdmission415
{
    internal const int MaximumConcurrent = 4;
    private static readonly SemaphoreSlim Slots = new(MaximumConcurrent, MaximumConcurrent);
    internal static bool Enabled(HttpResponse response) => response.HttpContext.RequestServices
        ?.GetService<IConfiguration>()?.GetValue<bool>("Bms:Attachments:Streaming415Enabled") == true;

    internal static async Task<IResult> BoundAsync(HttpResponse response, Func<Task<IResult>> produce, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Slots.Wait(0))
        {
            response.Headers.RetryAfter = "2";
            return SafeProblemResults.ExpenseRequestAttachmentsUnavailable();
        }
        var lease = new Lease();
        // Also covers a later pipeline failure before ExecuteAsync is invoked.
        try { response.RegisterForDispose(lease); return new Held(await produce(), lease); }
        catch { lease.Dispose(); throw; }
    }

    private sealed class Lease : IDisposable
    {
        private int released;
        public void Dispose() { if (Interlocked.Exchange(ref released, 1) == 0) Slots.Release(); }
    }
    private sealed class Held(IResult result, Lease lease) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        { try { await result.ExecuteAsync(context); } finally { lease.Dispose(); } }
    }
}
