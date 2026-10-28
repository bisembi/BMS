using System.Data;
using Bms.Application.Abstractions;
using Bms.Application.ReferenceData;
using Bms.Domain.Common;
using Bms.Domain.ReferenceData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// EF implementation of the shared directory editor.  Directories with an
/// active flag are deactivated rather than physically removed so existing
/// documents and foreign keys remain auditable.  Expense items have no active
/// flag in the schema and are physically removed only when no accrual mapping
/// refers to them.
/// </summary>
public sealed class ReferenceDataMaintenanceStore(BmsDbContext db, BudgetOwnerRoleSynchronizer? ownerRoles = null) : IReferenceDataMaintenanceStore
{
    public async Task<ReferenceDataMaintenanceItem> CreateAsync(
        ReferenceDataMaintenanceCommand command,
        CancellationToken cancellationToken)
    {
        EnsureSerializable();
        var directory = RequireDirectory(command.DirectoryId);
        if (command.HistoricalForActuals && (directory != "cash-flow-items" || command.IsGroup != false
            || !command.ParentIdSpecified || command.ParentId is null || command.ParentId == Guid.Empty
            || command.ParentName is not null || !CashFlowActivityType.IsDefined(command.ActivityType)))
            throw new DomainRuleViolationException("Historical actuals articles require explicit leaf, parent and activity classification on creation.");
        if (directory != "counterparties" && directory != "cash-flow-items" && directory != "expense-items"
            && await CodeExistsAsync(directory, command.Code, null, cancellationToken))
            throw new ReferenceDataMaintenanceConflictException();
        if (directory == "counterparties"
            && await BinExistsAsync(command.Bin, null, cancellationToken))
            throw new ReferenceDataMaintenanceConflictException();
        if (directory == "currencies"
            && await NumericCodeExistsAsync(command.NumericCode!, null, cancellationToken))
            throw new ReferenceDataMaintenanceConflictException();

        return directory switch
        {
            "organizations" => CreateOrganization(command),
            "cost-centers" => await CreateCostCenterAsync(command, cancellationToken),
            "cash-flow-items" => await CreateCashFlowItemAsync(command, cancellationToken),
            "expense-items" => await CreateExpenseItemAsync(command, cancellationToken),
            "counterparties" => await CreateCounterpartyAsync(command, cancellationToken),
            "contracts" => await CreateContractAsync(command, cancellationToken),
            "currencies" => CreateCurrency(command),
            _ => throw new DomainRuleViolationException("The reference directory is not writable."),
        };
    }

    public async Task<ReferenceDataMaintenanceItem> UpdateAsync(
        string directoryId,
        Guid id,
        ReferenceDataMaintenanceCommand command,
        CancellationToken cancellationToken)
    {
        EnsureSerializable();
        var directory = RequireDirectory(directoryId);
        if (command.HistoricalForActuals)
            throw new DomainRuleViolationException("Historical actuals classification is creation-only.");
        if (directory == "expense-items")
            await ExpenseItemCodeAllocator.LockCodesAsync(db, cancellationToken);
        if (directory != "counterparties" && directory != "cash-flow-items"
            && await CodeExistsAsync(directory, command.Code, id, cancellationToken))
            throw new ReferenceDataMaintenanceConflictException();
        if (directory == "counterparties"
            && await BinExistsAsync(command.Bin, id, cancellationToken))
            throw new ReferenceDataMaintenanceConflictException();
        if (directory == "currencies"
            && await NumericCodeExistsAsync(command.NumericCode!, id, cancellationToken))
            throw new ReferenceDataMaintenanceConflictException();
        return directory switch
        {
            "organizations" => await UpdateOrganizationAsync(id, command, cancellationToken),
            "cost-centers" => await UpdateCostCenterAsync(id, command, cancellationToken),
            "cash-flow-items" => await UpdateCashFlowItemAsync(id, command, cancellationToken),
            "expense-items" => await UpdateExpenseItemAsync(id, command, cancellationToken),
            "counterparties" => await UpdateCounterpartyAsync(id, command, cancellationToken),
            "contracts" => await UpdateContractAsync(id, command, cancellationToken),
            "currencies" => await UpdateCurrencyAsync(id, command, cancellationToken),
            _ => throw new DomainRuleViolationException("The reference directory is not writable."),
        };
    }

    public async Task<ReferenceDataMaintenanceItem> DeleteAsync(
        string directoryId,
        Guid id,
        CancellationToken cancellationToken)
    {
        EnsureSerializable();
        var directory = RequireDirectory(directoryId);
        return directory switch
        {
            "organizations" => await DeactivateOrganizationAsync(id, cancellationToken),
            "cost-centers" => await DeactivateCostCenterAsync(id, cancellationToken),
            "cash-flow-items" => await DeactivateCashFlowItemAsync(id, cancellationToken),
            "expense-items" => await DeleteExpenseItemAsync(id, cancellationToken),
            "counterparties" => await DeactivateCounterpartyAsync(id, cancellationToken),
            "contracts" => await DeactivateContractAsync(id, cancellationToken),
            "currencies" => await DeactivateCurrencyAsync(id, cancellationToken),
            _ => throw new DomainRuleViolationException("The reference directory is not writable."),
        };
    }

    private ReferenceDataMaintenanceItem CreateOrganization(ReferenceDataMaintenanceCommand command)
    {
        var entity = new OrganizationReference { Id = Guid.NewGuid(), Code = command.Code, Name = command.Name, IsActive = true };
        db.Organizations.Add(entity);
        return Item("organizations", entity.Id, entity.Code, entity.Name, isActive: true);
    }

    private async Task<ReferenceDataMaintenanceItem> CreateCostCenterAsync(ReferenceDataMaintenanceCommand command, CancellationToken token)
    {
        await EnsureParentAsync("cost-centers", command.ParentId, null, token);
        var entity = new CostCenterReference { Id = Guid.NewGuid(), Code = command.Code, Name = command.Name, ParentId = command.ParentId, IsGroup = command.IsGroup ?? false, IsActive = true };
        db.CostCenters.Add(entity);
        return Item("cost-centers", entity.Id, entity.Code, entity.Name, entity.ParentId, entity.IsActive, entity.IsGroup);
    }

    private async Task<ReferenceDataMaintenanceItem> CreateCashFlowItemAsync(ReferenceDataMaintenanceCommand command, CancellationToken token)
    {
        var parentId = await ResolveCashFlowParentAsync(command, null, token);
        var entity = new CashFlowItemReference
        {
            Id = Guid.NewGuid(),
            Code = await NextCashFlowItemCodeAsync(token),
            Name = command.Name,
            ParentId = parentId,
            ActivityType = command.ActivityType ?? CashFlowActivityType.Operating,
            FlowDirection = command.FlowDirection,
            IsGroup = command.IsGroup ?? false,
            IsActive = !command.HistoricalForActuals,
            IsHistoricalForActuals = command.HistoricalForActuals,
        };
        db.CashFlowItems.Add(entity);
        return Item("cash-flow-items", entity.Id, entity.Code, entity.Name, entity.ParentId, entity.IsActive, entity.IsGroup,
            activityType: entity.ActivityType, flowDirection: entity.FlowDirection);
    }

    private async Task<ReferenceDataMaintenanceItem> CreateExpenseItemAsync(ReferenceDataMaintenanceCommand command, CancellationToken token)
    {
        var code = await ExpenseItemCodeAllocator.NextAsync(db, token);
        var entity = new ExpenseItemReference { Id = Guid.NewGuid(), Code = code, Name = command.Name };
        db.ExpenseItems.Add(entity);
        return Item("expense-items", entity.Id, entity.Code, entity.Name, isActive: true);
    }

    private async Task<ReferenceDataMaintenanceItem> CreateCounterpartyAsync(
        ReferenceDataMaintenanceCommand command,
        CancellationToken token)
    {
        var code = await NextCounterpartyCodeAsync(token);
        var entity = new CounterpartyReference
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = command.Name,
            Bin = command.Bin,
            IsActive = true,
        };
        db.Counterparties.Add(entity);
        return Item("counterparties", entity.Id, entity.Code, entity.Name, isActive: true, bin: entity.Bin);
    }

    private async Task<ReferenceDataMaintenanceItem> CreateContractAsync(ReferenceDataMaintenanceCommand command, CancellationToken token)
    {
        await EnsureContractReferencesAsync(command, token);
        var entity = new ContractReference
        {
            Id = Guid.NewGuid(),
            CounterpartyId = command.CounterpartyId.GetValueOrDefault(),
            OrganizationId = command.OrganizationId.GetValueOrDefault(),
            Code = command.Code,
            Name = command.Name,
            ContractDate = command.ContractDate.GetValueOrDefault(),
            CurrencyId = command.CurrencyId.GetValueOrDefault(),
            IsActive = true,
        };
        db.Contracts.Add(entity);
        return Item("contracts", entity.Id, entity.Code, entity.Name, isActive: true,
            counterpartyId: entity.CounterpartyId, organizationId: entity.OrganizationId,
            contractDate: entity.ContractDate, currencyId: entity.CurrencyId);
    }

    private ReferenceDataMaintenanceItem CreateCurrency(ReferenceDataMaintenanceCommand command)
    {
        var entity = new CurrencyReference { Id = Guid.NewGuid(), Code = command.Code, Name = command.Name, NumericCode = command.NumericCode ?? string.Empty, IsActive = true };
        db.Currencies.Add(entity);
        return Item("currencies", entity.Id, entity.Code, entity.Name, isActive: true, numericCode: entity.NumericCode);
    }

    private async Task<ReferenceDataMaintenanceItem> UpdateOrganizationAsync(Guid id, ReferenceDataMaintenanceCommand command, CancellationToken token)
    {
        var entity = await db.Organizations.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new ReferenceDataMaintenanceNotFoundException();
        entity.Code = command.Code; entity.Name = command.Name;
        return Item("organizations", entity.Id, entity.Code, entity.Name, isActive: entity.IsActive);
    }

    private async Task<ReferenceDataMaintenanceItem> UpdateCostCenterAsync(Guid id, ReferenceDataMaintenanceCommand command, CancellationToken token)
    {
        var entity = await db.CostCenters.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new ReferenceDataMaintenanceNotFoundException();
        var removedScope = entity.IsActive && !entity.IsGroup && command.IsGroup == true;
        if (command.ParentId == id) throw new DomainRuleViolationException("A ЦФО cannot be its own parent.");
        if (command.IsGroup == true && await db.CashFlowItems.AnyAsync(item => item.CostCenterId == id && item.IsActive, token))
            throw new DomainRuleViolationException("Reassign active DDS articles before converting their CFO into a group.");
        await EnsureParentAsync("cost-centers", command.ParentId, id, token);
        entity.Code = command.Code; entity.Name = command.Name;
        if (command.ParentIdSpecified || command.ParentId is not null) entity.ParentId = command.ParentId;
        entity.IsGroup = command.IsGroup ?? entity.IsGroup;
        if (removedScope) await SynchronizeCenterOwnersAsync(id, "CostCenterConvertedToGroup", token);
        return Item("cost-centers", entity.Id, entity.Code, entity.Name, entity.ParentId, entity.IsActive, entity.IsGroup);
    }

    private async Task<ReferenceDataMaintenanceItem> UpdateCashFlowItemAsync(Guid id, ReferenceDataMaintenanceCommand command, CancellationToken token)
    {
        var entity = await db.CashFlowItems.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new ReferenceDataMaintenanceNotFoundException();
        EnsureHistoricalItemUpdateAllowed(entity, command);
        if ((command.IsGroup ?? entity.IsGroup) && (command.FlowDirectionSpecified ? command.FlowDirection : entity.FlowDirection) is not null)
            throw new DomainRuleViolationException("A group cannot have a financial direction. Clear the article direction explicitly first.");
        if (command.IsGroup == true && entity.CostCenterId is not null)
            throw new DomainRuleViolationException("Clear the article CFO before converting it into a group.");
        var parentId = await ResolveCashFlowParentAsync(command, id, token);
        if (parentId == id) throw new DomainRuleViolationException("A cash-flow item cannot be its own parent.");
        entity.Name = command.Name;
        if (command.ParentName is not null || command.ParentIdSpecified || command.ParentId is not null)
            entity.ParentId = parentId;
        entity.ActivityType = command.ActivityType ?? entity.ActivityType;
        if (command.FlowDirectionSpecified) entity.FlowDirection = command.FlowDirection;
        entity.IsGroup = command.IsGroup ?? entity.IsGroup;
        entity.AccrualSettingsVersion = Guid.NewGuid();
        return Item("cash-flow-items", entity.Id, entity.Code, entity.Name, entity.ParentId, entity.IsActive, entity.IsGroup,
            activityType: entity.ActivityType, flowDirection: entity.FlowDirection);
    }

    internal static void EnsureHistoricalItemUpdateAllowed(CashFlowItemReference entity, ReferenceDataMaintenanceCommand command)
    {
        if (entity.IsHistoricalForActuals && (command.IsGroup ?? entity.IsGroup))
            throw new DomainRuleViolationException("A historical actuals article must remain a leaf.");
    }

    private async Task<ReferenceDataMaintenanceItem> UpdateExpenseItemAsync(Guid id, ReferenceDataMaintenanceCommand command, CancellationToken token)
    {
        var entity = await db.ExpenseItems.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new ReferenceDataMaintenanceNotFoundException();
        entity.Code = command.Code; entity.Name = command.Name;
        return Item("expense-items", entity.Id, entity.Code, entity.Name, isActive: true);
    }

    private async Task<ReferenceDataMaintenanceItem> UpdateCounterpartyAsync(Guid id, ReferenceDataMaintenanceCommand command, CancellationToken token)
    {
        var entity = await db.Counterparties.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new ReferenceDataMaintenanceNotFoundException();
        entity.Name = command.Name;
        entity.Bin = command.Bin;
        return Item("counterparties", entity.Id, entity.Code, entity.Name, isActive: entity.IsActive, bin: entity.Bin);
    }

    private async Task<ReferenceDataMaintenanceItem> UpdateContractAsync(Guid id, ReferenceDataMaintenanceCommand command, CancellationToken token)
    {
        var entity = await db.Contracts.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new ReferenceDataMaintenanceNotFoundException();
        await EnsureContractReferencesAsync(command, token);
        entity.Code = command.Code; entity.Name = command.Name; entity.CounterpartyId = command.CounterpartyId.GetValueOrDefault();
        entity.OrganizationId = command.OrganizationId.GetValueOrDefault(); entity.ContractDate = command.ContractDate.GetValueOrDefault(); entity.CurrencyId = command.CurrencyId.GetValueOrDefault();
        return Item("contracts", entity.Id, entity.Code, entity.Name, isActive: entity.IsActive,
            counterpartyId: entity.CounterpartyId, organizationId: entity.OrganizationId, contractDate: entity.ContractDate, currencyId: entity.CurrencyId);
    }

    private async Task<ReferenceDataMaintenanceItem> UpdateCurrencyAsync(Guid id, ReferenceDataMaintenanceCommand command, CancellationToken token)
    {
        var entity = await db.Currencies.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new ReferenceDataMaintenanceNotFoundException();
        entity.Code = command.Code; entity.Name = command.Name; entity.NumericCode = command.NumericCode ?? string.Empty;
        return Item("currencies", entity.Id, entity.Code, entity.Name, isActive: entity.IsActive, numericCode: entity.NumericCode);
    }

    private async Task<ReferenceDataMaintenanceItem> DeactivateOrganizationAsync(Guid id, CancellationToken token)
    {
        var entity = await db.Organizations.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new ReferenceDataMaintenanceNotFoundException();
        entity.IsActive = false; return Item("organizations", entity.Id, entity.Code, entity.Name, isActive: false);
    }

    private async Task<ReferenceDataMaintenanceItem> DeactivateCostCenterAsync(Guid id, CancellationToken token)
    {
        if (await db.CashFlowItems.AnyAsync(item => item.CostCenterId == id && item.IsActive, token))
            throw new DomainRuleViolationException("Reassign active DDS articles before disabling their CFO.");
        var entity = await db.CostCenters.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new ReferenceDataMaintenanceNotFoundException();
        var removedScope = entity.IsActive && !entity.IsGroup;
        entity.IsActive = false;
        if (removedScope) await SynchronizeCenterOwnersAsync(id, "CostCenterDeactivated", token);
        return Item("cost-centers", entity.Id, entity.Code, entity.Name, entity.ParentId, false, entity.IsGroup);
    }

    private async Task SynchronizeCenterOwnersAsync(Guid id, string cause, CancellationToken token)
    {
        var ids = await db.BudgetOwnerCostCenterAssignments.Where(a => a.CostCenterId == id && a.IsActive)
            .Select(a => a.EmployeeId).ToArrayAsync(token);
        if (ids.Length == 0) return;
        // The standalone metadata resolver constructs this store without sinks;
        // owner-changing maintenance must use the fully configured DI writer.
        var synchronizer = ownerRoles ?? throw new InvalidOperationException("Owner synchronization is unavailable.");
        await synchronizer.RemoveRoleWithoutScopeAsync(ids, cause, token);
    }

    private async Task<ReferenceDataMaintenanceItem> DeactivateCashFlowItemAsync(Guid id, CancellationToken token)
    {
        var entity = await db.CashFlowItems.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new ReferenceDataMaintenanceNotFoundException();
        entity.IsActive = false; return Item("cash-flow-items", entity.Id, entity.Code, entity.Name, entity.ParentId, false, entity.IsGroup);
    }

    private async Task<ReferenceDataMaintenanceItem> DeleteExpenseItemAsync(Guid id, CancellationToken token)
    {
        var entity = await db.ExpenseItems.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new ReferenceDataMaintenanceNotFoundException();
        if (await db.CashFlowItems.AnyAsync(x => x.AccrualExpenseItemId == id, token))
            throw new ReferenceDataMaintenanceConflictException();
        db.ExpenseItems.Remove(entity);
        return Item("expense-items", entity.Id, entity.Code, entity.Name, isActive: false);
    }

    private async Task<ReferenceDataMaintenanceItem> DeactivateCounterpartyAsync(Guid id, CancellationToken token)
    {
        var entity = await db.Counterparties.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new ReferenceDataMaintenanceNotFoundException();
        entity.IsActive = false; return Item("counterparties", entity.Id, entity.Code, entity.Name, isActive: false, bin: entity.Bin);
    }

    private async Task<ReferenceDataMaintenanceItem> DeactivateContractAsync(Guid id, CancellationToken token)
    {
        var entity = await db.Contracts.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new ReferenceDataMaintenanceNotFoundException();
        entity.IsActive = false; return Item("contracts", entity.Id, entity.Code, entity.Name, isActive: false,
            counterpartyId: entity.CounterpartyId, organizationId: entity.OrganizationId, contractDate: entity.ContractDate, currencyId: entity.CurrencyId);
    }

    private async Task<ReferenceDataMaintenanceItem> DeactivateCurrencyAsync(Guid id, CancellationToken token)
    {
        var entity = await db.Currencies.SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new ReferenceDataMaintenanceNotFoundException();
        entity.IsActive = false; return Item("currencies", entity.Id, entity.Code, entity.Name, isActive: false, numericCode: entity.NumericCode);
    }

    private async Task EnsureParentAsync(string directoryId, Guid? parentId, Guid? childId, CancellationToken token)
    {
        if (parentId is null) return;
        // A named cash-flow group may have been staged by this same command.
        // It has no database row until the caller commits its transaction.
        var stagedGroup = directoryId == "cash-flow-items" && db.CashFlowItems.Local.Any(x =>
            x.Id == parentId && x.IsActive && x.IsGroup && db.Entry(x).State == EntityState.Added);
        var exists = directoryId switch
        {
            "cost-centers" => await db.CostCenters.AnyAsync(x => x.Id == parentId && x.IsActive && x.IsGroup, token),
            "cash-flow-items" => stagedGroup || await db.CashFlowItems.AnyAsync(x => x.Id == parentId && x.IsActive && x.IsGroup, token),
            _ => false,
        };
        if (!exists)
            throw new ReferenceDataMaintenanceNotFoundException();

        // Serializable reads retain the ancestry until commit. Checking only
        // parentId != childId misses A -> B -> A and longer cycles. Also refuse
        // attaching to an already malformed cycle; never rewrite it silently.
        var visited = new HashSet<Guid>();
        if (childId is { } child) visited.Add(child);
        var next = parentId;
        while (next is { } current)
        {
            token.ThrowIfCancellationRequested();
            if (!visited.Add(current))
                throw new DomainRuleViolationException("A reference-data hierarchy cannot contain a cycle.");
            if (directoryId == "cost-centers")
            {
                var tracked = db.CostCenters.Local.FirstOrDefault(x => x.Id == current);
                var row = tracked is not null && db.Entry(tracked).State == EntityState.Added ? new HierarchyParent(tracked.ParentId)
                    : await db.CostCenters.AsNoTracking().Where(x => x.Id == current)
                        .Select(x => new HierarchyParent(x.ParentId)).SingleOrDefaultAsync(token);
                if (tracked is not null && db.Entry(tracked).State == EntityState.Modified
                    && db.Entry(tracked).Property(x => x.ParentId).IsModified)
                    row = new HierarchyParent(tracked.ParentId);
                next = (row ?? throw new ReferenceDataMaintenanceNotFoundException()).ParentId;
            }
            else
            {
                var tracked = db.CashFlowItems.Local.FirstOrDefault(x => x.Id == current);
                var row = tracked is not null && db.Entry(tracked).State == EntityState.Added ? new HierarchyParent(tracked.ParentId)
                    : await db.CashFlowItems.AsNoTracking().Where(x => x.Id == current)
                        .Select(x => new HierarchyParent(x.ParentId)).SingleOrDefaultAsync(token);
                if (tracked is not null && db.Entry(tracked).State == EntityState.Modified
                    && db.Entry(tracked).Property(x => x.ParentId).IsModified)
                    row = new HierarchyParent(tracked.ParentId);
                next = (row ?? throw new ReferenceDataMaintenanceNotFoundException()).ParentId;
            }
        }
    }

    private sealed record HierarchyParent(Guid? ParentId);

    private async Task<Guid?> ResolveCashFlowParentAsync(
        ReferenceDataMaintenanceCommand command,
        Guid? childId,
        CancellationToken token)
    {
        if (command.ParentName is null)
        {
            await EnsureParentAsync("cash-flow-items", command.ParentId, childId, token);
            return command.ParentId;
        }

        var parentId = (await ResolveCashFlowGroupAsync(command.ParentName, token))?.Id;
        await EnsureParentAsync("cash-flow-items", parentId, childId, token);
        return parentId;
    }

    // Shared by the versioned inline editor. Resolving a group stages it in
    // the caller's Serializable transaction; it never saves independently.
    internal async Task<CashFlowItemReference?> ResolveCashFlowGroupAsync(string? groupName, CancellationToken token)
    {
        EnsureSerializable();
        if (groupName is null) return null;
        var groups = await db.CashFlowItems
            .Where(item => item.IsActive && item.IsGroup)
            .ToListAsync(token);
        var existing = groups.FirstOrDefault(item =>
            string.Equals(item.Name, groupName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var group = new CashFlowItemReference
        {
            Id = Guid.NewGuid(),
            Code = await NextCashFlowItemCodeAsync(token),
            Name = groupName,
            IsGroup = true,
            IsActive = true,
        };
        db.CashFlowItems.Add(group);
        return group;
    }

    private async Task EnsureContractReferencesAsync(ReferenceDataMaintenanceCommand command, CancellationToken token)
    {
        if (!await db.Counterparties.AnyAsync(x => x.Id == command.CounterpartyId && x.IsActive, token)
            || !await db.Organizations.AnyAsync(x => x.Id == command.OrganizationId && x.IsActive, token)
            || !await db.Currencies.AnyAsync(x => x.Id == command.CurrencyId && x.IsActive, token))
            throw new ReferenceDataMaintenanceNotFoundException();
    }

    private async Task<bool> CodeExistsAsync(string directory, string code, Guid? exceptId, CancellationToken token) => directory switch
    {
        "organizations" => await db.Organizations.AnyAsync(x => x.Code == code && (!exceptId.HasValue || x.Id != exceptId), token),
        "cost-centers" => await db.CostCenters.AnyAsync(x => x.Code == code && (!exceptId.HasValue || x.Id != exceptId), token),
        "cash-flow-items" => await db.CashFlowItems.AnyAsync(x => x.Code == code && (!exceptId.HasValue || x.Id != exceptId), token),
        "expense-items" => await db.ExpenseItems.AnyAsync(x => x.Code == code && (!exceptId.HasValue || x.Id != exceptId), token),
        "counterparties" => false,
        "contracts" => await db.Contracts.AnyAsync(x => x.Code == code && (!exceptId.HasValue || x.Id != exceptId), token),
        "currencies" => await db.Currencies.AnyAsync(x => x.Code == code && (!exceptId.HasValue || x.Id != exceptId), token),
        _ => throw new DomainRuleViolationException("The reference directory is not writable."),
    };

    private Task<bool> BinExistsAsync(string? bin, Guid? exceptId, CancellationToken token) =>
        string.IsNullOrWhiteSpace(bin)
            ? Task.FromResult(false)
            : db.Counterparties.AnyAsync(
                x => x.Bin == bin && (!exceptId.HasValue || x.Id != exceptId),
                token);

    private async Task<string> NextCounterpartyCodeAsync(CancellationToken token)
    {
        var used = await db.Counterparties
            .Select(counterparty => counterparty.Code)
            .ToListAsync(token);
        var next = used
            .Select(code => System.Text.RegularExpressions.Regex.Match(code, "^CP-(?<number>\\d+)$"))
            .Where(match => match.Success && long.TryParse(match.Groups["number"].Value, out _))
            .Select(match => long.Parse(match.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0)
            .Max() + 1;
        while (await db.Counterparties.AnyAsync(x => x.Code == $"CP-{next:D6}", token)) next++;
        return $"CP-{next:D6}";
    }

    private async Task<string> NextCashFlowItemCodeAsync(CancellationToken token)
    {
        var used = await db.CashFlowItems
            .Select(item => item.Code)
            .ToListAsync(token);
        used.AddRange(db.CashFlowItems.Local.Select(item => item.Code));
        var next = used
            .Select(code => System.Text.RegularExpressions.Regex.Match(code, "^CF-(?<number>\\d+)$"))
            .Where(match => match.Success && long.TryParse(match.Groups["number"].Value, out _))
            .Select(match => long.Parse(match.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0)
            .Max() + 1;
        while (used.Contains($"CF-{next:D6}", StringComparer.Ordinal)) next++;
        return $"CF-{next:D6}";
    }

    private Task<bool> NumericCodeExistsAsync(string numericCode, Guid? exceptId, CancellationToken token) =>
        db.Currencies.AnyAsync(
            currency => currency.NumericCode == numericCode
                && (!exceptId.HasValue || currency.Id != exceptId),
            token);

    private void EnsureSerializable()
    {
        var transaction = db.Database.CurrentTransaction;
        if (transaction is null || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Reference-data maintenance requires a Serializable business transaction.");
    }

    private static string RequireDirectory(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "organizations" => "organizations",
        "cost-centers" => "cost-centers",
        "cash-flow-items" => "cash-flow-items",
        "expense-items" => "expense-items",
        "counterparties" => "counterparties",
        "contracts" => "contracts",
        "currencies" => "currencies",
        _ => throw new DomainRuleViolationException("The reference directory is not writable."),
    };

    private static ReferenceDataMaintenanceItem Item(string directoryId, Guid id, string code, string name,
        Guid? parentId = null, bool isActive = true, bool? isGroup = null, Guid? counterpartyId = null,
        Guid? organizationId = null, DateOnly? contractDate = null, Guid? currencyId = null, string? numericCode = null,
        string? bin = null, string? activityType = null, string? flowDirection = null) =>
        new(id, directoryId, code, name, parentId, isActive, isGroup, counterpartyId, organizationId, contractDate, currencyId, numericCode, bin, activityType, flowDirection);
}
