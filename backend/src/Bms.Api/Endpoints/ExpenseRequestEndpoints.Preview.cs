using Bms.Application.ExpenseRequests;
using Bms.Api.Problems;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Endpoints;

public static partial class ExpenseRequestEndpoints
{
    private static Task<IResult> PreviewAttachmentAsync(Guid id, Guid attachmentId,
        [FromServices] AttachmentPreviewService service, HttpResponse response, CancellationToken token) =>
        PreviewBoundaryAsync(id, attachmentId, false, service, response, token);
    private static Task<IResult> PreviewStagedAttachmentAsync(Guid sessionId, Guid attachmentId,
        [FromServices] AttachmentPreviewService service, HttpResponse response, CancellationToken token) =>
        PreviewBoundaryAsync(sessionId, attachmentId, true, service, response, token);

    private static Task<IResult> PreviewBoundaryAsync(Guid id, Guid attachmentId, bool staged,
        AttachmentPreviewService service, HttpResponse response, CancellationToken token) => StageBoundaryAsync(response, async () =>
    {
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        try
        {
            var preview = await service.PreviewAsync(id, attachmentId, staged, token);
            return preview is null ? SafeProblemResults.ExpenseRequestNotFound() :
                Results.Bytes(preview.Content, preview.MediaType);
        }
        catch (AttachmentPreviewUnsupportedException)
        { return SafeProblemResults.AttachmentPreviewUnsupported(); }
        catch (AttachmentPreviewInvalidException)
        { return SafeProblemResults.ExpenseRequestAttachmentRejected(); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return SafeProblemResults.ExpenseRequestAttachmentsUnavailable(); }
    });
}
