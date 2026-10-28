using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Applies the unified CFO read scope. Legacy includeAll arguments are not
/// authority. Workflow command permissions are checked independently.
/// </summary>
internal static class ExpenseRequestVisibilityQuery
{
    /// <summary>
    /// Same list/task policy with canonical data authority captured under the
    /// caller's Serializable read locks. Task participation remains additional
    /// filtering; it never grants visibility outside the captured CFO scope.
    /// </summary>
    internal static IQueryable<ExpenseRequest> ApplyCapturedListScope(
        BmsDbContext dbContext, IQueryable<ExpenseRequest> source,
        string actorExternalId, Bms.Application.Authorization.BusinessDataReadScope capturedScope,
        bool onlyMyTasks)
    {
        ValidateInputs(dbContext, source, actorExternalId);
        var visible = BusinessDataReadScopeQuery.ApplyCapturedRequests(source, capturedScope);
        return onlyMyTasks
            ? ApplyMyTasks(dbContext, visible, actorExternalId, isBmsAdministrator: false)
            : visible;
    }

    internal static IQueryable<ExpenseRequest> ApplyListScope(
        BmsDbContext dbContext,
        IQueryable<ExpenseRequest> source,
        string actorExternalId,
        bool includeAll,
        bool onlyMyTasks) =>
        ApplyListScope(
            dbContext,
            source,
            actorExternalId,
            includeAll,
            DateTimeOffset.UtcNow,
            onlyMyTasks);

    /// <summary>
    /// Builds the exact list scope once. Workflow tasks are intersected with
    /// current CFO ownership; participation never bypasses data visibility.
    /// </summary>
    internal static IQueryable<ExpenseRequest> ApplyListScope(
        BmsDbContext dbContext,
        IQueryable<ExpenseRequest> source,
        string actorExternalId,
        bool includeAll,
        DateTimeOffset authorizationAtUtc,
        bool onlyMyTasks) =>
        onlyMyTasks
            ? ApplyMyTasks(
                dbContext,
                BusinessDataReadScopeQuery.ApplyRequests(dbContext, source, actorExternalId),
                actorExternalId,
                // includeAll is a legacy full-read hint, not an administrator
                // profile. In particular, a financial director must retain
                // tasks assigned to them. The canonical SQL read scope and
                // exact current actor/role/stage predicates still apply.
                isBmsAdministrator: false)
            : Apply(
                dbContext,
                source,
                actorExternalId,
                includeAll,
                authorizationAtUtc);

    internal static IQueryable<ExpenseRequest> Apply(
        BmsDbContext dbContext,
        IQueryable<ExpenseRequest> source,
        string actorExternalId,
        bool includeAll) =>
        Apply(dbContext, source, actorExternalId, includeAll, DateTimeOffset.UtcNow);

    internal static IQueryable<ExpenseRequest> Apply(
        BmsDbContext dbContext,
        IQueryable<ExpenseRequest> source,
        string actorExternalId,
        bool includeAll,
        DateTimeOffset authorizationAtUtc) =>
        ApplyCore(
            dbContext,
            source,
            actorExternalId,
            includeAll,
            authorizationAtUtc,
            includeActiveSubstitutions: true);

    /// <summary>
    /// The payment calendar deliberately retains its treasury/list scope. W619
    /// delegates work on an existing request; it does not delegate treasury
    /// visibility. Keep this named boundary so a future shared-query change
    /// cannot silently broaden the calendar.
    /// </summary>
    internal static IQueryable<ExpenseRequest> ApplyPaymentCalendarScope(
        BmsDbContext dbContext,
        IQueryable<ExpenseRequest> source,
        string actorExternalId,
        bool includeAll,
        DateTimeOffset authorizationAtUtc) =>
        ApplyCore(
            dbContext,
            source,
            actorExternalId,
            includeAll,
            authorizationAtUtc,
            includeActiveSubstitutions: false);

    private static IQueryable<ExpenseRequest> ApplyCore(
        BmsDbContext dbContext,
        IQueryable<ExpenseRequest> source,
        string actorExternalId,
        bool includeAll,
        DateTimeOffset authorizationAtUtc,
        bool includeActiveSubstitutions) =>
        BusinessDataReadScopeQuery.ApplyRequests(dbContext, source, actorExternalId, includeNsiQueue: includeActiveSubstitutions);

    /// <summary>
    /// Applies full-card visibility. A treasurer has intentionally wider list
    /// visibility, but gains the card only after all approval stages completed.
    /// Independent initiator, approver and NSI rights remain additive for an
    /// employee who holds more than one functional role.
    /// </summary>
    internal static IQueryable<ExpenseRequest> ApplyDetail(
        BmsDbContext dbContext,
        IQueryable<ExpenseRequest> source,
        string actorExternalId,
        bool includeAll) =>
        ApplyDetail(dbContext, source, actorExternalId, includeAll, DateTimeOffset.UtcNow);

    internal static IQueryable<ExpenseRequest> ApplyDetail(
        BmsDbContext dbContext,
        IQueryable<ExpenseRequest> source,
        string actorExternalId,
        bool includeAll,
        DateTimeOffset authorizationAtUtc) =>
        ApplyDetailCore(
            dbContext,
            source,
            actorExternalId,
            includeAll,
            authorizationAtUtc,
            includeActiveSubstitutions: true);

    internal static IQueryable<ExpenseRequest> ApplyPaymentCalendarDetail(
        BmsDbContext dbContext,
        IQueryable<ExpenseRequest> source,
        string actorExternalId,
        bool includeAll,
        DateTimeOffset authorizationAtUtc) =>
        ApplyDetailCore(
            dbContext,
            source,
            actorExternalId,
            includeAll,
            authorizationAtUtc,
            includeActiveSubstitutions: false);

    private static IQueryable<ExpenseRequest> ApplyDetailCore(
        BmsDbContext dbContext,
        IQueryable<ExpenseRequest> source,
        string actorExternalId,
        bool includeAll,
        DateTimeOffset authorizationAtUtc,
        bool includeActiveSubstitutions) =>
        BusinessDataReadScopeQuery.ApplyRequests(dbContext, source, actorExternalId);

    /// <summary>
    /// Selects active, server-authoritative human workflow work for the current
    /// actor. Initiator draft/cancel actions are not an
    /// approval inbox, and administrators remain read-only even if their actor
    /// happens to have an employee mapping.
    /// </summary>
    internal static IQueryable<ExpenseRequest> ApplyMyTasks(
        BmsDbContext dbContext,
        IQueryable<ExpenseRequest> source,
        string actorExternalId,
        bool isBmsAdministrator)
    {
        ValidateInputs(dbContext, source, actorExternalId);

        if (isBmsAdministrator)
        {
            return source.Where(_ => false);
        }

        var actorEmployeeIds = ActiveActorEmployeeIds(dbContext, actorExternalId);
        var actorRoleCodes = ActiveActorRoleCodes(dbContext, actorEmployeeIds);
        var exactStageRoleCodes = BusinessDataReadScopeQuery.RoleCodes(dbContext, actorEmployeeIds);
        var selectedRoleRequired = dbContext.ActiveRoleSelection is not null;

        return source.Where(request =>
            actorEmployeeIds.Count() == 1
            && ((request.Status == ExpenseRequestStatus.InApproval
                    && request.ActiveApprovalRouteSnapshotNumber.HasValue
                    && request.ActiveApprovalRouteStageNumber > 0
                    && dbContext.ApprovalRouteStageParticipants
                        .AsNoTracking()
                        .Any(participant =>
                            participant.ExpenseRequestId == request.Id
                            && participant.SnapshotNumber
                                == request.ActiveApprovalRouteSnapshotNumber
                            && participant.StageNumber
                                == request.ActiveApprovalRouteStageNumber
                            && actorEmployeeIds.Contains(participant.EmployeeId)
                            && dbContext.ApprovalRouteSnapshots.AsNoTracking().Any(snapshot =>
                                snapshot.ExpenseRequestId == request.Id && snapshot.SnapshotNumber == participant.SnapshotNumber
                                && ((snapshot.WorkflowDefinitionVersion == 1 && !selectedRoleRequired) || ((snapshot.WorkflowDefinitionVersion >= 1 && snapshot.WorkflowDefinitionVersion <= 21)
                                    && dbContext.ApprovalRouteStages.AsNoTracking().Any(stage =>
                                        stage.ExpenseRequestId == request.Id && stage.SnapshotNumber == participant.SnapshotNumber
                                        && stage.StageNumber == participant.StageNumber
                                        && exactStageRoleCodes.Any(role => role == stage.RoleCode)
                                        && (stage.RoleCode != ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode
                                            || dbContext.BudgetOwnerCostCenterAssignments.AsNoTracking().Any(owner =>
                                                owner.EmployeeId == participant.EmployeeId && owner.CostCenterId == snapshot.CostCenterId
                                                && owner.IsActive && dbContext.CostCenters.AsNoTracking().Any(center =>
                                                    center.Id == owner.CostCenterId && center.IsActive && !center.IsGroup)))))))))
                || (request.Status == ExpenseRequestStatus.AwaitingNsi
                    && actorRoleCodes.Any(roleCode =>
                        roleCode == ExpenseRequestWorkflowCodes.NsiSpecialistRoleCode))
                || ((request.Status == ExpenseRequestStatus.Approved || request.Status == ExpenseRequestStatus.ToPay)
                    && actorRoleCodes.Any(roleCode =>
                        roleCode == ExpenseRequestWorkflowCodes.TreasurerRoleCode))));
    }

    // A financial read role is not an organization grant. Keep this predicate
    // inside that branch so independent initiator/treasurer permissions
    // and the administrator read-only bypass retain their existing scope.
    internal static IQueryable<Guid> ActiveActorOrganizationIds(
        BmsDbContext dbContext,
        string actorExternalId)
    {
        var grantActor=BusinessDataReadScopeQuery.GrantActor471(dbContext,actorExternalId);
        var delegated=dbContext.ActiveRoleSelection?.Substitution471 is not null;
        var employees=BusinessDataReadScopeQuery.EmployeeIds(dbContext,actorExternalId);
        return from grant in dbContext.ActorOrganizationGrants.AsNoTracking()
        join organization in dbContext.Organizations.AsNoTracking()
            on grant.OrganizationId equals organization.Id
        where grant.ActorExternalId == grantActor && (!delegated||employees.Count()==1)
            && grant.IsActive
            && organization.IsActive
        select grant.OrganizationId;
    }

    internal static IQueryable<Guid> ActiveActorEmployeeIds(
        BmsDbContext dbContext,
        string actorExternalId) =>
        BusinessDataReadScopeQuery.EmployeeIds(dbContext, actorExternalId);

    internal static IQueryable<string> ActiveSubstitutedPrincipalActorIds(
        BmsDbContext dbContext,
        string deputyActorExternalId,
        DateTimeOffset authorizationAtUtc) =>
        from substitution in dbContext.ExpenseRequestSubstitutions.AsNoTracking()
        join deputyMapping in dbContext.EmployeeActorMappings.AsNoTracking()
            on substitution.DeputyEmployeeId equals deputyMapping.EmployeeId
        join deputyEmployee in dbContext.Employees.AsNoTracking()
            on substitution.DeputyEmployeeId equals deputyEmployee.Id
        join principalEmployee in dbContext.Employees.AsNoTracking()
            on substitution.PrincipalEmployeeId equals principalEmployee.Id
        join principalMapping in dbContext.EmployeeActorMappings.AsNoTracking()
            on substitution.PrincipalEmployeeId equals principalMapping.EmployeeId
        where deputyMapping.ActorExternalId == deputyActorExternalId
            && deputyMapping.IsActive
            && deputyEmployee.IsActive
            && principalMapping.IsActive
            && principalEmployee.IsActive
            && substitution.IsActive
            && substitution.Id != Guid.Empty
            && substitution.PrincipalEmployeeId != substitution.DeputyEmployeeId
            && substitution.ValidFromUtc <= authorizationAtUtc
            && substitution.ValidThroughUtc >= authorizationAtUtc
            && substitution.Reason != string.Empty
            && substitution.Reason == substitution.Reason.Trim()
        select principalMapping.ActorExternalId;

    // The report and its paid/closed drilldown share these read roles. This
    // does not authorize closing commands or add items to the workflow inbox.
    internal static IQueryable<string> ActiveFinancialReadRoleCodes(
        BmsDbContext dbContext,
        IQueryable<Guid> actorEmployeeIds) =>
        ActiveActorRoleCodes(dbContext, actorEmployeeIds).Where(roleCode =>
            roleCode == ExpenseRequestWorkflowCodes.FinancialControllerRoleCode
            || roleCode == ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode
            || roleCode == ExpenseRequestWorkflowCodes.ResponsibleAccountantRoleCode);

    private static IQueryable<string> ActiveActorRoleCodes(
        BmsDbContext dbContext,
        IQueryable<Guid> actorEmployeeIds) =>
        BusinessDataReadScopeQuery.RoleCodes(dbContext, actorEmployeeIds);

    private static IQueryable<Guid> CurrentApprovalRequestIds(
        BmsDbContext dbContext,
        IQueryable<Guid> actorEmployeeIds) =>
        from participant in dbContext.ApprovalRouteStageParticipants.AsNoTracking()
        join request in dbContext.ExpenseRequests.AsNoTracking()
            on participant.ExpenseRequestId equals request.Id
        where request.Status == ExpenseRequestStatus.InApproval
            && request.ActiveApprovalRouteSnapshotNumber.HasValue
            && request.ActiveApprovalRouteStageNumber > 0
            && participant.SnapshotNumber == request.ActiveApprovalRouteSnapshotNumber
            && participant.StageNumber == request.ActiveApprovalRouteStageNumber
            && actorEmployeeIds.Contains(participant.EmployeeId)
        select participant.ExpenseRequestId;

    private static void ValidateInputs(
        BmsDbContext dbContext,
        IQueryable<ExpenseRequest> source,
        string actorExternalId)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(actorExternalId))
        {
            throw new ArgumentException(
                "Actor external id is required.",
                nameof(actorExternalId));
        }
    }
}
