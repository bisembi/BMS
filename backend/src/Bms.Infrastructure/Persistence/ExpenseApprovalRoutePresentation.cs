using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class ExpenseApprovalRoutePresentation(BmsDbContext db, ICurrentActor actor,
    IExpenseRequestRepository requests, IApprovalRouteSnapshotStore snapshots,
    ExpenseInitiatorRouteResolver routes, IBusinessTransactionRunner transactions) : IExpenseApprovalRoutePresentation
{
    public async Task<ExpenseApprovalRoutePresentationDto> BuildDraftAsync(ExpenseRequest draft, decimal deficit, CancellationToken token)
    {
        try
        {
            int? version = draft.InitiatorExternalId == actor.ExternalId
                ? await routes.SubmissionVersionAsync(draft, token, ExpenseRequestActionAuthority.Direct(actor.ExternalId), deficit)
                : await routes.ReadOnlyPreviewVersionAsync(draft, token);
            if (version is null) return Unavailable(draft, true, "AUTHOR_ROLE_REQUIRED");
            // Participant resolution uses the original author's identity solely for
            // display/self-approval exclusion. No submit command or authority is granted.
            var participants = await routes.ResolveAsync(draft, version.Value, deficit, draft.InitiatorExternalId, token);
            var planned = ApprovalRoutePlanner.Plan(deficit, version.Value);
            var stages = await PresentAsync(planned.Select(stage => (stage.StageNumber, stage.RoleCode,
                participants.Single(p => p.StageNumber == stage.StageNumber).EligibleEmployeeIds)), token);
            return new(draft.Id, draft.ConcurrencyStamp, draft.Status.ToCode(), true, true, 0, stages,
                await ReadTreasurerNamesAsync(token), NsiSpecialistNames: await ReadNsiNamesAsync(draft, false, token));
        }
        catch (OperationCanceledException) { throw; }
        catch (ActorForbiddenException) { return Unavailable(draft, true); }
        catch (Bms.Domain.Common.DomainRuleViolationException) { return Unavailable(draft, true); }
        catch (ApprovalRouteParticipantResolutionUnavailableException) { return Unavailable(draft, true); }
        catch (Exception) { return Unavailable(draft, true); }
    }

    public Task<ExpenseApprovalRoutePresentationDto?> ReadAsync(Guid requestId, CancellationToken token)
    {
        var identity = ActorIdentity.RequireBusinessDataAccess(actor);
        return transactions.ExecuteAsync<ExpenseApprovalRoutePresentationDto?>(async ct =>
        {
            var scope = await actor.ResolveBusinessDataReadScopeAsync(ct);
            var request = await requests.FindVisibleAsync(requestId, identity, actor.HasFullBusinessDataAccess, ct);
            if (request is null || !scope.Contains(request)) return null;
            if (request.Status.IsEditableDraft()) return request.PaymentOperation?.IsPayrollAdvance == true
                ? await BuildDraftAsync(request, 0m, ct) : Unavailable(request, true);
            var snapshot = request.ActiveApprovalRouteSnapshotNumber is int number
                ? await snapshots.FindAsync(requestId, number, ct) : await snapshots.FindLatestAsync(requestId, ct);
            if (snapshot is null && request.Status != ExpenseRequestStatus.AwaitingNsi) return Unavailable(request, false);
            var stages = snapshot is null ? Array.Empty<ExpenseApprovalStagePresentation>()
                : await PresentAsync(snapshot.Stages.Select(stage =>
                    (stage.StageNumber, stage.RoleCode, (IEnumerable<Guid>)stage.EligibleEmployeeIds)), ct);
            return new(request.Id, request.ConcurrencyStamp, request.Status.ToCode(), false, true,
                request.Status == ExpenseRequestStatus.InApproval ? request.ActiveApprovalRouteStageNumber : 0, stages,
                await ReadTreasurerNamesAsync(ct), NsiSpecialistNames: await ReadNsiNamesAsync(request, true, ct));
        }, token);
    }

    private static ExpenseApprovalRoutePresentationDto Unavailable(ExpenseRequest request, bool preview, string? reason = null) =>
        new(request.Id, request.ConcurrencyStamp, request.Status.ToCode(), preview, false, 0, [], UnavailableReason: reason);

    // Presentation only: treasury recipients are not an approval stage and are
    // never written into immutable route snapshots or used as authorization.
    internal IQueryable<string> TreasurerNamesQuery() =>
        (from employee in db.Employees.AsNoTracking()
         join assignment in db.EmployeeFunctionalRoles.AsNoTracking() on employee.Id equals assignment.EmployeeId
         join role in db.FunctionalRoles.AsNoTracking() on assignment.FunctionalRoleId equals role.Id
         where employee.IsActive && assignment.IsActive && role.IsActive
             && role.Code == ExpenseRequestWorkflowCodes.TreasurerRoleCode
         select employee.Name).Distinct();

    private async Task<IReadOnlyList<string>> ReadTreasurerNamesAsync(CancellationToken token) =>
        (await TreasurerNamesQuery().ToArrayAsync(token)).OrderBy(name => name, StringComparer.Ordinal).ToArray();

    // NSI is the existing conditional master-data handoff, not a financial approval
    // stage. Show its current organization-scoped recipients before treasury without
    // modifying captured approvals, workflow permissions, request state or reserves.
    internal IQueryable<string> NsiNamesQuery(Guid? organizationId) =>
        (from employee in db.Employees.AsNoTracking()
         join assignment in db.EmployeeFunctionalRoles.AsNoTracking() on employee.Id equals assignment.EmployeeId
         join role in db.FunctionalRoles.AsNoTracking() on assignment.FunctionalRoleId equals role.Id
         join mapping in db.EmployeeActorMappings.AsNoTracking() on employee.Id equals mapping.EmployeeId
         join grant in db.ActorOrganizationGrants.AsNoTracking() on mapping.ActorExternalId equals grant.ActorExternalId
         join organization in db.Organizations.AsNoTracking() on grant.OrganizationId equals organization.Id
         where organizationId.HasValue && organization.Id == organizationId.Value
             && employee.IsActive && assignment.IsActive && role.IsActive && mapping.IsActive && grant.IsActive && organization.IsActive
             && role.Code == ExpenseRequestWorkflowCodes.NsiSpecialistRoleCode
             && db.EmployeeActorMappings.Count(m => m.EmployeeId == employee.Id && m.IsActive) == 1
             && db.EmployeeActorMappings.Count(m => m.ActorExternalId == mapping.ActorExternalId) == 1
         select employee.Name).Distinct();

    internal static bool NeedsNsiPresentation(bool newCounterparty, bool newContract, ExpenseRequestStatus status, bool confirmed) =>
        newCounterparty || newContract || status == ExpenseRequestStatus.AwaitingNsi || confirmed;

    private async Task<IReadOnlyList<string>?> ReadNsiNamesAsync(ExpenseRequest request, bool includeHistory, CancellationToken token)
    {
        var confirmed = includeHistory && await db.BusinessAuditEntries.AsNoTracking().AnyAsync(a =>
            a.ObjectType == "ExpenseRequest" && a.ObjectId == request.Id.ToString() && a.Action == "WorkflowNsiConfirmed", token);
        if (!NeedsNsiPresentation(request.IsNewCounterparty, request.IsNewContract, request.Status, confirmed)) return null;
        return (await NsiNamesQuery(request.OrganizationId).ToArrayAsync(token)).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private async Task<IReadOnlyList<ExpenseApprovalStagePresentation>> PresentAsync(
        IEnumerable<(int Number, string Role, IEnumerable<Guid> Employees)> rows, CancellationToken token)
    {
        var stages = rows.ToArray();
        var ids = stages.SelectMany(row => row.Employees).Distinct().ToArray();
        // Include inactive employees for historical snapshots; their eligibility is never recalculated.
        var names = await db.Employees.AsNoTracking().Where(employee => ids.Contains(employee.Id))
            .Select(employee => new { employee.Id, employee.Name }).ToDictionaryAsync(employee => employee.Id, employee => employee.Name, token);
        return stages.Select(row => new ExpenseApprovalStagePresentation(row.Number, row.Role,
            row.Employees.Select(id => names.GetValueOrDefault(id, "Сотрудник недоступен"))
                .OrderBy(name => name, StringComparer.Ordinal).ToArray())).ToArray();
    }
}
