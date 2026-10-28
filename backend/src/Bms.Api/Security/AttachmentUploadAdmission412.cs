using Bms.Api.Problems;
using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Api.Security;

/// <summary>
/// No waiting queue and no idle actor partitions. A lease covers multipart
/// parsing, CSRF form fallback, scanning and persistence, not just the reader.
/// Limits are per application instance; storage/business quotas remain separate.
/// </summary>
public sealed class AttachmentUploadAdmission412
{
    public const int MaximumConcurrentUploads = 2;
    private readonly object sync = new();
    private readonly HashSet<string> activeActors = new(StringComparer.Ordinal);

    public IDisposable? TryEnter(string actor)
    {
        if (!ActorIdentity.IsCanonical(actor)) throw new ArgumentException("Canonical actor required.", nameof(actor));
        lock (sync)
        {
            if (activeActors.Count >= MaximumConcurrentUploads || !activeActors.Add(actor)) return null;
            return new Lease(this, actor);
        }
    }

    private sealed class Lease(AttachmentUploadAdmission412 owner, string actor) : IDisposable
    {
        private AttachmentUploadAdmission412? owner = owner;
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref owner, null);
            if (current is null) return;
            lock (current.sync) current.activeActors.Remove(actor);
        }
    }
}

public sealed class AttachmentUploadAdmissionFilter412(
    AttachmentUploadAdmission412 admission, ICurrentActor actor) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
        string identity;
        try { identity = ActorIdentity.RequireBusinessDataAccess(actor); }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        using var lease = admission.TryEnter(identity);
        if (lease is null)
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.HttpContext.Response.Headers.RetryAfter = "2";
            return SafeProblemResults.ExpenseRequestAttachmentsUnavailable();
        }
        return await next(context);
    }
}
