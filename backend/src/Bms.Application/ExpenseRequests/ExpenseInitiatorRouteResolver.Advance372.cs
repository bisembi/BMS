using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;

namespace Bms.Application.ExpenseRequests;

public sealed partial class ExpenseInitiatorRouteResolver
{
    private async Task<int> AdvanceVersion372Async(ExpenseRequest request, CancellationToken token)
    {
        if (request.PaymentOperation?.IsPayrollAdvance != true || functionalRoles is null
            || request.DefaultCostCenterId is not Guid center) throw new ActorForbiddenException();
        var original = await actors.ResolveAsync(request.InitiatorExternalId, token);
        if (!original.HasActiveEmployee || original.EmployeeId is not Guid employee) throw new ActorForbiddenException();
        var assigned = await functionalRoles.ResolveAssignedAsync(employee, token);
        var owns = await authority.IsOwnerForAuthorizedPrincipalAsync(employee, center, token);
        if (!owns && !await authority.RequiresOwnerForAuthorizedPrincipalAsync(employee, center, token)) throw new ActorForbiddenException();
        return PayrollAdvanceWorkflow372.Version(!owns, assigned.HasExactRole("FIN_DIRECTOR"),
            assigned.HasExactRole(PayrollAdvanceWorkflow372.CalculatorRole));
    }
    private async Task<IReadOnlyList<ApprovalRouteSnapshotStageParticipants>> ResolveAdvance372Async(
        ExpenseRequest request, int version, string submitterExternalId, CancellationToken token)
    {
        if (await AdvanceVersion372Async(request, token) != version) throw new ActorForbiddenException();
        var original = await actors.ResolveAsync(request.InitiatorExternalId, token);
        var submitter = await actors.ResolveAsync(submitterExternalId, token);
        if (original.EmployeeId is not Guid author || submitter.EmployeeId is not Guid deputy) throw new ActorForbiddenException();
        var stages = PayrollAdvanceWorkflow372.Plan(version);
        var finance = stages.Where(s => s.RoleCode != "BUDGET_OWNER").Select((s, i) => new ApprovalRouteStage(i + 1, s.RoleCode)).ToArray();
        var resolved = finance.Length == 0 ? [] : await financial.ResolveConfiguredAsync(finance, token);
        var byRole = finance.Zip(resolved).ToDictionary(p => p.First.RoleCode, p => p.Second.EligibleEmployeeIds);
        var result = new List<ApprovalRouteSnapshotStageParticipants>();
        foreach (var stage in stages)
        {
            var ids = stage.RoleCode == "BUDGET_OWNER"
                ? await authority.ReadOwnersAsync(request.DefaultCostCenterId!.Value, author, token) : byRole[stage.RoleCode];
            var independent = ids.Where(id => id != author && id != deputy).ToArray();
            if (independent.Length == 0) throw new DomainRuleViolationException("Assign an independent approver for the advance route.");
            result.Add(new(stage.StageNumber, independent));
        }
        return result;
    }
}
