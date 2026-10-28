using Bms.Application.Authorization;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>One SQL-translatable read policy, applied before count, totals and paging.</summary>
internal static class BusinessDataReadScopeQuery
{
    internal static string GrantActor471(BmsDbContext db,string actor)=>db.ActiveRoleSelection?.Substitution471 is {} proof
        && proof.DeputyActorExternalId==actor?proof.PrincipalActorExternalId:actor;
    internal static IQueryable<Guid> EmployeeIds(BmsDbContext db, string actorExternalId)
    {
        if (db.ActiveRoleSelection?.Substitution471 is { } proof)
            return db.ActiveRoleSelection.ActorExternalId == actorExternalId
                && actorExternalId == proof.DeputyActorExternalId
                && db.ActiveRoleSelection.ActiveRoleCode == proof.RoleCode
                ? StaffSubstitutionGuard471.LivePrincipal(db, proof)
                : db.Employees.Where(_ => false).Select(e => e.Id);
        var matching = db.EmployeeActorMappings.AsNoTracking()
            .Where(mapping => mapping.ActorExternalId == actorExternalId);
        // BIN2 equality still pads trailing spaces in SQL Server. Do not let a
        // padded or contradictory mapping obtain authority the canonical
        // EmployeeActorResolver would reject. Count all matches, not only active ones.
        var exact = db.Database.IsSqlServer()
            ? matching.Where(mapping => EF.Functions.DataLength(mapping.ActorExternalId)
                == EF.Functions.DataLength(actorExternalId)
                && EF.Functions.Collate(mapping.ActorExternalId, "Latin1_General_100_BIN2") == actorExternalId)
            : matching;
        var employees = from mapping in exact
               join employee in db.Employees.AsNoTracking() on mapping.EmployeeId equals employee.Id
               where matching.Count() == 1 && mapping.IsActive && employee.IsActive && employee.Id != Guid.Empty
               select employee.Id;
        if (db.ActiveRoleSelection is { } selection)
        {
            if (!selection.IsInitialized || selection.ActorExternalId != actorExternalId
                || selection.EmployeeId is not Guid selectedEmployee)
                return employees.Where(_ => false);
            employees = employees.Where(id => id == selectedEmployee);
        }
        return employees;
    }

    internal static IQueryable<string> RoleCodes(BmsDbContext db, IQueryable<Guid> employeeIds)
        => BuildRoleCodes(db, employeeIds, effectiveCurrentActor: true);

    // Assignment inventory is not current-actor authority. Only route planning
    // and membership readers behind an already-proved principal may use it.
    internal static IQueryable<string> AssignedRoleCodes(BmsDbContext db, IQueryable<Guid> employeeIds)
        => BuildRoleCodes(db, employeeIds, effectiveCurrentActor: false);

    private static IQueryable<string> BuildRoleCodes(BmsDbContext db, IQueryable<Guid> employeeIds,
        bool effectiveCurrentActor)
    {
        var roles = from assignment in db.EmployeeFunctionalRoles.AsNoTracking()
                    join role in db.FunctionalRoles.AsNoTracking() on assignment.FunctionalRoleId equals role.Id
                    where employeeIds.Contains(assignment.EmployeeId) && assignment.IsActive && role.IsActive && role.Id != Guid.Empty
                    select new { Role = role, assignment.EmployeeId };
        if (effectiveCurrentActor && db.ActiveRoleSelection is { } selection)
        {
            // Scope current-actor SQL joins too: list/count/totals/pagination
            // and CFO creator scopes must not silently reconstruct the union.
            if (!selection.IsInitialized || selection.EmployeeId is not Guid currentEmployeeId)
                return roles.Where(_ => false).Select(item => item.Role.Code);
            var selectedCodes = selection.ActiveRoleCode is { } selected
                ? new[] { selected } : Array.Empty<string>();
            roles = roles.Where(item => item.EmployeeId == currentEmployeeId
                && selectedCodes.Contains(item.Role.Code));
        }
        // SQL Server's default case-insensitive collation must not turn an
        // unknown/wrong-case code into a canonical functional role.
        return db.Database.IsSqlServer()
            ? roles.Where(item => EF.Functions.DataLength(item.Role.Code) == item.Role.Code.Length * 2)
                .Select(item => EF.Functions.Collate(item.Role.Code, "Latin1_General_100_BIN2"))
            : roles.Select(item => item.Role.Code);
    }

    internal static IQueryable<Guid> OwnedCostCenterIds(BmsDbContext db, Guid employeeId) =>
        OwnedCostCenterIds(db, db.Employees.AsNoTracking().Where(employee =>
            employee.Id == employeeId && employee.IsActive).Select(employee => employee.Id));

    internal static IQueryable<Guid> AssignedCreatorCostCenterIds(BmsDbContext db, Guid employeeId) =>
        OwnedCostCenterIds(db, db.Employees.AsNoTracking().Where(employee =>
            employee.Id == employeeId && employee.IsActive).Select(employee => employee.Id), inventory: true);

    private static IQueryable<Guid> OwnedCostCenterIds(BmsDbContext db, IQueryable<Guid> employees, bool inventory = false)
    {
        var roles = inventory ? AssignedRoleCodes(db, employees) : RoleCodes(db, employees);
        var owned = from assignment in db.BudgetOwnerCostCenterAssignments.AsNoTracking()
               join center in db.CostCenters.AsNoTracking() on assignment.CostCenterId equals center.Id
               where employees.Count() == 1 && employees.Contains(assignment.EmployeeId)
                   && roles.Any(code => code == ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode)
                   && assignment.IsActive && center.IsActive && !center.IsGroup && center.Id != Guid.Empty
               select center.Id;
        var initiated = from assignment in db.CfoInitiatorCostCenterAssignments.AsNoTracking()
                        join center in db.CostCenters.AsNoTracking() on assignment.CostCenterId equals center.Id
                        where employees.Count() == 1 && employees.Contains(assignment.EmployeeId)
                            && roles.Any(code => code == ExpenseRequestWorkflowCodes.CfoInitiatorRoleCode)
                            && assignment.IsActive && center.IsActive && !center.IsGroup && center.Id != Guid.Empty
                        select center.Id;
        return owned.Union(initiated);
    }

    internal static IQueryable<Guid> VisibleCostCenterIds(BmsDbContext db, string actorExternalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorExternalId);
        var employees = EmployeeIds(db, actorExternalId);
        var globalCodes = BusinessDataReadPolicy.GlobalRoleCodes.ToArray();
        var roles = RoleCodes(db, employees);
        var owned = OwnedCostCenterIds(db, employees);
        return db.CostCenters.AsNoTracking().Where(center => employees.Count() == 1
            && !center.IsGroup && center.Id != Guid.Empty
            && (roles.Any(code => globalCodes.Contains(code)) || owned.Contains(center.Id)))
            .Select(center => center.Id);
    }

    internal static IQueryable<ExpenseRequest> ApplyRequests(BmsDbContext db,
        IQueryable<ExpenseRequest> source, string actorExternalId, bool includeNsiQueue = true)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorExternalId);
        var employees = EmployeeIds(db, actorExternalId);
        var roles = RoleCodes(db, employees);
        var globalCodes = BusinessDataReadPolicy.GlobalRoleCodes.ToArray();
        var owned = OwnedCostCenterIds(db, employees);
        var nsiOrganizations = NsiOrganizationIds(db, actorExternalId);
        var payrollRequests = PayrollRequestIds372(db, actorExternalId);
        return source.Where(request => employees.Count() == 1
            && (roles.Any(code => globalCodes.Contains(code))
                || (request.DefaultCostCenterId.HasValue && owned.Contains(request.DefaultCostCenterId.Value)
                    && !request.Lines.Any(line => line.CostCenterId.HasValue
                        && !owned.Contains(line.CostCenterId.Value)))
                || (includeNsiQueue && request.Status == ExpenseRequestStatus.AwaitingNsi && request.OrganizationId.HasValue
                    && nsiOrganizations.Contains(request.OrganizationId.Value))
                || payrollRequests.Contains(request.Id)));
    }

    /// <summary>
    /// Read-only application of authority resolved inside the same Serializable
    /// attempt. Do not cache this scope across reads or use it as write authority.
    /// Retains the exact header/every-explicit-line policy without expanding
    /// employee/role/assignment subqueries once per exported financial row.
    /// </summary>
    internal static IQueryable<ExpenseRequest> ApplyCapturedRequests(
        IQueryable<ExpenseRequest> source, BusinessDataReadScope capturedScope)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(capturedScope);
        if (capturedScope.CanReadAll) return source;
        var owned = capturedScope.CostCenterIds.ToArray();
        var nsiOrganizations = capturedScope.NsiOrganizationIds.ToArray();
        var payrollRequests = capturedScope.PayrollRequestIds.ToArray();
        if (owned.Length == 0 && nsiOrganizations.Length == 0 && payrollRequests.Length == 0) return source.Where(_ => false);
        return source.Where(request => (request.DefaultCostCenterId.HasValue
            && owned.Contains(request.DefaultCostCenterId.Value)
            && !request.Lines.Any(line => line.CostCenterId.HasValue
                && !owned.Contains(line.CostCenterId.Value)))
            || (request.Status == ExpenseRequestStatus.AwaitingNsi && request.OrganizationId.HasValue
                && nsiOrganizations.Contains(request.OrganizationId.Value))
            || payrollRequests.Contains(request.Id));
    }

    internal static IQueryable<Guid> PayrollRequestIds372(BmsDbContext db, string actorExternalId)
    {
        var employees = EmployeeIds(db, actorExternalId);
        var roles = RoleCodes(db, employees);
        // A returned request may later change operation. Historical payroll
        // participation must never expose that replacement supplier document.
        var advances = db.ExpenseRequests.FromSqlRaw("""
            SELECT [request].* FROM bms_web.ExpenseRequests AS [request]
            WHERE JSON_VALUE([request].[PaymentOperationJson], '$.kind') COLLATE Latin1_General_100_BIN2 = N'PayrollAdvance'
            """).AsNoTracking().Select(r => r.Id);
        return (from participant in db.ApprovalRouteStageParticipants.AsNoTracking()
            join stage in db.ApprovalRouteStages.AsNoTracking()
                on new { participant.ExpenseRequestId, participant.SnapshotNumber, participant.StageNumber }
                equals new { stage.ExpenseRequestId, stage.SnapshotNumber, stage.StageNumber }
            join snapshot in db.ApprovalRouteSnapshots.AsNoTracking()
                on new { stage.ExpenseRequestId, stage.SnapshotNumber } equals new { snapshot.ExpenseRequestId, snapshot.SnapshotNumber }
            where employees.Count() == 1 && employees.Contains(participant.EmployeeId)
                && advances.Contains(participant.ExpenseRequestId)
                && roles.Contains(PayrollAdvanceWorkflow372.CalculatorRole) && stage.RoleCode == PayrollAdvanceWorkflow372.CalculatorRole
                && snapshot.WorkflowDefinitionVersion >= 14 && snapshot.WorkflowDefinitionVersion <= 21
            select participant.ExpenseRequestId).Distinct();
    }

    internal static IQueryable<Guid> NsiOrganizationIds(BmsDbContext db, string actorExternalId)
    {
        var employees = EmployeeIds(db, actorExternalId);
        var grantActor = GrantActor471(db,actorExternalId);
        var roles = RoleCodes(db, employees);
        return (from grant in db.ActorOrganizationGrants.AsNoTracking()
            join organization in db.Organizations.AsNoTracking() on grant.OrganizationId equals organization.Id
            where employees.Count() == 1 && roles.Any(code => code == ExpenseRequestWorkflowCodes.NsiSpecialistRoleCode)
                && grant.ActorExternalId == grantActor && grant.IsActive && organization.IsActive
            select organization.Id).Distinct();
    }

    internal static async Task<BusinessDataReadScope> ResolveAsync(BmsDbContext db,
        string actorExternalId, CancellationToken cancellationToken)
    {
        var employees = EmployeeIds(db, actorExternalId);
        var ids = await employees.Take(2).ToArrayAsync(cancellationToken);
        if (ids.Length > 1) throw new BudgetOwnerCreationAuthorityUnavailableException();
        if (ids.Length == 0) return BusinessDataReadScope.None;
        var globalCodes = BusinessDataReadPolicy.GlobalRoleCodes.ToArray();
        if (await RoleCodes(db, employees).AnyAsync(code => globalCodes.Contains(code), cancellationToken))
            return BusinessDataReadScope.All;
        return new BusinessDataReadScope(false,
            await OwnedCostCenterIds(db, employees).Distinct().ToArrayAsync(cancellationToken),
            await NsiOrganizationIds(db, actorExternalId).ToArrayAsync(cancellationToken),
            await PayrollRequestIds372(db, actorExternalId).ToArrayAsync(cancellationToken));
    }
}

public sealed class PayrollRequestScope372(BmsDbContext db) : Bms.Application.Abstractions.IPayrollRequestScope372
{
    public async Task<IReadOnlyList<Guid>> ReadAsync(string actorExternalId, CancellationToken token) =>
        await BusinessDataReadScopeQuery.PayrollRequestIds372(db, actorExternalId).ToArrayAsync(token);
}

public sealed class NsiWorkQueueScopeReader(BmsDbContext db) : Bms.Application.Abstractions.INsiWorkQueueScopeReader
{
    public async Task<IReadOnlyList<Guid>> ReadOrganizationIdsAsync(string actorExternalId, CancellationToken cancellationToken)
        => await BusinessDataReadScopeQuery.NsiOrganizationIds(db, actorExternalId).ToArrayAsync(cancellationToken);
}
