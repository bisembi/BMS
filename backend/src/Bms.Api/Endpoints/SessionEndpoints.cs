using Bms.Api.Diagnostics;
using Bms.Api.Problems;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Application.Security;
using Bms.Api.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Endpoints;

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/session", GetCurrentSessionAsync);
        return endpoints;
    }

    internal static async Task<IResult> GetCurrentSessionAsync(
        ICurrentActor actor,
        HttpResponse response,
        [FromServices] IAntiforgery antiforgery,
        [FromServices] CsrfProtectionSettings csrfProtection,
        CancellationToken cancellationToken,
        [FromServices] IEmployeeActorMappingReader? employeeActorMappingReader = null,
        [FromServices] IStaffSubstitutionStore471? staffSubstitutions = null,
        [FromServices] IActiveRoleSelection? roleSelection = null,
        [FromServices] IConfiguration? configuration = null)
    {
        response.Headers.CacheControl = "no-store";

        try
        {
            var actorExternalId = ActorIdentity.Require(actor);

            // Issuing the token also stores the cookie half of the pair, so
            // neither happens until a deployment switches issuance on. Issuing
            // is deliberately independent of requiring: a rollout hands tokens
            // out first and starts refusing later.
            var csrfToken = csrfProtection.IssuesToken
                ? antiforgery.GetAndStoreTokens(response.HttpContext).RequestToken
                : null;
            var actorDisplayName = await TryReadActorDisplayNameAsync(
                actor,
                actorExternalId,
                employeeActorMappingReader,
                cancellationToken);
            var readScope = actor.CanAccessBusinessData
                ? await actor.ResolveBusinessDataReadScopeAsync(cancellationToken)
                : BusinessDataReadScope.None;
            IReadOnlyList<StaffRoleChoice471>? staffRoles = null;
            var staffEnabled = actor.CanAccessBusinessData && configuration?.GetValue<bool>("Bms:StaffSubstitution471:Enabled") == true;
            if (staffEnabled && staffSubstitutions is not null)
            {
                var active = await staffSubstitutions.ActiveAsync(actorExternalId, cancellationToken);
                staffRoles = active.Select(d => new StaffRoleChoice471(d.DocumentId,d.RoleCode,d.PrincipalName,
                    DateOnly.FromDateTime(d.ValidFromUtc.ToOffset(TimeSpan.FromHours(5)).DateTime),
                    d.ValidBeforeUtc is {} before ? DateOnly.FromDateTime(before.ToOffset(TimeSpan.FromHours(5)).DateTime).AddDays(-1) : null)).ToArray();
            }
            return Results.Ok(new CurrentSessionDto(
                actorExternalId,
                actor.ActiveProfileCode,
                actor.AvailableProfileCodes,
                actor.IsBmsAdministrator,
                actor.IsBmsIntegrationAdministrator,
                actor.CanAccessBusinessData,
                actor.HasFullBusinessDataAccess,
                actor.CanManageReferenceData,
                await actor.CanCreateExpenseRequestAsync(cancellationToken),
                await actor.CanViewBusinessAuditMonitorAsync(cancellationToken),
                csrfToken,
                actorDisplayName,
                CanReadBudgetReports: readScope.CanReadAll || readScope.CostCenterIds.Count > 0,
                CanReadAllBusinessData: readScope.CanReadAll,
                ActiveRoleCode: actor.ActiveRoleCode,
                AvailableRoleCodes: actor.AvailableRoleCodes,
                StaffSubstitutionRoles471: staffRoles,
                ActiveSubstitutionId471: roleSelection?.Substitution471?.DocumentId,
                OwnRoleCodes471: staffEnabled ? roleSelection?.OwnRoleCodes471 : null));
        }
        catch (ActorNotAuthenticatedException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.");
        }
        catch (BudgetOwnerCreationAuthorityUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.ExpenseRequestCreationAuthority);
            return SafeProblemResults.ExpenseRequestCreationAuthorityUnavailable();
        }
        catch (BusinessAuditReadAuthorityUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.BusinessAuditReadAuthority);
            return SafeProblemResults.BusinessAuditUnavailable();
        }
        catch (StaffSubstitutionUnavailable471)
        { return Results.Problem(statusCode:503,title:"Не удалось проверить замещения сотрудника."); }
    }

    internal static async Task<string?> TryReadActorDisplayNameAsync(
        ICurrentActor actor,
        string actorExternalId,
        IEmployeeActorMappingReader? employeeActorMappingReader,
        CancellationToken cancellationToken)
    {
        if (!actor.IsWindowsAuthenticated || employeeActorMappingReader is null)
        {
            return null;
        }

        try
        {
            var rows = await employeeActorMappingReader.ReadAsync(
                actorExternalId,
                cancellationToken);
            if (rows.Count != 1) return null;
            var row = rows[0];
            return row.MappingIsActive
                && row.EmployeeIsActive
                && !string.IsNullOrWhiteSpace(row.EmployeeName)
                ? row.EmployeeName.Trim()
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The name is presentation-only. A catalogue read outage must not
            // change the already reviewed authentication/capability result.
            return null;
        }
    }
}
