using System.Globalization;
using Bms.Api.Diagnostics;
using Bms.Api.Problems;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Endpoints;

/// <summary>
/// HTTP boundary for persisted expense-request attachments. File bytes are
/// accepted only as one bounded multipart part and are never echoed in JSON.
/// </summary>
public static partial class ExpenseRequestEndpoints
{
    internal static async Task<IResult> ListAttachmentsAsync(
        Guid id,
        [FromServices] ExpenseRequestAttachmentService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            var result = await service.ListAsync(id, cancellationToken);
            return result is null ? SafeProblemResults.ExpenseRequestNotFound() : Results.Ok(result);
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (ExpenseRequestAttachmentUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.ExpenseRequestAttachments);
            return SafeProblemResults.ExpenseRequestAttachmentsUnavailable();
        }
    }

    internal static async Task<IResult> DownloadAttachmentAsync(
        Guid id,
        Guid attachmentId,
        [FromServices] ExpenseRequestAttachmentService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        try
        {
            if (AttachmentDownloadAdmission415.Enabled(response))
                return await AttachmentDownloadAdmission415.BoundAsync(response, async () =>
                {
                    var stream = await service.OpenDownload415Async(id, attachmentId, cancellationToken);
                    if (stream is null) return SafeProblemResults.ExpenseRequestNotFound();
                    try
                    {
                        response.RegisterForDisposeAsync(stream);
                        response.Headers.LastModified = stream.UploadedAtUtc.ToString("R", CultureInfo.InvariantCulture);
                        return Results.File(stream.Content, "application/octet-stream", stream.FileName, enableRangeProcessing: false);
                    }
                    catch { await stream.DisposeAsync(); throw; }
                }, cancellationToken);
            var result = await service.DownloadAsync(id, attachmentId, cancellationToken);
            if (result is null)
            {
                return SafeProblemResults.ExpenseRequestNotFound();
            }

            response.Headers.LastModified = result.UploadedAtUtc.ToString(
                "R",
                CultureInfo.InvariantCulture);
            return Results.File(
                result.Content,
                "application/octet-stream",
                result.FileName,
                enableRangeProcessing: false);
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (ExpenseRequestAttachmentUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.ExpenseRequestAttachments);
            return SafeProblemResults.ExpenseRequestAttachmentsUnavailable();
        }
    }

    internal static async Task<IResult> AddAttachmentAsync(
        Guid id,
        [FromServices] ExpenseRequestAttachmentService service,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            var idempotencyKey = RequireSingleIdempotencyKey(request);
            var command = await ReadAddAttachmentCommandAsync(request, cancellationToken);
            var result = await service.AddAsync(
                id,
                command,
                idempotencyKey,
                cancellationToken);
            return Results.Json(result.Response, statusCode: result.ResponseStatusCode);
        }
        catch (BadHttpRequestException exception)
            when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return SafeProblemResults.ExpenseRequestAttachmentTooLarge();
        }
        catch (BadHttpRequestException)
        {
            ApiFailureLog.InvalidRequest(
                response.HttpContext,
                ApiFailureOperation.ExpenseRequestAttachments);
            return SafeProblemResults.InvalidRequest();
        }
        catch (InvalidDataException)
        {
            return SafeProblemResults.InvalidRequest();
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (ExpenseRequestNotFoundException)
        {
            return SafeProblemResults.ExpenseRequestNotFound();
        }
        catch (InvalidIdempotencyKeyException)
        {
            return SafeProblemResults.InvalidIdempotencyKey();
        }
        catch (IdempotencyConflictException)
        {
            return ConflictProblem(ExpenseRequestProblemCodes.IdempotencyConflict);
        }
        catch (ExpenseRequestVersionConflictException)
        {
            return ConflictProblem(ExpenseRequestProblemCodes.VersionConflict);
        }
        catch (ExpenseRequestAttachmentContentRejectedException)
        {
            return SafeProblemResults.ExpenseRequestAttachmentRejected();
        }
        catch (ExpenseRequestAttachmentScanUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.ExpenseRequestAttachments);
            return SafeProblemResults.ExpenseRequestAttachmentsUnavailable();
        }
        catch (ExpenseRequestAttachmentUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.ExpenseRequestAttachments);
            return SafeProblemResults.ExpenseRequestAttachmentsUnavailable();
        }
        catch (DomainRuleViolationException exception)
        {
            return SafeProblemResults.ExpenseAttachmentValidation(exception.Reason);
        }
    }

    internal static async Task<IResult> DeleteAttachmentAsync(
        Guid id,
        Guid attachmentId,
        [FromBody] DeleteExpenseRequestAttachmentCommand? command,
        [FromServices] ExpenseRequestAttachmentService service,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            var idempotencyKey = RequireSingleIdempotencyKey(request);
            if (command is null)
            {
                return SafeProblemResults.InvalidRequest();
            }

            var result = await service.DeleteAsync(
                id,
                attachmentId,
                command,
                idempotencyKey,
                cancellationToken);
            return Results.Json(result.Response, statusCode: result.ResponseStatusCode);
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (Exception exception)
            when (exception is ExpenseRequestNotFoundException
                or ExpenseRequestAttachmentNotFoundException)
        {
            return SafeProblemResults.ExpenseRequestNotFound();
        }
        catch (InvalidIdempotencyKeyException)
        {
            return SafeProblemResults.InvalidIdempotencyKey();
        }
        catch (IdempotencyConflictException)
        {
            return ConflictProblem(ExpenseRequestProblemCodes.IdempotencyConflict);
        }
        catch (ExpenseRequestVersionConflictException)
        {
            return ConflictProblem(ExpenseRequestProblemCodes.VersionConflict);
        }
        catch (ExpenseRequestAttachmentUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.ExpenseRequestAttachments);
            return SafeProblemResults.ExpenseRequestAttachmentsUnavailable();
        }
        catch (DomainRuleViolationException)
        {
            return SafeProblemResults.DomainRuleViolation();
        }
    }

    internal static async Task<IResult> ResolveAddAttachmentIdempotencyAsync(
        Guid id,
        [FromServices] ExpenseRequestAttachmentService service,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken) => await ResolveAttachmentIdempotencyAsync(
            token => service.ResolveAddIdempotencyAsync(
                id,
                RequireSingleIdempotencyKey(request),
                token),
            response,
            cancellationToken);

    internal static async Task<IResult> ResolveDeleteAttachmentIdempotencyAsync(
        Guid id,
        Guid attachmentId,
        [FromServices] ExpenseRequestAttachmentService service,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken) => await ResolveAttachmentIdempotencyAsync(
            token => service.ResolveDeleteIdempotencyAsync(
                id,
                attachmentId,
                RequireSingleIdempotencyKey(request),
                token),
            response,
            cancellationToken);

    private static async Task<IResult> ResolveAttachmentIdempotencyAsync(
        Func<CancellationToken, Task<ExpenseRequestAttachmentMutationDto?>> resolve,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Vary = "Idempotency-Key";
        try
        {
            var result = await resolve(cancellationToken);
            return result is null ? Results.NoContent() : Results.Ok(result);
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (ExpenseRequestNotFoundException)
        {
            return SafeProblemResults.ExpenseRequestNotFound();
        }
        catch (InvalidIdempotencyKeyException)
        {
            return SafeProblemResults.InvalidIdempotencyKey();
        }
        catch (ExpenseRequestAttachmentUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.ExpenseRequestAttachments);
            return SafeProblemResults.ExpenseRequestAttachmentsUnavailable();
        }
    }

    private static async Task<AddExpenseRequestAttachmentCommand> ReadAddAttachmentCommandAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            throw new InvalidDataException("Multipart form data is required.");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        if (form.Files.Count != 1
            || form.Files[0].Name != "file"
            || form.Keys.Any(key => key is not ("expectedVersion" or "kind" or "comment"))
            || !TryReadSingle(form, "expectedVersion", out var versionText)
            || !Guid.TryParseExact(versionText, "D", out var expectedVersion)
            || expectedVersion == Guid.Empty
            || !TryReadSingle(form, "kind", out var kindText)
            || !ExpenseRequestAttachmentKindCode.TryParse(kindText, out var kind))
        {
            throw new InvalidDataException("Attachment form shape is invalid.");
        }

        string? comment = null;
        if (form.ContainsKey("comment")
            && !TryReadSingle(form, "comment", out comment))
        {
            throw new InvalidDataException("Attachment comment shape is invalid.");
        }

        var file = form.Files[0];
        if (file.Length <= 0)
        {
            throw new ExpenseRequestAttachmentContentRejectedException();
        }
        if (file.Length > ExpenseRequestAttachmentUploadPolicy.MaximumFileSizeBytes)
        {
            throw new BadHttpRequestException(
                "The attachment request exceeded its public size limit.",
                StatusCodes.Status413PayloadTooLarge);
        }

        await using var buffer = new MemoryStream(
            checked((int)Math.Min(
                file.Length,
                ExpenseRequestAttachmentUploadPolicy.MaximumFileSizeBytes)));
        await file.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > ExpenseRequestAttachmentUploadPolicy.MaximumFileSizeBytes)
        {
            throw new BadHttpRequestException(
                "The attachment request exceeded its public size limit.",
                StatusCodes.Status413PayloadTooLarge);
        }
        if (buffer.Length != file.Length)
        {
            throw new InvalidDataException("Attachment content length is invalid.");
        }

        return new AddExpenseRequestAttachmentCommand(
            expectedVersion,
            kind,
            file.FileName,
            buffer.ToArray(),
            comment);
    }

    private static bool TryReadSingle(
        IFormCollection form,
        string key,
        out string? value)
    {
        var values = form[key];
        value = values.Count == 1 ? values[0] : null;
        return value is not null;
    }
}
