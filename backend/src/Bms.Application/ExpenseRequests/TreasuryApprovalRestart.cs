using Bms.Application.Abstractions;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;

namespace Bms.Application.ExpenseRequests;

/// <summary>Capture a NEW route for a treasury month change. Historical
/// approvals, amounts and the initiator's indicative date are never edited.</summary>
public sealed class TreasuryApprovalRestart(ExpenseInitiatorRouteResolver routes,
    IApprovalRouteSnapshotStore snapshots, IExpenseRequestWorkflowDefinitionReader definitions)
{
    public async Task<bool> DeficitHasIncreasedAsync(ExpenseRequest request,decimal deficit,CancellationToken token)
    {
        var previous=await snapshots.FindLatestAsync(request.Id,token)
            ?? throw new ExpenseRequestWorkflowUnavailableException();
        return deficit>previous.PositiveDeficitKzt;
    }

    public async Task RestartAsync(ExpenseRequest request, decimal deficit, DateTimeOffset now, CancellationToken token)
    {
        var resolved=await routes.ResolveTreasuryRescheduleAsync(request,deficit,token);
        var source=await definitions.ReadVersionAsync(resolved.Version,token);
        if(!ExpenseRequestWorkflowDefinition251.TryCreate(resolved.Version,source.Statuses,source.Commands,
            source.Permissions,out var definition) || definition is null || resolved.Participants.Count==0
            || request.OrganizationId is not Guid organization || request.DefaultCostCenterId is not Guid center)
            throw new ExpenseRequestWorkflowUnavailableException();
        var number=await snapshots.NextSnapshotNumberAsync(request.Id,token);
        snapshots.Add(ApprovalRouteSnapshot.Create(request.Id,number,resolved.Version,organization,center,
            deficit,now,resolved.Participants));
        await snapshots.CaptureCompanyBudgetBaselineAsync(request.Id,number,token);
        request.ApplyWorkflowTransition(request.ConcurrencyStamp,now,request.Status,ExpenseRequestStatus.InApproval,number,1);
    }
}
