using Bms.Api.Diagnostics;
using Bms.Api.Problems;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Application.ReferenceData;

namespace Bms.Api.Endpoints;

public static class ApprovalMatrixEndpoints
{
    public static IEndpointRouteBuilder MapApprovalMatrixEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/reference-data/approval-matrix/initiators",
            async (ApprovalMatrixService service, HttpResponse response, CancellationToken token) =>
            {
                response.Headers.CacheControl = "no-store";
                try { return Results.Ok(await service.GetVersionAsync(2, token)); }
                catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
                catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
                catch (OperationCanceledException) { throw; }
                catch { return SafeProblemResults.ApprovalMatrixUnavailable(); }
            });
        endpoints.MapGet("/api/v1/reference-data/approval-matrix", GetAsync)
            .WithTags("Reference data")
            .WithName("GetApprovalMatrix");
        return endpoints;
    }

    internal static async Task<IResult> GetAsync(
        ICurrentActor currentActor,
        ApprovalMatrixService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(currentActor);
            return Results.Ok(await service.GetAsync(cancellationToken));
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (!ApiProcessFatalExceptionClassifier.IsFatal(exception))
        {
            ApiFailureLog.DependencyUnavailable(response.HttpContext, ApiFailureOperation.ApprovalMatrixRead);
            return SafeProblemResults.ApprovalMatrixUnavailable();
        }
    }
}
