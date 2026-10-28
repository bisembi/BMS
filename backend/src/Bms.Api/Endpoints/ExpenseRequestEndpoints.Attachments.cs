using Bms.Api.Problems;
using Bms.Api.Diagnostics;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Bms.Domain.ExpenseRequests;

namespace Bms.Api.Endpoints;

/// Attachment upload intent boundary. This endpoint validates only the public
/// candidate file shape; it does not receive bytes, persist metadata, mutate a
/// draft, prove attachment facts or grant submit authority.
public static partial class ExpenseRequestEndpoints
{
    internal static async Task<IResult> ValidateAttachmentUploadIntent(
        ExpenseRequestAttachmentUploadIntentCommand? command,
        ICurrentActor actor,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";

        try
        {
            _ = ActorIdentity.RequireBusinessDataAccess(actor);
            if (!await actor.CanCreateExpenseRequestAsync(cancellationToken))
            {
                throw new ActorForbiddenException();
            }

            if (command is null)
            {
                return SafeProblemResults.InvalidRequest();
            }

            var result = ExpenseRequestAttachmentUploadPolicy.Evaluate(
                command.FileName,
                command.FileSizeBytes);
            return Results.Ok(ExpenseRequestAttachmentUploadIntentDto.FromPolicyResult(result));
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (BudgetOwnerCreationAuthorityUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.ExpenseRequestCreationAuthority);
            return SafeProblemResults.ExpenseRequestCreationAuthorityUnavailable();
        }
    }
}
