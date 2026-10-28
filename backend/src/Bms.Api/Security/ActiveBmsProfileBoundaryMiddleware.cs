using Bms.Api.Diagnostics;
using Bms.Api.Problems;
using Bms.Application.Abstractions;
using Bms.Application.Security;

namespace Bms.Api.Security;

/// <summary>
/// Accepts an optional request-scoped profile selector only after the current
/// actor and its server-resolved assignments are available. The selector can
/// narrow an assignment; it cannot create one.
/// </summary>
public sealed class ActiveBmsProfileBoundaryMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Bms-Active-Profile";

    public async Task InvokeAsync(HttpContext context, ICurrentActor currentActor)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(currentActor);

        if (!context.Request.Path.StartsWithSegments(ApiActorAuthenticationBoundaryMiddleware.ApiPrefix))
        {
            await next(context);
            return;
        }

        var values = context.Request.Headers[HeaderName];
        if (values.Count == 0)
        {
            await next(context);
            return;
        }

        if (values.Count != 1 || values[0]?.Contains(',') == true)
        {
            await Reject(
                context,
                SafeProblemResults.ActiveBmsProfileInvalid(
                    ApiTraceIdentifier.Canonicalize(context.TraceIdentifier)));
            return;
        }

        var requestedProfile = values[0];
        if (!string.Equals(requestedProfile, BmsAccessProfileCodes.Business, StringComparison.Ordinal)
            && !string.Equals(
                requestedProfile,
                BmsAccessProfileCodes.SystemAdministrator,
                StringComparison.Ordinal))
        {
            await Reject(
                context,
                SafeProblemResults.ActiveBmsProfileInvalid(
                    ApiTraceIdentifier.Canonicalize(context.TraceIdentifier)));
            return;
        }

        // Integration administrators retain their separate technical boundary;
        // the business/system-admin switch is not a way around it.
        if (currentActor.IsBmsIntegrationAdministrator)
        {
            await Reject(
                context,
                SafeProblemResults.ActiveBmsProfileNotAvailable(
                    ApiTraceIdentifier.Canonicalize(context.TraceIdentifier)));
            return;
        }

        if (string.Equals(
                requestedProfile,
                BmsAccessProfileCodes.SystemAdministrator,
                StringComparison.Ordinal)
            && !currentActor.HasBmsAdministratorAssignment)
        {
            await Reject(
                context,
                SafeProblemResults.ActiveBmsProfileNotAssigned(
                    ApiTraceIdentifier.Canonicalize(context.TraceIdentifier)));
            return;
        }

        await next(context);
    }

    private static Task Reject(HttpContext context, IResult problem)
    {
        var response = context.Response;
        response.Headers.CacheControl = "no-store";
        response.OnStarting(static state =>
        {
            ((HttpResponse)state).Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        }, response);

        return problem.ExecuteAsync(context);
    }
}
