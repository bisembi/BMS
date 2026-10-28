using Bms.Api.Problems;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Common;
using Bms.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Endpoints;

public static partial class ExpenseRequestEndpoints
{
    private static Task<IResult> ListStagedAttachmentsAsync(Guid sessionId, [FromServices] IExpenseRequestAttachmentUploadService service,
        HttpResponse response, CancellationToken token) => StageBoundaryAsync(response,
            async () => Results.Ok(await service.ListAsync(sessionId, token)));

    private static Task<IResult> AddStagedAttachmentAsync(Guid sessionId, [FromServices] IExpenseRequestAttachmentUploadService service,
        HttpRequest request, HttpResponse response, CancellationToken token) => StageBoundaryAsync(response,
        async () =>
        {
            await service.VerifyCreateAuthorityAsync(token);
            var key = RequireSingleIdempotencyKey(request);
            var command = await ReadAddAttachmentCommandAsync(request, token);
            return Results.Json(await service.AddAsync(sessionId, command, key, token), statusCode: 201);
        });

    private static Task<IResult> WithdrawStagedAttachmentAsync(Guid sessionId, Guid attachmentId,
        [FromBody] DeleteExpenseRequestAttachmentCommand command, [FromServices] IExpenseRequestAttachmentUploadService service,
        HttpRequest request, HttpResponse response, CancellationToken token) => StageBoundaryAsync(response,
        async () =>
        {
            _ = RequireSingleIdempotencyKey(request);
            if (command.ExpectedVersion != sessionId) return SafeProblemResults.InvalidRequest();
            return Results.Ok(await service.WithdrawAsync(sessionId, attachmentId, token));
        });

    private static Task<IResult> DownloadStagedAttachmentAsync(Guid sessionId, Guid attachmentId,
        [FromServices] IExpenseRequestAttachmentUploadService service, HttpResponse response, CancellationToken token) => StageBoundaryAsync(response,
        async () =>
        {
            response.Headers["X-Content-Type-Options"] = "nosniff";
            if (AttachmentDownloadAdmission415.Enabled(response) && service is IExpenseRequestAttachmentUploadStream415 streams)
                return await AttachmentDownloadAdmission415.BoundAsync(response, async () =>
                {
                    var stream = await streams.OpenDownload415Async(sessionId, attachmentId, token);
                    if (stream is null) return SafeProblemResults.ExpenseRequestNotFound();
                    try
                    {
                        response.RegisterForDisposeAsync(stream);
                        return Results.File(stream.Content, "application/octet-stream", stream.FileName, enableRangeProcessing: false);
                    }
                    catch { await stream.DisposeAsync(); throw; }
                }, token);
            var result = await service.DownloadAsync(sessionId, attachmentId, token);
            return result is null ? SafeProblemResults.ExpenseRequestNotFound() :
                Results.File(result.Content, "application/octet-stream", result.FileName, enableRangeProcessing: false);
        });

    private static Task<IResult> ResolveStagedAddAsync(Guid sessionId, [FromServices] IExpenseRequestAttachmentUploadService service,
        HttpRequest request, HttpResponse response, CancellationToken token) => StageBoundaryAsync(response,
        async () =>
        {
            var result = await service.ResolveAsync(sessionId, RequireSingleIdempotencyKey(request), null, token);
            return result is null ? Results.NoContent() : Results.Ok(result);
        });

    private static Task<IResult> ResolveStagedDeleteAsync(Guid sessionId, Guid attachmentId, [FromServices] IExpenseRequestAttachmentUploadService service,
        HttpRequest request, HttpResponse response, CancellationToken token) => StageBoundaryAsync(response,
        async () =>
        {
            var result = await service.ResolveAsync(sessionId, RequireSingleIdempotencyKey(request), attachmentId, token);
            return result is null ? Results.NoContent() : Results.Ok(result);
        });

    private static async Task<IResult> StageBoundaryAsync(HttpResponse response, Func<Task<IResult>> action)
    {
        response.Headers.CacheControl = "no-store";
        try { return await action(); }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (ExpenseRequestAttachmentNotFoundException) { return SafeProblemResults.ExpenseRequestNotFound(); }
        catch (ExpenseRequestVersionConflictException) { return ConflictProblem(ExpenseRequestProblemCodes.VersionConflict); }
        catch (IdempotencyConflictException) { return ConflictProblem(ExpenseRequestProblemCodes.IdempotencyConflict); }
        catch (InvalidIdempotencyKeyException) { return SafeProblemResults.InvalidIdempotencyKey(); }
        catch (ExpenseRequestAttachmentContentRejectedException) { return SafeProblemResults.ExpenseRequestAttachmentRejected(); }
        catch (BadHttpRequestException e) when (e.StatusCode == 413) { return SafeProblemResults.ExpenseRequestAttachmentTooLarge(); }
        catch (Exception e) when (e is BadHttpRequestException or InvalidDataException) { return SafeProblemResults.InvalidRequest(); }
        catch (DomainRuleViolationException) { return SafeProblemResults.DomainRuleViolation(); }
        catch (Exception e) when (e is ExpenseRequestAttachmentUnavailableException or ExpenseRequestAttachmentScanUnavailableException
            or Microsoft.Data.SqlClient.SqlException or Microsoft.EntityFrameworkCore.DbUpdateException)
        { return SafeProblemResults.ExpenseRequestAttachmentsUnavailable(); }
    }
}
