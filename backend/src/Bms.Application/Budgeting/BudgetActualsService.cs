using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Bms.Domain.Workflow;

namespace Bms.Application.Budgeting;

public sealed class BudgetActualsService(ICashFlowActualsStore actuals, IBudgetInstanceStore instances,
    IBudgetDirectoryStore directory, BudgetDocumentAuthority authority, IAccessScope accessScope,
    EmployeeActorResolver employees, EmployeeFunctionalRoleResolver roles, ICurrentActor actor,
    IBusinessTransactionRunner transactions, IClock clock)
{
    public async Task<BudgetActualsPreviewDto> PreviewAsync(PreviewBudgetActualsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        var months = CashFlowActualsSource.Months(command.FromMonth, command.ToMonth);
        if (command.CostCenterId == Guid.Empty || command.CashFlowItemIds is null
            || command.CashFlowItemIds.Count is < 1 or > 2000
            || command.CashFlowItemIds.Any(id => id == Guid.Empty)
            || command.CashFlowItemIds.Distinct().Count() != command.CashFlowItemIds.Count
            || command.BudgetInstanceId == Guid.Empty || command.ExpectedConcurrencyStamp == Guid.Empty
            || command.BudgetInstanceId.HasValue != command.ExpectedConcurrencyStamp.HasValue)
            throw new DomainRuleViolationException("Invalid actuals copy selection.");
        _ = await authority.RequireEmployeeAsync(actor, cancellationToken);
        return await transactions.ExecuteAsync(async token =>
        {
            if (!await directory.LockCostCenterAsync(command.CostCenterId, token)
                || !await instances.CostCenterIsActiveNonGroupAsync(command.CostCenterId, token))
                throw new ActorForbiddenException();
            var readScope = await actor.ResolveBusinessDataReadScopeAsync(token);
            if (!readScope.Contains(command.CostCenterId)) throw new ActorForbiddenException();
            var editor = await authority.ResolveEditorAsync(actor, command.CostCenterId, token);
            if (editor.EmployeeId is null) throw new ActorForbiddenException();
            if (!editor.HasFullBudgetAccess)
            {
                var ids = new[] { command.CostCenterId };
                var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
                if (!await accessScope.CanUseAsync(externalId, null, ids, today, token)) throw new ActorForbiddenException();
                foreach (var month in months)
                    if (!await accessScope.CanUseAsync(externalId, null, ids, month, token)) throw new ActorForbiddenException();
            }
            if (command.BudgetInstanceId is Guid id)
            {
                var instance = await instances.FindAsync(id, token);
                if (instance is null || instance.CostCenterId != command.CostCenterId) throw new BudgetInstanceNotFoundException();
                if (instance.Status != BudgetInstanceStatus.Draft) throw new ActorForbiddenException();
                if (await instances.IsInApprovalAsync(id, token)) throw new ActorForbiddenException();
                if (instance.ConcurrencyStamp != command.ExpectedConcurrencyStamp) throw new BudgetInstanceConcurrencyConflictException();
                // The owner's explicit 2025 exclusion applies to persisted target documents too.
                if (instance.ValidFrom.Year != command.FromMonth.Year || instance.ValidTo.Year != command.FromMonth.Year)
                    throw new DomainRuleViolationException("Only a draft in the selected source year can copy this snapshot.");
            }
            foreach (var cashFlowId in command.CashFlowItemIds)
                if (!await instances.CashFlowItemBelongsToCostCenterAsync(cashFlowId, command.CostCenterId, token))
                    throw new DomainRuleViolationException("Select only articles assigned to the budget cost center.");
            return await actuals.PreviewAsync(command, token);
        }, cancellationToken);
    }

    public async Task<CashFlowActualsImportResultDto> ImportAsync(ImportCashFlowActualsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        var employee = await employees.ResolveAsync(externalId, cancellationToken);
        var employeeId = employee.EmployeeId ?? throw new ActorForbiddenException();
        var assignments = await roles.ResolveAsync(employeeId, cancellationToken);
        if (!assignments.EmployeeExists || !assignments.EmployeeIsActive
            || (!HasCurrentAdministratorRole(assignments) && !assignments.HasExactRole(ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode)))
            throw new ActorForbiddenException();
        CashFlowActualsSource.Validate(command);
        if (CashFlowActualsSource.ImportYear(command.AvailableMonths) is not (2025 or 2026))
            throw new CashFlowActualsSourceInvalidException();
        if (command.CapturedAtUtc > clock.UtcNow.AddMinutes(5)) throw new CashFlowActualsSourceInvalidException();
        return await transactions.ExecuteAsync(async token =>
        {
            // Recheck server-assigned authority inside the same transaction as persistence.
            var currentEmployee = await employees.ResolveAsync(externalId, token);
            if (currentEmployee.EmployeeId != employeeId) throw new ActorForbiddenException();
            var currentRoles = await roles.ResolveAsync(employeeId, token);
            if (!currentRoles.EmployeeExists || !currentRoles.EmployeeIsActive
                || (!HasCurrentAdministratorRole(currentRoles) && !currentRoles.HasExactRole(ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode)))
                throw new ActorForbiddenException();
            var existing = await actuals.FindImportAsync(command.SourceHash, token);
            return existing is not null ? existing with { AlreadyImported = true }
                : await actuals.ImportAsync(command, employeeId, externalId, clock.UtcNow, token);
        }, cancellationToken);
    }

    private bool HasCurrentAdministratorRole(EmployeeFunctionalRoleResolution resolution) =>
        actor.HasBmsAdministratorAssignment
        && (resolution.HasExactRole("BMS_Администратор") || resolution.HasExactRole("BMS.Administrator"));
}
