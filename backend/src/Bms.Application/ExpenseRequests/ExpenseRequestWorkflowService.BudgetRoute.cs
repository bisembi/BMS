using System.Text.Json;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Budgeting;
using Bms.Domain.Workflow;

namespace Bms.Application.ExpenseRequests;

public sealed partial class ExpenseRequestWorkflowService
{
    // Maintenance only: no HTTP endpoint or new workflow command is registered.
    // All existing decisions and immutable snapshots remain in their original
    // form. Queued payments and paid/closed documents are never reopened.
    public Task<ExpenseRequestDto> RecalculateBudgetRouteAsync(Guid id, Guid expectedVersion, CancellationToken token)
    {
        var actor = ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (currentActor.IsBmsAdministrator || currentActor.ActiveRoleCode != "FIN_DIRECTOR") throw new ActorForbiddenException();
        return transactionRunner.ExecuteAsync(async ct =>
        {
            await RequireRoleAsync(actor, "FIN_DIRECTOR", ct);
            var request = await repository.FindForWorkflowUpdateAsync(id, ct) ?? throw new ExpenseRequestNotFoundException();
            if (request.ConcurrencyStamp != expectedVersion) throw new ExpenseRequestVersionConflictException();
            if (request.Status is not (ExpenseRequestStatus.InApproval or ExpenseRequestStatus.Approved or ExpenseRequestStatus.AwaitingNsi))
                throw new ExpenseRequestWorkflowUnavailableException();
            var projections = await budgetRepository.ListRequestProjectionsAsync([request.Id], ct);
            var current = SubmissionBudgetPreflightEvaluator.Evaluate(await budgetSnapshotReader.ReadAsync(request.Id, projections, ct));
            if (!current.IsReady || current.PositiveDeficitKzt is not { } deficit) throw new ExpenseRequestWorkflowUnavailableException();
            var old = request.ActiveApprovalRouteSnapshotNumber is { } active
                ? await approvalRouteSnapshotStore.FindAsync(request.Id, active, ct)
                : await approvalRouteSnapshotStore.FindLatestAsync(request.Id, ct);
            if (old is null || old.OrganizationId != request.OrganizationId || old.CostCenterId != request.DefaultCostCenterId)
                throw new ExpenseRequestWorkflowUnavailableException();
            _ = await ReadVerifiedWorkflowAsync(ct, old.WorkflowDefinitionVersion);
            var required = ApprovalRoutePlanner.Plan(deficit, old.WorkflowDefinitionVersion);
            if (required.All(stage => old.Stages.Any(row => row.StageNumber == stage.StageNumber && row.RoleCode == stage.RoleCode)))
                return ExpenseRequestDto.FromDomain(request, projections);
            if (old.Stages.Count > required.Count || old.Stages.Where((stage, index) =>
                stage.StageNumber != required[index].StageNumber || stage.RoleCode != required[index].RoleCode).Any())
                throw new ExpenseRequestWorkflowUnavailableException();
            var author = await employeeActorResolver.ResolveAsync(request.InitiatorExternalId, ct);
            if (author.EmployeeId is not { } authorId) throw new ExpenseRequestWorkflowUnavailableException();
            var finance = required.Where(stage => stage.RoleCode != "BUDGET_OWNER")
                .Select((stage, index) => new ApprovalRouteStage(index + 1, stage.RoleCode)).ToArray();
            var resolved = finance.Length == 1 && finance[0].RoleCode == "GENERAL_DIRECTOR"
                ? await approvalRouteParticipantResolver.ResolveGeneralDirectorAsync(ct)
                : await approvalRouteParticipantResolver.ResolveConfiguredAsync(finance, ct);
            var byRole = finance.Zip(resolved).ToDictionary(pair => pair.First.RoleCode,
                pair => pair.Second.EligibleEmployeeIds.Where(employee => employee != authorId).ToArray());
            var participants = required.Select(stage => new ApprovalRouteSnapshotStageParticipants(stage.StageNumber,
                stage.StageNumber <= old.Stages.Count ? old.Stages[stage.StageNumber - 1].EligibleEmployeeIds
                    : byRole.GetValueOrDefault(stage.RoleCode) ?? [])).ToArray();
            if (participants.Any(stage => !stage.EligibleEmployeeIds.Any())) throw new ExpenseRequestWorkflowUnavailableException();
            var number = await approvalRouteSnapshotStore.NextSnapshotNumberAsync(request.Id, ct);
            var now = clock.UtcNow;
            var next = ApprovalRouteSnapshot.Create(request.Id, number, old.WorkflowDefinitionVersion, old.OrganizationId,
                old.CostCenterId, deficit, now, participants);
            var before = ExpenseRequestDto.FromDomain(request, projections);
            var stageNumber = request.Status == ExpenseRequestStatus.InApproval ? request.ActiveApprovalRouteStageNumber : old.Stages.Count + 1;
            approvalRouteSnapshotStore.Add(next);
            await approvalRouteSnapshotStore.CaptureCompanyBudgetBaselineAsync(request.Id, number, ct);
            request.ApplyWorkflowTransition(request.ConcurrencyStamp, now, request.Status, ExpenseRequestStatus.InApproval, number, stageNumber);
            var after = ExpenseRequestDto.FromDomain(request, projections);
            var reason = "Пересчёт: бюджет минус факт 1С минус неоплаченные остатки заявок. "
                + $"Маршрут {old.SnapshotNumber} → {number}. Прежние решения сохранены; новые этапы не согласованы автоматически.";
            auditSink.Append(Guid.NewGuid(), now, actor, "ExpenseRequest", id.ToString("D"), "BudgetRouteRecalculated",
                JsonSerializer.Serialize(before, JsonOptions), JsonSerializer.Serialize(new { CommandCode = "RECALCULATE_BUDGET_ROUTE", Reason = reason, ExpenseRequest = after }, JsonOptions), Guid.NewGuid().ToString("N"));
            await unitOfWork.SaveChangesAsync(ct);
            return after;
        }, token);
    }
}
