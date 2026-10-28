using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;

namespace Bms.Application.ExpenseRequests;

public sealed partial class ExpenseInitiatorRouteResolver(EmployeeActorResolver actors, CfoWorkflowAuthority authority,
    IApprovalRouteSnapshotStore snapshots, ApprovalRouteParticipantResolver financial, ICurrentActor? currentActor = null,
    EmployeeFunctionalRoleResolver? functionalRoles = null)
{
    /// <summary>Read-only classification for an already-visible other author's draft.
    /// Never authorizes submission and never infers the viewer's senior-role exception.</summary>
    public async Task<int?> ReadOnlyPreviewVersionAsync(ExpenseRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Status.IsEditableDraft() || functionalRoles is null)
            throw new ExpenseRequestWorkflowUnavailableException();
        if (request.PaymentOperation?.IsPayrollAdvance == true) return await AdvanceVersion372Async(request, token);
        var original = await actors.ResolveAsync(request.InitiatorExternalId, token);
        if (!original.HasActiveEmployee || original.EmployeeId is not Guid employee
            || request.DefaultCostCenterId is not Guid center) throw new ActorForbiddenException();
        var assigned = await functionalRoles.ResolveAssignedAsync(employee, token);
        // A senior author's selected submission role is not persisted in a draft.
        // Tell the reader it is pending, instead of guessing a shortened route.
        if (assigned.HasExactRole(ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode)
            || assigned.HasExactRole(ExpenseRequestWorkflowCodes.GeneralDirectorRoleCode)) return null;
        var owns = await authority.IsOwnerForAuthorizedPrincipalAsync(employee, center, token);
        if (assigned.HasExactRole(ExpenseRequestWorkflowCodes.FinancialControllerRoleCode)) return owns ? 12 : 13;
        if (!owns && !await authority.RequiresOwnerForAuthorizedPrincipalAsync(employee, center, token))
            throw new ActorForbiddenException();
        return owns ? 6 : 7;
    }

    public async Task<(int Version, IReadOnlyList<ApprovalRouteSnapshotStageParticipants> Participants)> ResolveTreasuryRescheduleAsync(
        ExpenseRequest request, decimal deficit, CancellationToken token)
    {
        // This is a separate, explicitly treasury-authorized boundary, never a
        // boolean bypass on the ordinary initiator/deputy submission method.
        if (currentActor is not { IsAuthenticated: true, IsBmsAdministrator: false, CanAccessBusinessData: true }
            || currentActor.ActiveRoleCode != ExpenseRequestWorkflowCodes.TreasurerRoleCode || functionalRoles is null)
            throw new ActorForbiddenException();
        var treasurer = await actors.ResolveAsync(currentActor.ExternalId ?? string.Empty, token);
        if (!treasurer.HasActiveEmployee || treasurer.EmployeeId is not Guid treasuryEmployee
            || !(await functionalRoles.ResolveAsync(treasuryEmployee, token)).HasExactRole(ExpenseRequestWorkflowCodes.TreasurerRoleCode))
            throw new ActorForbiddenException();
        var original = await actors.ResolveAsync(request.InitiatorExternalId, token);
        if (!original.HasActiveEmployee || original.EmployeeId is not Guid employee
            || request.DefaultCostCenterId is not Guid center) throw new ActorForbiddenException();
        var latest = await snapshots.FindLatestAsync(request.Id, token)
            ?? throw new ExpenseRequestWorkflowUnavailableException();
        if (request.PaymentOperation?.IsPayrollAdvance == true)
        {
            var advanceVersion = await AdvanceVersion372Async(request, token);
            return (advanceVersion, await ResolveAdvance372Async(request, advanceVersion, request.InitiatorExternalId, token));
        }
        if (!ExpenseRequestWorkflowDefinition251.IsVersion(latest.WorkflowDefinitionVersion))
            throw new ExpenseRequestWorkflowUnavailableException();
        var assigned = await functionalRoles.ResolveAssignedAsync(employee, token);
        var owns = await authority.IsOwnerForAuthorizedPrincipalAsync(employee, center, token);
        var seniorFd = latest.WorkflowDefinitionVersion is 8 or 9
            && assigned.HasExactRole(ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode);
        var seniorGd = latest.WorkflowDefinitionVersion is 10 or 11
            && assigned.HasExactRole(ExpenseRequestWorkflowCodes.GeneralDirectorRoleCode);
        var controllerAuthor = latest.WorkflowDefinitionVersion is 12 or 13
            && assigned.HasExactRole(ExpenseRequestWorkflowCodes.FinancialControllerRoleCode);
        var version = seniorFd ? owns ? 8 : 9 : seniorGd ? owns ? 10 : 11 : controllerAuthor ? owns ? 12 : 13 : owns ? 6 : 7;
        var planned = ApprovalRoutePlanner.Plan(deficit, version);
        var configured = await financial.ResolveConfiguredAsync(planned.Where(s => s.RoleCode != "BUDGET_OWNER")
            .Select((s,i)=>new ApprovalRouteStage(i+1,s.RoleCode)).ToArray(),token);
        var independent = configured.Select(p=>new ApprovalRouteSnapshotStageParticipants(p.StageNumber,
            p.EligibleEmployeeIds.Where(id=>id!=employee).ToArray())).ToArray();
        if(independent.Any(p=>!p.EligibleEmployeeIds.Any()))
            throw new DomainRuleViolationException("Assign independent financial approvers before rescheduling.");
        if(owns) return(version,independent);
        var owners=await authority.ReadOwnersAsync(center,employee,token);
        if(owners.Count==0) throw new DomainRuleViolationException("Assign an independent owner before rescheduling.");
        return(version,new[]{new ApprovalRouteSnapshotStageParticipants(1,owners)}
            .Concat(independent.Select(p=>new ApprovalRouteSnapshotStageParticipants(p.StageNumber+1,p.EligibleEmployeeIds))).ToArray());
    }

    public async Task<int> SubmissionVersionAsync(ExpenseRequest request, CancellationToken token,
        ExpenseRequestActionAuthority? actionAuthority = null, decimal deficit = 0m)
    {
        if (currentActor?.UsesActiveRoleSelection == true
            && (actionAuthority?.IsAuthorized != true
                || actionAuthority.InitiatorExternalId != request.InitiatorExternalId
                || !await currentActor.CanCreateExpenseRequestAsync(token)))
            throw new ActorForbiddenException();
        if (request.PaymentOperation?.IsPayrollAdvance == true)
        {
            if (currentActor?.UsesActiveRoleSelection == true && currentActor.ActiveRoleCode is not ("BUDGET_OWNER" or "CFO_INITIATOR"))
                throw new ActorForbiddenException();
            return await AdvanceVersion372Async(request, token);
        }
        var original = await actors.ResolveAsync(request.InitiatorExternalId, token);
        if (!original.HasActiveEmployee || original.EmployeeId is not Guid employee
            || request.DefaultCostCenterId is not Guid center) throw new ActorForbiddenException();
        // Above the GD threshold the approved finance chain is FC -> FD -> GD,
        // or FC -> GD for a direct FD submission. Do not add a CFO-owner stage
        // or require the author to hold that CFO's owner role in this case.
        // Only NEW submissions are classified; active snapshots stay immutable.
        var owns = await authority.IsOwnerForAuthorizedPrincipalAsync(employee, center, token);
        if (await IsDirectFinancialDirectorAsync(request, employee, token)) return owns ? 8 : 9;
        if (await IsDirectFinancialDirectorAsync(request, employee, token, ExpenseRequestWorkflowCodes.GeneralDirectorRoleCode))
            return owns ? 10 : 11;
        if (await IsControllerAuthorAsync(employee, token)) return owns ? 12 : 13;
        return owns ? 6 : 7;
    }
    public async Task<int> CurrentVersionAsync(ExpenseRequest request, CancellationToken token)
    {
        var snapshot = request.ActiveApprovalRouteSnapshotNumber is int number && request.Status == ExpenseRequestStatus.InApproval
            ? await snapshots.FindAsync(request.Id, number, token) : await snapshots.FindLatestAsync(request.Id, token);
        if (request.Status == ExpenseRequestStatus.InApproval && snapshot is null)
            throw new ExpenseRequestWorkflowUnavailableException();
        return snapshot?.WorkflowDefinitionVersion ?? 1;
    }
    public async Task<IReadOnlyList<ApprovalRouteSnapshotStageParticipants>> ResolveAsync(ExpenseRequest request,
        int version, decimal deficit, string submitterExternalId, CancellationToken token)
    {
        if (PayrollAdvanceWorkflow372.IsVersion(version)) return await ResolveAdvance372Async(request, version, submitterExternalId, token);
        if (request.PaymentOperation?.IsPayrollAdvance == true) throw new ActorForbiddenException();
        var original = await actors.ResolveAsync(request.InitiatorExternalId, token);
        var submitter = await actors.ResolveAsync(submitterExternalId, token);
        if (original.EmployeeId is not Guid employee || submitter.EmployeeId is not Guid submittedBy)
            throw new ActorForbiddenException();
        if (ExpenseRequestWorkflowDefinition251.IsVersion(version))
        {
            if (version is 8 or 9 or 10 or 11)
            {
                var seniorRole = version is 8 or 9 ? ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode
                    : ExpenseRequestWorkflowCodes.GeneralDirectorRoleCode;
                if (submitterExternalId != request.InitiatorExternalId || employee != submittedBy
                    || !await IsDirectFinancialDirectorAsync(request, employee, token, seniorRole)) throw new ActorForbiddenException();
            }
            if (request.DefaultCostCenterId is not Guid costCenter) throw new ActorForbiddenException();
            var owns = await authority.IsOwnerForAuthorizedPrincipalAsync(employee, costCenter, token);
            if (owns != (version is 6 or 8 or 10 or 12)) throw new ActorForbiddenException();
            if (version is 12 or 13 && !await IsControllerAuthorAsync(employee, token)) throw new ActorForbiddenException();
            var planned = ApprovalRoutePlanner.Plan(deficit, version);
            if (planned.Count == 0) return [];
            var financialRoute = planned.Where(s => s.RoleCode != "BUDGET_OWNER")
                .Select((s, i) => new ApprovalRouteStage(i + 1, s.RoleCode)).ToArray();
            var resolved = financialRoute.Length == 0
                ? Array.Empty<ApprovalRouteSnapshotStageParticipants>()
                : await financial.ResolveConfiguredAsync(financialRoute, token);
            var independent251 = resolved.Select(p => new ApprovalRouteSnapshotStageParticipants(p.StageNumber,
                p.EligibleEmployeeIds.Where(id => id != employee).ToArray())).ToArray();
            if (independent251.Any(p => !p.EligibleEmployeeIds.Any()))
                throw new DomainRuleViolationException("Assign independent251 financial approvers before submission.");
            if (owns) return independent251;
            var owners251 = (await authority.ReadOwnersAsync(costCenter, employee, token)).Where(id => id != submittedBy).ToArray();
            if (owners251.Length == 0) throw new DomainRuleViolationException("Assign an independent251 owner of this CFO before submission.");
            return new[] { new ApprovalRouteSnapshotStageParticipants(1, owners251) }
                .Concat(independent251.Select(p => new ApprovalRouteSnapshotStageParticipants(p.StageNumber + 1, p.EligibleEmployeeIds))).ToArray();
        }
        if (version is 3 or 4)
        {
            // Re-prove this exception at capture time, not just during preview.
            if (submitterExternalId != request.InitiatorExternalId || employee != submittedBy
                || request.DefaultCostCenterId is not Guid ownedCenter
                || !await IsDirectFinancialDirectorAsync(request, employee, token)
                || ((version == 3 || deficit <= ApprovalRoutePlanner.GeneralDirectorThresholdKzt)
                    && !await authority.IsOwnerForAuthorizedPrincipalAsync(employee, ownedCenter, token)))
                throw new ActorForbiddenException();
            if (ApprovalRoutePlanner.Plan(deficit, version).Count == 0) return [];
            var seniorParticipants = version == 3 ? await financial.ResolveGeneralDirectorAsync(token)
                : await financial.ResolveSeniorAsync(token);
            var seniorIndependent = seniorParticipants.Select(p => new ApprovalRouteSnapshotStageParticipants(p.StageNumber,
                p.EligibleEmployeeIds.Where(id => id != employee).ToArray())).ToArray();
            if (seniorIndependent.Any(p => !p.EligibleEmployeeIds.Any()))
                throw new DomainRuleViolationException("Assign independent financial approvers before submission.");
            return seniorIndependent;
        }
        if (version == 5 && (submitterExternalId != request.InitiatorExternalId || employee != submittedBy
            || deficit > ApprovalRoutePlanner.GeneralDirectorThresholdKzt
            || !await IsDirectFinancialDirectorAsync(request, employee, token)))
            throw new ActorForbiddenException();
        var participants = await financial.ResolveAsync(
            version == 5 ? ApprovalRoutePlanner.Plan(0m) : ApprovalRoutePlanner.Plan(deficit), token);
        // The existing no-self-approval rule still applies on financial stages.
        // Fail before submission if there is nobody able to decide a stage.
        var independent = participants.Select(p => new ApprovalRouteSnapshotStageParticipants(p.StageNumber,
            p.EligibleEmployeeIds.Where(id => id != employee).ToArray())).ToArray();
        if (independent.Any(p => !p.EligibleEmployeeIds.Any()))
            throw new DomainRuleViolationException("Assign an independent financial approver before submission.");
        if (version == 1) return independent;
        if (version is not (2 or 5) || request.DefaultCostCenterId is not Guid center) throw new ExpenseRequestWorkflowUnavailableException();
        var owners = (await authority.ReadOwnersAsync(center, employee, token)).Where(id => id != submittedBy).ToArray();
        if (owners.Length == 0) throw new DomainRuleViolationException("Assign an independent owner of this CFO before submission.");
        return new[] { new ApprovalRouteSnapshotStageParticipants(1, owners) }
            .Concat(independent.Select(p => new ApprovalRouteSnapshotStageParticipants(p.StageNumber + 1, p.EligibleEmployeeIds))).ToArray();
    }

    private async Task<bool> IsDirectFinancialDirectorAsync(ExpenseRequest request, Guid employee, CancellationToken token,
        string roleCode = ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode) =>
        currentActor is { IsAuthenticated: true, IsBmsAdministrator: false, UsesActiveRoleSelection: true }
        && currentActor.ExternalId == request.InitiatorExternalId
        && currentActor.ActiveRoleCode == roleCode
        && await currentActor.CanCreateExpenseRequestAsync(token)
        && await authority.IsEffectiveFinancialDirectorAsync(employee, token, roleCode);

    private async Task<bool> IsControllerAuthorAsync(Guid employee, CancellationToken token) =>
        functionalRoles is not null
        && (await functionalRoles.ResolveAssignedAsync(employee, token))
            .HasExactRole(ExpenseRequestWorkflowCodes.FinancialControllerRoleCode);
}
