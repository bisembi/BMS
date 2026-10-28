using System.Security.Claims;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Application.Authorization;
using Bms.Application.Security;
using Bms.Domain.Workflow;
using Microsoft.AspNetCore.Authentication.Negotiate;

namespace Bms.Api.Security;

public sealed class HttpCurrentActor(
    IHttpContextAccessor httpContextAccessor,
    IWebHostEnvironment environment,
    IConfiguration configuration,
    BudgetOwnerCreationAuthority budgetOwnerCreationAuthority,
    IBusinessAuditReadAuthority businessAuditReadAuthority,
    IActiveRoleSelection? activeRoleSelection = null,
    ReferenceDataMaintenanceAuthority? referenceDataMaintenanceAuthority = null) : ICurrentActor
{
    private readonly BudgetOwnerCreationAuthority budgetOwnerCreationAuthority =
        budgetOwnerCreationAuthority
        ?? throw new ArgumentNullException(nameof(budgetOwnerCreationAuthority));
    private readonly IBusinessAuditReadAuthority businessAuditReadAuthority =
        businessAuditReadAuthority
        ?? throw new ArgumentNullException(nameof(businessAuditReadAuthority));

    public string? ExternalId
    {
        get
        {
            var context = httpContextAccessor.HttpContext;
            if (context?.User.Identity?.IsAuthenticated == true)
            {
                var principalIds = context.User.FindAll(ClaimTypes.NameIdentifier).ToArray();
                return principalIds.Length == 1
                    && ActorIdentity.IsCanonical(principalIds[0].Value)
                        ? principalIds[0].Value
                        : null;
            }

            if (!AllowsDevelopmentHeaders() || context is null)
            {
                return null;
            }

            var developmentIds = context.Request.Headers[
                DevelopmentActorHeaderBoundaryMiddleware.ActorHeader];
            return developmentIds.Count == 1
                && IsSingleCanonicalDevelopmentActorHeader(developmentIds[0])
                    ? developmentIds[0]
                    : null;
        }
    }

    public bool IsAuthenticated => ActorIdentity.IsCanonical(ExternalId);

    public bool IsWindowsAuthenticated =>
        httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true
        && string.Equals(
            httpContextAccessor.HttpContext.User.Identity.AuthenticationType,
            NegotiateDefaults.AuthenticationScheme,
            StringComparison.Ordinal);

    public bool HasBmsAdministratorAssignment =>
        IsAuthenticated
        && (HasScopedAvailableRole(BmsAccessProfileCodes.SystemAdministrator)
            || (!HasScopedSelection
                && HasRole(BmsRoles.Administrator, BmsRoles.AdministratorAlias)));

    public string? ActiveRoleCode => HasScopedSelection
        ? activeRoleSelection!.ActiveRoleCode
        : ResolveLegacyActiveRoleCode();
    public bool UsesActiveRoleSelection => activeRoleSelection is not null;

    public IReadOnlyList<string> AvailableRoleCodes => HasScopedSelection
        ? activeRoleSelection!.AvailableRoleCodes
        : ResolveLegacyAvailableRoleCodes();

    public string ActiveProfileCode => ResolveActiveProfileCode();

    public IReadOnlyList<string> AvailableProfileCodes
    {
        get
        {
            if (!IsAuthenticated) return Array.Empty<string>();
            if (IsBmsIntegrationAdministrator)
            {
                return [BmsAccessProfileCodes.IntegrationAdministrator];
            }
            return HasBmsAdministratorAssignment
                ? [BmsAccessProfileCodes.Business, BmsAccessProfileCodes.SystemAdministrator]
                : [BmsAccessProfileCodes.Business];
        }
    }

    public bool IsBmsAdministrator =>
        HasBmsAdministratorAssignment
        && string.Equals(
            ActiveRoleCode,
            BmsAccessProfileCodes.SystemAdministrator,
            StringComparison.Ordinal);

    public bool IsBmsIntegrationAdministrator =>
        IsAuthenticated
        && (HasScopedAvailableRole(BmsAccessProfileCodes.IntegrationAdministrator)
            || (!HasScopedSelection
                && HasRole(
                    BmsRoles.IntegrationAdministrator,
                    BmsRoles.IntegrationAdministratorAlias)));

    public bool CanAccessBusinessData =>
        IsAuthenticated && !IsBmsIntegrationAdministrator;

    public bool HasFullBusinessDataAccess =>
        CanAccessBusinessData
        && HasRole(ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode,
            ExpenseRequestWorkflowCodes.AuditorRoleCode);

    public Task<BusinessDataReadScope> ResolveBusinessDataReadScopeAsync(CancellationToken cancellationToken) =>
        budgetOwnerCreationAuthority.ResolveBusinessDataReadScopeAsync(
            ActorIdentity.RequireBusinessDataAccess(this), cancellationToken);

    public bool CanManageReferenceData =>
        CanAccessBusinessData
        && (IsBmsAdministrator
            || HasRole(
                ExpenseRequestWorkflowCodes.FinancialControllerRoleCode,
                ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode,
                ExpenseRequestWorkflowCodes.GeneralDirectorRoleCode));

    public async Task<bool> CanManageReferenceDataAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanManageReferenceData) return false;
        // The explicit local Development/Testing header adapter has no SQL
        // employee inventory. Never apply this compatibility path to an
        // authenticated principal, a scoped selector, or a production host.
        if (activeRoleSelection is null && AllowsDevelopmentHeaders()
            && httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated != true)
            return true;
        // DI supplies this in all host modes. Missing registration never falls back to cached rights.
        if (referenceDataMaintenanceAuthority is null) throw new BudgetDocumentAuthorityUnavailableException();
        return await referenceDataMaintenanceAuthority.ResolveAsync(this, cancellationToken) is not null;
    }

    public bool CanCreateExpenseRequest
    {
        get
        {
            if (!IsAuthenticated)
            {
                return false;
            }

            // Both system-administration profiles are unconditional read-only profiles.
            // Combining either one with a functional role must not unlock business commands.
            if (IsBmsAdministrator || IsBmsIntegrationAdministrator)
            {
                return false;
            }

            if (httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true)
            {
                // A production claim proves authentication only. The approved
                // creation authority is the active BUDGET_OWNER assignment of
                // the resolved employee, and actor-to-employee resolution is
                // not implemented yet. Do not turn a generic SSO role claim
                // into an unverified budget-owner permission.
                return false;
            }

            // A development-only header actor represents an initiator unless an
            // explicit role list says otherwise. This path is disabled in Production.
            var (isValid, roles) = DevelopmentRoles();
            return AllowsDevelopmentHeaders()
                && !string.IsNullOrWhiteSpace(ExternalId)
                && isValid
                && (roles.Count == 0 || roles.Contains(BmsRoles.Initiator)
                    || roles.Contains(BmsRoles.InitiatorAlias));
        }
    }

    public async Task<bool> CanCreateExpenseRequestAsync(CancellationToken cancellationToken)
    {
        if (!IsAuthenticated || IsBmsAdministrator || IsBmsIntegrationAdministrator)
        {
            return false;
        }

        // Development/test header actors are deliberately local adapters. They
        // have no SQL-backed identity mapping and retain the already reviewed
        // header-role contract; this branch is unreachable in Production.
        if (!HasAuthenticatedPrincipal())
        {
            return CanCreateExpenseRequest;
        }

        if (ExternalId is not string actorExternalId)
        {
            return false;
        }

        // Keep an unavailable projection distinguishable from a genuine
        // denial. Session and command endpoints map this reviewed exception
        // to a fixed 503; swallowing it here would turn an infrastructure
        // outage into a misleading 403 and make the command guard uneven.
        if (await budgetOwnerCreationAuthority.HasFinancialDraftAuthorityAsync(
                actorExternalId, cancellationToken))
        {
            return true;
        }

        var resolution = await budgetOwnerCreationAuthority.ResolveAnyAsync(
            actorExternalId,
            cancellationToken);
        return resolution.CanCreateExpenseRequest;
    }

    public async Task<bool> CanViewBusinessAuditMonitorAsync(
        CancellationToken cancellationToken)
    {
        if (!IsAuthenticated || IsBmsIntegrationAdministrator)
        {
            return false;
        }

        if (IsBmsAdministrator)
        {
            return true;
        }

        // Development/test headers are a loopback-only identity adapter. An
        // explicit AUDITOR role is sufficient there and avoids turning the
        // otherwise lightweight local session check into a SQL dependency.
        if (!HasAuthenticatedPrincipal())
        {
            var (isValid, roles) = DevelopmentRoles();
            return AllowsDevelopmentHeaders()
                && isValid
                && roles.Any(role => string.Equals(
                    role,
                    ExpenseRequestWorkflowCodes.AuditorRoleCode,
                    StringComparison.Ordinal));
        }

        return ExternalId is string actorExternalId
            && await businessAuditReadAuthority.CanReadAsync(
                actorExternalId,
                cancellationToken);
    }

    private bool HasRole(params string[] roles)
    {
        if (HasScopedSelection)
        {
            var activeRole = activeRoleSelection!.ActiveRoleCode;
            return activeRole is not null
                && roles.Any(role => IsSelectedPublicRole(activeRole, role));
        }

        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            return roles.Any(user.IsInRole);
        }

        var (isValid, developmentRoles) = DevelopmentRoles();
        return isValid && roles.Any(developmentRoles.Contains);
    }

    private string ResolveActiveProfileCode()
    {
        if (!IsAuthenticated) return BmsAccessProfileCodes.Business;
        if (IsBmsIntegrationAdministrator)
        {
            return BmsAccessProfileCodes.IntegrationAdministrator;
        }

        if (HasScopedSelection)
        {
            return string.Equals(
                    ActiveRoleCode,
                    BmsAccessProfileCodes.SystemAdministrator,
                    StringComparison.Ordinal)
                ? BmsAccessProfileCodes.SystemAdministrator
                : BmsAccessProfileCodes.Business;
        }

        var profileHeaders = httpContextAccessor.HttpContext?.Request.Headers[
            ActiveBmsProfileBoundaryMiddleware.HeaderName] ?? [];
        if (profileHeaders.Count == 1)
        {
            if (string.Equals(
                    profileHeaders[0],
                    BmsAccessProfileCodes.Business,
                    StringComparison.Ordinal))
            {
                return BmsAccessProfileCodes.Business;
            }
            if (string.Equals(
                    profileHeaders[0],
                    BmsAccessProfileCodes.SystemAdministrator,
                    StringComparison.Ordinal)
                && HasBmsAdministratorAssignment)
            {
                return BmsAccessProfileCodes.SystemAdministrator;
            }
        }

        // A dual FIN_DIRECTOR + BMS administrator assignment starts in the
        // business profile. Admin-only actors retain the old admin default;
        // switching never creates either assignment.
        return HasBmsAdministratorAssignment
            && !HasRole(ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode)
                ? BmsAccessProfileCodes.SystemAdministrator
                : BmsAccessProfileCodes.Business;
    }

    private (bool IsValid, HashSet<string> Roles) DevelopmentRoles()
    {
        var context = httpContextAccessor.HttpContext;
        if (!AllowsDevelopmentHeaders()
            || context?.User.Identity?.IsAuthenticated == true)
        {
            return (false, []);
        }

        var headers = context?.Request.Headers[
            DevelopmentActorHeaderBoundaryMiddleware.RolesHeader] ?? [];
        if (headers.Count == 0)
        {
            return (true, []);
        }

        if (headers.Count != 1)
        {
            return (false, []);
        }

        var header = headers[0];
        if (string.IsNullOrWhiteSpace(header))
        {
            return (false, []);
        }

        var roles = header.Split(
                [',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return roles.Count == 0 ? (false, []) : (true, roles);
    }

    private bool AllowsDevelopmentHeaders() =>
        DevelopmentActorHeaderBoundaryMiddleware.AllowsHeaders(environment, configuration);

    private bool HasAuthenticatedPrincipal() =>
        httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    private bool HasScopedSelection => activeRoleSelection?.IsInitialized == true;

    private bool HasScopedAvailableRole(string publicRoleCode) =>
        HasScopedSelection && activeRoleSelection!.HasAvailableRole(publicRoleCode);

    private static bool IsSelectedPublicRole(string selectedPublicRole, string requestedRole)
    {
        if (ActiveRoleCatalog.TryMapAssignmentCode(requestedRole, out var mapped))
        {
            return string.Equals(selectedPublicRole, mapped, StringComparison.Ordinal);
        }
        return string.Equals(selectedPublicRole, requestedRole, StringComparison.Ordinal);
    }

    private string? ResolveLegacyActiveRoleCode()
    {
        if (!IsAuthenticated) return null;
        if (HasRole(BmsRoles.IntegrationAdministrator, BmsRoles.IntegrationAdministratorAlias))
            return BmsAccessProfileCodes.IntegrationAdministrator;
        if (string.Equals(
                ResolveActiveProfileCode(),
                BmsAccessProfileCodes.SystemAdministrator,
                StringComparison.Ordinal))
            return BmsAccessProfileCodes.SystemAdministrator;
        return ResolveLegacyAvailableRoleCodes()
            .Where(ActiveRoleCatalog.IsBusinessRole)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private IReadOnlyList<string> ResolveLegacyAvailableRoleCodes()
    {
        if (!IsAuthenticated) return Array.Empty<string>();

        var available = new HashSet<string>(StringComparer.Ordinal);
        foreach (var roleCode in ActiveRoleCatalog.BusinessRoleCodes)
        {
            if (HasRole(roleCode)) available.Add(roleCode);
        }
        if (HasRole(BmsRoles.Administrator, BmsRoles.AdministratorAlias))
            available.Add(BmsAccessProfileCodes.SystemAdministrator);
        if (HasRole(BmsRoles.IntegrationAdministrator, BmsRoles.IntegrationAdministratorAlias))
            available.Add(BmsAccessProfileCodes.IntegrationAdministrator);
        return Array.AsReadOnly(available.Order(StringComparer.Ordinal).ToArray());
    }

    private static bool IsSingleCanonicalDevelopmentActorHeader(string? value) =>
        value is not null
        && ActorIdentity.IsCanonical(value)
        // Kestrel/HttpClient may coalesce repeated field lines into one
        // comma-separated value before HeaderDictionary is observed.  An
        // actor identifier is scalar, never an HTTP list, so accepting a
        // comma here would bypass the exact-one-value rule above.
        && value.IndexOf(',') < 0;

}
