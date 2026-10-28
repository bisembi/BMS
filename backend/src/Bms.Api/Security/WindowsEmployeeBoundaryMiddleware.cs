using System.Net;
using System.Security.Claims;
using Bms.Api.Diagnostics;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;

namespace Bms.Api.Security;

/// <summary>
/// Every refusal this boundary can produce, and nothing else.
///
/// The boundary used to take a status, a code and a title from its caller. That
/// made the public text an argument, which is exactly what the fixed-public-text
/// contract forbids: a caller one refactor away could have put a mapping
/// diagnostic, an account name or an exception message on the wire, and the
/// literal-only guard could not see it. A closed enum makes the four reviewed
/// responses the only ones that exist, and an unknown value is refused before
/// anything is written rather than serialized.
/// </summary>
internal enum WindowsBoundaryRefusal
{
    AuthenticationRequired,
    EmployeeNotMapped,
    EmployeeMappingUnavailable,
    LocalHttpsRequired,
}

public sealed class WindowsEmployeeBoundaryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        EmployeeActorResolver resolver,
        EmployeeFunctionalRoleResolver roleResolver,
        IActiveRoleSelectionInitializer activeRoleSelection)
    {
        if (!context.Request.Path.StartsWithSegments(ApiActorAuthenticationBoundaryMiddleware.ApiPrefix))
        {
            await next(context);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await context.ChallengeAsync(NegotiateDefaults.AuthenticationScheme);
            if (!context.Response.HasStarted)
                await Reject(context, WindowsBoundaryRefusal.AuthenticationRequired);
            return;
        }

        var ids = context.User.FindAll(ClaimTypes.NameIdentifier).ToArray();
        if (context.User.Identity.AuthenticationType != NegotiateDefaults.AuthenticationScheme
            || ids.Length != 1 || !ActorIdentity.IsCanonical(ids[0].Value))
        {
            await Reject(context, WindowsBoundaryRefusal.EmployeeNotMapped);
            return;
        }

        try
        {
            var employee = await resolver.ResolveAsync(ids[0].Value, context.RequestAborted);
            if (!employee.HasActiveEmployee || employee.EmployeeId is not Guid employeeId)
            {
                await Reject(context, WindowsBoundaryRefusal.EmployeeNotMapped);
                return;
            }

            var roles = await roleResolver.ResolveAssignedAsync(
                employeeId,
                context.RequestAborted);
            if (!roles.EmployeeExists || !roles.EmployeeIsActive)
            {
                await Reject(context, WindowsBoundaryRefusal.EmployeeMappingUnavailable);
                return;
            }

            activeRoleSelection.InitializeAssignments(
                ids[0].Value,
                employeeId,
                roles.ActiveRoleCodes);

            // Never mutate the cached Negotiate identity.  More importantly,
            // do not copy every SQL assignment into effective role claims: the
            // selected request role is held by a dedicated scoped accessor.
            context.User = new ClaimsPrincipal(
                context.User.Identities.Select(identity => new ClaimsIdentity(identity)));
        }
        catch (Exception exception) when (exception is
            EmployeeActorResolutionUnavailableException or
            EmployeeFunctionalRoleResolutionUnavailableException)
        {
            await Reject(context, WindowsBoundaryRefusal.EmployeeMappingUnavailable);
            return;
        }
        await next(context);
    }

    /// <summary>
    /// The four reviewed refusals, each written out in full.
    ///
    /// Every title is a literal at its own call site, so the endpoint sweep can
    /// read it and hold it against the reviewed allowlist. The statuses, codes
    /// and titles are the ones this boundary already returned; nothing about the
    /// wire changed.
    /// </summary>
    internal static Task Reject(HttpContext context, WindowsBoundaryRefusal refusal)
    {
        var traceId = ApiTraceIdentifier.Canonicalize(context.TraceIdentifier);
        return refusal switch
        {
            WindowsBoundaryRefusal.AuthenticationRequired => Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Windows authentication is required.",
                extensions: Extensions("AuthenticationRequired", traceId)).ExecuteAsync(context),
            WindowsBoundaryRefusal.EmployeeNotMapped => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "An active BMS employee mapping is required.",
                extensions: Extensions("WindowsEmployeeNotMapped", traceId)).ExecuteAsync(context),
            WindowsBoundaryRefusal.EmployeeMappingUnavailable => Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Employee authorization is temporarily unavailable.",
                extensions: Extensions("WindowsEmployeeMappingUnavailable", traceId)).ExecuteAsync(context),
            WindowsBoundaryRefusal.LocalHttpsRequired => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "A direct local HTTPS connection is required.",
                extensions: Extensions("WindowsLocalHttpsRequired", traceId)).ExecuteAsync(context),
            // A value cast in from outside the reviewed set never becomes a
            // response: it fails here, before a status or a body exists.
            _ => throw new ArgumentOutOfRangeException(nameof(refusal)),
        };
    }

    private static Dictionary<string, object?> Extensions(string code, string traceId) =>
        new()
        {
            ["code"] = code,
            ["traceId"] = traceId
        };
}

// Runs before the Negotiate handler can consume a handshake. The first stage
// is expressly a direct localhost adapter; proxy placement is not configured.
public sealed class WindowsLocalTransportBoundaryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments(ApiActorAuthenticationBoundaryMiddleware.ApiPrefix))
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is null || !IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote)
                || !context.Request.IsHttps)
            {
                context.Response.Headers.CacheControl = "no-store";
                await WindowsEmployeeBoundaryMiddleware.Reject(
                    context, WindowsBoundaryRefusal.LocalHttpsRequired);
                return;
            }
        }
        await next(context);
    }
}
