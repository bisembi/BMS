using Bms.Api.Diagnostics;
using Bms.Api.Problems;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Application.Security;

namespace Bms.Api.Security;

/// <summary>
/// Selects exactly one already-assigned BMS role for the current request.  The
/// selector is scalar, ordinal and case-sensitive; it can only narrow the
/// server-resolved assignment set.
/// </summary>
public sealed class ActiveBmsRoleBoundaryMiddleware(
    RequestDelegate next,
    IWebHostEnvironment environment,
    IConfiguration configuration)
{
    public const string HeaderName = "X-Bms-Active-Role";
    public const string SubstitutionHeader471 = "X-Bms-Substitution-Id";

    public async Task InvokeAsync(
        HttpContext context,
        IActiveRoleSelectionInitializer selection,
        IStaffSubstitutionStore471? substitutions=null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(selection);

        if (!context.Request.Path.StartsWithSegments(
                ApiActorAuthenticationBoundaryMiddleware.ApiPrefix))
        {
            await next(context);
            return;
        }

        if (!selection.IsInitialized)
        {
            if (!TryInitializeDevelopmentSelection(context, selection))
            {
                await Reject(
                    context,
                    SafeProblemResults.ActiveBmsRoleUnavailable(
                        ApiTraceIdentifier.Canonicalize(context.TraceIdentifier)));
                return;
            }
        }

        var requestedRole = ReadRequestedRole(context, out var invalidRoleHeader);
        if (invalidRoleHeader)
        {
            await Reject(
                context,
                SafeProblemResults.ActiveBmsRoleInvalid(
                    ApiTraceIdentifier.Canonicalize(context.TraceIdentifier)));
            return;
        }

        var profile = ReadRequestedProfile(context);
        var substitute=context.Request.Headers[SubstitutionHeader471];
        if(substitute.Count>0)
        {
            if(substitute.Count!=1||!Guid.TryParseExact(substitute[0],"D",out var id)||id==Guid.Empty
                ||requestedRole is null||!ActiveRoleCatalog.IsBusinessRole(requestedRole)||profile==BmsAccessProfileCodes.SystemAdministrator
                ||selection.HasAvailableRole(BmsAccessProfileCodes.IntegrationAdministrator)||selection.ActorExternalId is not{} real||substitutions is null)
            {await RejectNotAssigned(context);return;}
            StaffActingRole471? proof;
            try
            {
                proof=await substitutions.ResolveActiveAsync(real,id,requestedRole,context.RequestAborted);
            }
            catch(Bms.Application.Common.ActorForbiddenException){await RejectNotAssigned(context);return;}
            catch(OperationCanceledException){throw;}
            catch(Exception error)when(!ApiProcessFatalExceptionClassifier.IsFatal(error))
            {await Reject(context,Results.Problem(statusCode:503,title:"Замещение временно недоступно. Переключитесь на свою роль или повторите проверку."));return;}
            if(proof is null){await RejectNotAssigned(context);return;}
            selection.SelectSubstitution471(proof);await next(context);return;
        }
        if (selection.HasAvailableRole(BmsAccessProfileCodes.IntegrationAdministrator))
        {
            if (requestedRole is not null
                && !string.Equals(
                    requestedRole,
                    BmsAccessProfileCodes.IntegrationAdministrator,
                    StringComparison.Ordinal))
            {
                await RejectNotAssigned(context);
                return;
            }

            selection.Select(BmsAccessProfileCodes.IntegrationAdministrator);
            await next(context);
            return;
        }

        if (requestedRole is not null)
        {
            if (!selection.HasAvailableRole(requestedRole))
            {
                await RejectNotAssigned(context);
                return;
            }

            if ((string.Equals(
                        requestedRole,
                        BmsAccessProfileCodes.SystemAdministrator,
                        StringComparison.Ordinal)
                    && string.Equals(
                        profile,
                        BmsAccessProfileCodes.Business,
                        StringComparison.Ordinal))
                || (ActiveRoleCatalog.IsBusinessRole(requestedRole)
                    && string.Equals(
                        profile,
                        BmsAccessProfileCodes.SystemAdministrator,
                        StringComparison.Ordinal)))
            {
                await Reject(
                    context,
                    SafeProblemResults.ActiveBmsRoleProfileConflict(
                        ApiTraceIdentifier.Canonicalize(context.TraceIdentifier)));
                return;
            }

            selection.Select(requestedRole);
            await next(context);
            return;
        }

        if (string.Equals(
                profile,
                BmsAccessProfileCodes.SystemAdministrator,
                StringComparison.Ordinal))
        {
            if (!selection.HasAvailableRole(BmsAccessProfileCodes.SystemAdministrator))
            {
                await RejectNotAssigned(context);
                return;
            }
            selection.Select(BmsAccessProfileCodes.SystemAdministrator);
            await next(context);
            return;
        }

        // Personal defaults only select a freshly resolved business assignment.
        // Explicit role/profile branches above always take precedence. Presence
        // is separate from value: a revoked or malformed preference fails closed
        // rather than silently entering the owner's or administrator's role.
        var personalDefault = selection.EmployeeId is Guid employeeId
            ? configuration.GetSection("Bms:DefaultBusinessRoles").GetChildren()
                .SingleOrDefault(entry => string.Equals(
                    entry.Key, employeeId.ToString("D"), StringComparison.Ordinal))
            : null;
        if (personalDefault is not null)
        {
            var preferredRole = personalDefault.Value;
            selection.Select(ActiveRoleCatalog.IsBusinessRole(preferredRole)
                && selection.HasAvailableRole(preferredRole!) ? preferredRole : null);
            await next(context);
            return;
        }

        var defaultBusinessRole = selection.AvailableRoleCodes
            .Where(ActiveRoleCatalog.IsBusinessRole)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();

        // Preserve the existing admin-only default.  When BUSINESS is
        // explicitly requested, an admin-only actor receives no effective
        // business role instead of an implicit technical privilege.
        var defaultRole = defaultBusinessRole
            ?? (profile is null
                && selection.HasAvailableRole(BmsAccessProfileCodes.SystemAdministrator)
                    ? BmsAccessProfileCodes.SystemAdministrator
                    : null);
        selection.Select(defaultRole);
        await next(context);
    }

    private bool TryInitializeDevelopmentSelection(
        HttpContext context,
        IActiveRoleSelectionInitializer selection)
    {
        if (!DevelopmentActorHeaderBoundaryMiddleware.AllowsHeaders(
                environment,
                configuration)
            || context.User.Identity?.IsAuthenticated == true)
        {
            return false;
        }

        var actorHeaders = context.Request.Headers[
            DevelopmentActorHeaderBoundaryMiddleware.ActorHeader];
        if (actorHeaders.Count != 1
            || actorHeaders[0] is not string actorExternalId
            || !ActorIdentity.IsCanonical(actorExternalId)
            || actorExternalId.IndexOf(',') >= 0)
        {
            return false;
        }

        var roleHeaders = context.Request.Headers[
            DevelopmentActorHeaderBoundaryMiddleware.RolesHeader];
        var roleCodes = roleHeaders.Count switch
        {
            0 => Array.Empty<string>(),
            1 when roleHeaders[0] is string value => value.Split(
                [',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            _ => Array.Empty<string>(),
        };
        selection.InitializeAssignments(actorExternalId, employeeId: null, roleCodes);
        return true;
    }

    private static string? ReadRequestedRole(
        HttpContext context,
        out bool invalid)
    {
        var values = context.Request.Headers[HeaderName];
        if (values.Count == 0)
        {
            invalid = false;
            return null;
        }

        if (values.Count != 1
            || values[0] is not string value
            || value.IndexOf(',') >= 0
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || !ActiveRoleCatalog.IsPublicRole(value))
        {
            invalid = true;
            return null;
        }

        invalid = false;
        return value;
    }

    private static string? ReadRequestedProfile(HttpContext context)
    {
        var values = context.Request.Headers[ActiveBmsProfileBoundaryMiddleware.HeaderName];
        return values.Count == 1
            && values[0] is string value
            && (string.Equals(value, BmsAccessProfileCodes.Business, StringComparison.Ordinal)
                || string.Equals(
                    value,
                    BmsAccessProfileCodes.SystemAdministrator,
                    StringComparison.Ordinal))
                ? value
                : null;
    }

    private static Task RejectNotAssigned(HttpContext context) => Reject(
        context,
        SafeProblemResults.ActiveBmsRoleNotAssigned(
            ApiTraceIdentifier.Canonicalize(context.TraceIdentifier)));

    private static Task Reject(HttpContext context, IResult problem)
    {
        context.Response.Headers.CacheControl = "no-store";
        return problem.ExecuteAsync(context);
    }
}
