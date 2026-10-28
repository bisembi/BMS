using System.Data;
using Bms.Application.Abstractions;
using Bms.Application.ReferenceData;
using Bms.Domain.ReferenceData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Only the article's local settings/classification/version are writable. The surrounding
/// transaction retains the article/expense-item reads until audit and commit.
/// </summary>
public sealed class CashFlowAccrualSettingsStore(BmsDbContext dbContext) : ICashFlowAccrualSettingsStore
{
    public async Task<CashFlowAccrualSettingsSnapshot?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var row = await (
            from item in dbContext.CashFlowItems.AsNoTracking()
            where item.Id == id
            join expense in dbContext.ExpenseItems.AsNoTracking()
                on item.AccrualExpenseItemId equals expense.Id into expenses
            from expense in expenses.DefaultIfEmpty()
            join center in dbContext.CostCenters.AsNoTracking()
                on item.CostCenterId equals center.Id into centers
            from center in centers.DefaultIfEmpty()
            join parent in dbContext.CashFlowItems.AsNoTracking()
                on item.ParentId equals parent.Id into parents
            from parent in parents.DefaultIfEmpty()
            select new
            {
                item.Id,
                item.Code,
                item.Name,
                item.IsActive,
                item.IsGroup,
                item.FormsManagerialAccrual,
                item.AccrualExpenseItemId,
                item.AccrualSettingsVersion,
                item.CostCenterId,
                item.ActivityType,
                item.FlowDirection,
                item.ParentId,
                GroupName = parent == null ? null : parent.Name,
                CenterCode = center == null ? null : center.Code,
                CenterName = center == null ? null : center.Name,
                ExpenseId = expense == null ? (Guid?)null : expense.Id,
                ExpenseCode = expense == null ? null : expense.Code,
                ExpenseName = expense == null ? null : expense.Name
            }).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        var expenseItem = row.ExpenseId is { } expenseId
            ? new ReferenceDataItemDto(expenseId,
                row.ExpenseCode ?? throw new CashFlowAccrualSettingsUnavailableException(),
                row.ExpenseName ?? throw new CashFlowAccrualSettingsUnavailableException()) : null;
        return new(row.Id, row.Code, row.Name, row.IsActive, row.IsGroup,
            row.FormsManagerialAccrual, row.AccrualExpenseItemId, expenseItem, row.AccrualSettingsVersion,
            row.CostCenterId, row.CostCenterId is { } centerId
                ? new ReferenceDataItemDto(centerId,
                    row.CenterCode ?? throw new CashFlowAccrualSettingsUnavailableException(),
                    row.CenterName ?? throw new CashFlowAccrualSettingsUnavailableException()) : null,
            row.ActivityType, row.ParentId, row.GroupName, row.FlowDirection);
    }

    public async Task<ReferenceDataItemDto?> FindCostCenterAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireTransaction();
        return await dbContext.CostCenters.AsNoTracking()
            .Where(center => center.Id == id && center.IsActive && !center.IsGroup)
            .Select(center => new ReferenceDataItemDto(center.Id, center.Code, center.Name))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ReferenceDataItemDto?> FindExpenseItemAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireTransaction();
        return await dbContext.ExpenseItems.AsNoTracking().Where(item => item.Id == id)
            .Select(item => new ReferenceDataItemDto(item.Id, item.Code, item.Name))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task StageChangeAsync(CashFlowAccrualSettingsSnapshot before,
        CashFlowAccrualSettings settings, Guid newVersion, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (dbContext.ChangeTracker.HasChanges() || newVersion == Guid.Empty || newVersion == before.Version)
            throw new CashFlowAccrualSettingsUnavailableException();
        var row = await dbContext.CashFlowItems.SingleOrDefaultAsync(item => item.Id == before.Id, cancellationToken)
            ?? throw new CashFlowAccrualSettingsNotFoundException();
        if (row.AccrualSettingsVersion != before.Version)
            throw new CashFlowAccrualSettingsVersionConflictException();
        if (row.Code != before.Code || row.Name != before.Name || row.IsActive != before.IsActive
            || row.IsGroup != before.IsGroup || row.FormsManagerialAccrual != before.FormsManagerialAccrual
            || row.AccrualExpenseItemId != before.ExpenseItemId || row.CostCenterId != before.CostCenterId
            || row.ActivityType != before.ActivityType || row.ParentId != before.ParentId
            || row.FlowDirection != before.FlowDirection)
            throw new CashFlowAccrualSettingsUnavailableException();
        row.FormsManagerialAccrual = settings.FormsManagerialAccrual;
        row.AccrualExpenseItemId = settings.ExpenseItemId;
        row.AccrualSettingsVersion = newVersion;
        row.CostCenterId = settings.CostCenterId;
    }

    public Task StageNameAsync(CashFlowAccrualSettingsSnapshot before, Guid newVersion,
        string name, CancellationToken cancellationToken)
    {
        RequireTransaction();
        cancellationToken.ThrowIfCancellationRequested();
        var row = dbContext.CashFlowItems.Local.SingleOrDefault(item => item.Id == before.Id)
            ?? throw new CashFlowAccrualSettingsUnavailableException();
        if (newVersion == Guid.Empty || newVersion == before.Version || row.AccrualSettingsVersion != newVersion
            || row.Name != before.Name || string.IsNullOrWhiteSpace(name) || name != name.Trim()
            || name.Length > 150 || name.Any(char.IsControl))
            throw new CashFlowAccrualSettingsUnavailableException();
        // Only the tracked catalogue label changes. IDs, codes and sealed
        // source-to-article bindings and document snapshots are never rewritten.
        row.Name = name;
        return Task.CompletedTask;
    }

    public Task StageFlowDirectionAsync(CashFlowAccrualSettingsSnapshot before, Guid newVersion,
        string? flowDirection, CancellationToken cancellationToken)
    {
        RequireTransaction();
        cancellationToken.ThrowIfCancellationRequested();
        var row = dbContext.CashFlowItems.Local.SingleOrDefault(item => item.Id == before.Id)
            ?? throw new CashFlowAccrualSettingsUnavailableException();
        if (newVersion == Guid.Empty || newVersion == before.Version || row.AccrualSettingsVersion != newVersion
            || row.FlowDirection != before.FlowDirection || flowDirection is not (null or "Payment" or "Receipt"))
            throw new CashFlowAccrualSettingsUnavailableException();
        row.FlowDirection = flowDirection;
        return Task.CompletedTask;
    }

    public async Task<ReferenceDataItemDto?> StageClassificationAsync(CashFlowAccrualSettingsSnapshot before,
        Guid newVersion, string activityType, string? groupName, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var row = dbContext.CashFlowItems.Local.SingleOrDefault(item => item.Id == before.Id)
            ?? throw new CashFlowAccrualSettingsUnavailableException();
        if (newVersion == before.Version || row.AccrualSettingsVersion != newVersion
            || row.ActivityType != before.ActivityType || row.ParentId != before.ParentId)
            throw new CashFlowAccrualSettingsUnavailableException();
        ReferenceDataItemDto? parent;
        if (string.Equals(groupName, before.GroupName, StringComparison.OrdinalIgnoreCase))
        {
            // Preserve the precise current parent even if it is archived or
            // another group has the same name. An activity-only edit must not
            // reassign a group by label.
            parent = before.ParentId is { } parentId
                ? new(parentId, string.Empty, before.GroupName ?? throw new CashFlowAccrualSettingsUnavailableException()) : null;
        }
        else
        {
            var resolved = await new ReferenceDataMaintenanceStore(dbContext)
                .ResolveCashFlowGroupAsync(groupName, cancellationToken);
            row.ParentId = resolved?.Id;
            parent = resolved is null ? null : new(resolved.Id, resolved.Code, resolved.Name);
        }
        row.ActivityType = activityType;
        return parent;
    }

    private void RequireTransaction()
    {
        if (dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new CashFlowAccrualSettingsUnavailableException();
    }
}
