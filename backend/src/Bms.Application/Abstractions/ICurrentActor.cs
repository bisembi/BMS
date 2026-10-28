using Bms.Application.Security;
using Bms.Application.Authorization;

namespace Bms.Application.Abstractions;

public interface ICurrentActor
{
    string? ExternalId { get; }

    bool IsAuthenticated { get; }

    /// <summary>
    /// True only for the live Windows/AD principal. Development actor headers
    /// are intentionally not treated as a source of employee display data.
    /// </summary>
    bool IsWindowsAuthenticated => false;

    bool IsBmsAdministrator { get; }

    bool IsBmsIntegrationAdministrator => false;

    /// <summary>
    /// Whether the current identity has a server-resolved BMS system
    /// administrator assignment, independent of the selected profile.
    /// Legacy test doubles default to their existing effective capability.
    /// </summary>
    bool HasBmsAdministratorAssignment => IsBmsAdministrator;

    /// <summary>
    /// Exactly one request-scoped role selected from current server
    /// assignments. Null means the actor has no effective business role.
    /// </summary>
    string? ActiveRoleCode => null;
    bool UsesActiveRoleSelection => false;

    /// <summary>Known current assignments exposed as stable selector codes.</summary>
    IReadOnlyList<string> AvailableRoleCodes => Array.Empty<string>();

    /// <summary>
    /// The current request profile. Implementations that support switching
    /// must resolve it from the request and re-check the assignment on every
    /// request. This default preserves the capability semantics of older test
    /// doubles.
    /// </summary>
    string ActiveProfileCode => IsBmsIntegrationAdministrator
        ? BmsAccessProfileCodes.IntegrationAdministrator
        : IsBmsAdministrator
            ? BmsAccessProfileCodes.SystemAdministrator
            : BmsAccessProfileCodes.Business;

    /// <summary>
    /// Profiles the server has confirmed for this actor. A client may display
    /// these choices, but cannot create an assignment by sending a code.
    /// </summary>
    IReadOnlyList<string> AvailableProfileCodes => IsBmsIntegrationAdministrator
        ? [BmsAccessProfileCodes.IntegrationAdministrator]
        : HasBmsAdministratorAssignment
            ? [BmsAccessProfileCodes.Business, BmsAccessProfileCodes.SystemAdministrator]
            : [BmsAccessProfileCodes.Business];

    bool CanAccessBusinessData => IsAuthenticated && !IsBmsIntegrationAdministrator;

    /// <summary>
    /// Legacy profile capability retained for command and adapter compatibility.
    /// Business-document visibility must use ResolveBusinessDataReadScopeAsync:
    /// a technical administration profile is not a financial read role.
    /// </summary>
    bool HasFullBusinessDataAccess => IsBmsAdministrator;

    /// <summary>
    /// Production implementations re-resolve employee, roles and canonical
    /// ownership for every read attempt. The default only preserves explicit
    /// full-read legacy adapters; it is never production authorization.
    /// </summary>
    Task<BusinessDataReadScope> ResolveBusinessDataReadScopeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CanAccessBusinessData && HasFullBusinessDataAccess
            ? BusinessDataReadScope.All : BusinessDataReadScope.None);
    }

    /// <summary>
    /// Allows maintenance of business reference data. This is intentionally
    /// separate from business-document visibility, which is resolved from
    /// the current canonical functional roles and CFO owner assignments.
    /// Technical integration administrators never receive this capability.
    /// Legacy test doubles that already expose full business access retain
    /// their reviewed behaviour through the default.
    /// </summary>
    bool CanManageReferenceData =>
        CanAccessBusinessData && HasFullBusinessDataAccess;

    /// <summary>Production writers must re-resolve SQL authority, not reuse a request snapshot.</summary>
    Task<bool> CanManageReferenceDataAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (UsesActiveRoleSelection)
            throw new BudgetDocumentAuthorityUnavailableException();
        return Task.FromResult(CanManageReferenceData);
    }

    bool CanCreateExpenseRequest { get; }

    /// <summary>
    /// Resolves the reviewed read-only capability for the specialized
    /// business-audit monitor. A production principal may require the
    /// authoritative employee/functional-role projection; local test doubles
    /// retain the legacy pure-administrator behavior by default.
    /// </summary>
    Task<bool> CanViewBusinessAuditMonitorAsync(CancellationToken cancellationToken) =>
        Task.FromResult(
            IsAuthenticated
            && IsBmsAdministrator
            && !IsBmsIntegrationAdministrator);

    /// <summary>
    /// Resolves the current creation capability against the authoritative
    /// employee/role/CFO projection. The synchronous property remains a
    /// compatibility value for local header actors and legacy test doubles;
    /// HTTP callers that may represent a production SSO principal must await
    /// this method.
    /// </summary>
    Task<bool> CanCreateExpenseRequestAsync(CancellationToken cancellationToken) =>
        Task.FromResult(CanCreateExpenseRequest);
}
