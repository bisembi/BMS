using Bms.Application.Abstractions;
using Bms.Application.ReferenceData;
using Bms.Domain.Common;
using Bms.Domain.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class BudgetOwnerCostCenterAssignmentDirectoryWriter(BmsDbContext db, BudgetOwnerRoleSynchronizer ownerRoles)
    : IBudgetOwnerCostCenterAssignmentDirectoryWriter
{
    public async Task<BudgetOwnerCostCenterAssignmentDirectoryItemDto> CreateAsync(
        BudgetOwnerCostCenterAssignmentWriteCommand command, CancellationToken token)
    {
        EnsureSerializable();
        var employee = await db.Employees.SingleOrDefaultAsync(x => x.Code == command.EmployeeCode && x.IsActive, token)
            ?? throw new BudgetOwnerCostCenterAssignmentNotFoundException();
        var center = await db.CostCenters.SingleOrDefaultAsync(x => x.Code == command.CostCenterCode && x.IsActive && !x.IsGroup, token)
            ?? throw new BudgetOwnerCostCenterAssignmentNotFoundException();
        var existing = await db.BudgetOwnerCostCenterAssignments.SingleOrDefaultAsync(x => x.EmployeeId == employee.Id && x.CostCenterId == center.Id, token);
        if (existing is not null && existing.IsActive) throw new BudgetOwnerCostCenterAssignmentConflictException();

        // Scope assignment must never implicitly grant a functional role.
        await EnsureBudgetOwnerRoleAsync(employee.Id, token);
        if (existing is null) db.BudgetOwnerCostCenterAssignments.Add(new BudgetOwnerCostCenterAssignment { EmployeeId = employee.Id, CostCenterId = center.Id, IsActive = true });
        else existing.IsActive = true;
        return ToDto(employee, center);
    }

    public async Task<BudgetOwnerCostCenterAssignmentDirectoryItemDto> UpdateAsync(
        string employeeCode, string costCenterCode, BudgetOwnerCostCenterAssignmentWriteCommand command, CancellationToken token)
    {
        EnsureSerializable();
        var oldEmployee = await db.Employees.SingleOrDefaultAsync(x => x.Code == employeeCode, token) ?? throw new BudgetOwnerCostCenterAssignmentNotFoundException();
        var oldCenter = await db.CostCenters.SingleOrDefaultAsync(x => x.Code == costCenterCode, token) ?? throw new BudgetOwnerCostCenterAssignmentNotFoundException();
        var old = await db.BudgetOwnerCostCenterAssignments.SingleOrDefaultAsync(x => x.EmployeeId == oldEmployee.Id && x.CostCenterId == oldCenter.Id && x.IsActive, token)
            ?? throw new BudgetOwnerCostCenterAssignmentNotFoundException();
        var employee = await db.Employees.SingleOrDefaultAsync(x => x.Code == command.EmployeeCode && x.IsActive, token) ?? throw new BudgetOwnerCostCenterAssignmentNotFoundException();
        var center = await db.CostCenters.SingleOrDefaultAsync(x => x.Code == command.CostCenterCode && x.IsActive && !x.IsGroup, token) ?? throw new BudgetOwnerCostCenterAssignmentNotFoundException();
        if (await db.BudgetOwnerCostCenterAssignments.AnyAsync(x => x.EmployeeId == employee.Id && x.CostCenterId == center.Id && x.IsActive, token)
            && (employee.Id != old.EmployeeId || center.Id != old.CostCenterId)) throw new BudgetOwnerCostCenterAssignmentConflictException();

        await EnsureBudgetOwnerRoleAsync(employee.Id, token);
        old.IsActive = false;
        var replacement = await db.BudgetOwnerCostCenterAssignments.SingleOrDefaultAsync(x => x.EmployeeId == employee.Id && x.CostCenterId == center.Id, token);
        if (replacement is null) db.BudgetOwnerCostCenterAssignments.Add(new BudgetOwnerCostCenterAssignment { EmployeeId = employee.Id, CostCenterId = center.Id, IsActive = true });
        else replacement.IsActive = true;
        await ownerRoles.RemoveRoleWithoutScopeAsync([oldEmployee.Id], "BudgetOwnerAssignmentMoved", token);
        return ToDto(employee, center);
    }

    public async Task<BudgetOwnerCostCenterAssignmentDirectoryItemDto> DeleteAsync(string employeeCode, string costCenterCode, CancellationToken token)
    {
        EnsureSerializable();
        var employee = await db.Employees.SingleOrDefaultAsync(x => x.Code == employeeCode, token) ?? throw new BudgetOwnerCostCenterAssignmentNotFoundException();
        var center = await db.CostCenters.SingleOrDefaultAsync(x => x.Code == costCenterCode, token) ?? throw new BudgetOwnerCostCenterAssignmentNotFoundException();
        var assignment = await db.BudgetOwnerCostCenterAssignments.SingleOrDefaultAsync(x => x.EmployeeId == employee.Id && x.CostCenterId == center.Id && x.IsActive, token)
            ?? throw new BudgetOwnerCostCenterAssignmentNotFoundException();
        assignment.IsActive = false;
        await ownerRoles.RemoveRoleWithoutScopeAsync([employee.Id], "BudgetOwnerAssignmentRemoved", token);
        return ToDto(employee, center);
    }

    private void EnsureSerializable()
    {
        var tx = db.Database.CurrentTransaction;
        if (tx is null || tx.GetDbTransaction().IsolationLevel != System.Data.IsolationLevel.Serializable)
            throw new InvalidOperationException("Budget-owner maintenance requires a Serializable transaction.");
    }

    private async Task EnsureBudgetOwnerRoleAsync(Guid employeeId, CancellationToken token)
    {
        var role = await db.FunctionalRoles.SingleOrDefaultAsync(
            row => row.Code == ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode,
            token);
        if (role is null || !role.IsActive)
        {
            throw new DomainRuleViolationException(
                "Роль «Владелец бюджета» отключена; сначала активируйте её в справочнике ролей.");
        }

        var assignment = await db.EmployeeFunctionalRoles.SingleOrDefaultAsync(
            row => row.EmployeeId == employeeId && row.FunctionalRoleId == role.Id,
            token);
        if (assignment is null || !assignment.IsActive)
            throw new DomainRuleViolationException("Сначала системный администратор должен назначить сотруднику роль «Владелец бюджета».");
    }

    private static BudgetOwnerCostCenterAssignmentDirectoryItemDto ToDto(EmployeeReference employee, CostCenterReference center) =>
        new(employee.Code, employee.Name, center.Code, center.Name);
}
