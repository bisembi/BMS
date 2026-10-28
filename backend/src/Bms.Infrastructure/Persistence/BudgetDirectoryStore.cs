using System.Data;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Budgeting;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

/// <summary>Never commits independently of the directory/document business transaction.</summary>
public sealed partial class BudgetDirectoryStore(BmsDbContext db) : IBudgetDirectoryStore
{
    private Guid? lockedTransactionId;
    private Guid? lockedCostCenterId;

    public async Task<bool> LockCostCenterAsync(Guid costCenterId, CancellationToken cancellationToken)
    {
        var transactionId = EnsureSerializable();
        if (lockedTransactionId == transactionId && lockedCostCenterId != costCenterId)
        {
            throw new InvalidOperationException("A budget directory store can lock only one cost center per transaction.");
        }
        var center = await db.CostCenters.FromSqlInterpolated($"""
            SELECT [c].* FROM [bms_web].[CostCenters] AS [c]
            WITH (UPDLOCK, HOLDLOCK, INDEX([PK_bms_web_CostCenters]))
            WHERE [c].[Id] = {costCenterId}
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (lockedTransactionId != transactionId)
        {
            // A scoped DbContext can survive multiple successful transactions.
            // Refresh its directory identity map after the anchor lock. Pending
            // writes are invalid input: the caller's transaction runner owns
            // rollback and clears tracked state on failure.
            var prior = db.ChangeTracker.Entries().Where(entry => entry.Entity switch
            {
                BudgetCostCenterSettings row => row.CostCenterId == costCenterId,
                BudgetCostCenterCounter row => row.CostCenterId == costCenterId,
                BudgetCostCenterMembership row => row.CostCenterId == costCenterId,
                CostCenterPurpose457 row => row.CostCenterId == costCenterId,
                _ => false
            }).ToArray();
            if (prior.Any(entry => entry.State != EntityState.Unchanged))
            {
                throw new InvalidOperationException("Unsaved budget directory changes cannot be replaced at a transaction boundary.");
            }
            foreach (var entry in prior) { entry.State = EntityState.Detached; }
        }
        lockedTransactionId = transactionId;
        lockedCostCenterId = costCenterId;
        return center is not null;
    }

    public async Task<BudgetDirectorySettingsDto?> ReadAsync(Guid costCenterId, CancellationToken cancellationToken)
    {
        EnsureSerializable();
        var center = await db.CostCenters.AsNoTracking().SingleOrDefaultAsync(row => row.Id == costCenterId, cancellationToken);
        if (center is null) { return null; }
        var settings = await db.BudgetCostCenterSettings.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CostCenterId == costCenterId, cancellationToken);
        var counter = await db.BudgetCostCenterCounters.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CostCenterId == costCenterId, cancellationToken);
        var purpose = await db.CostCenterPurposes457.AsNoTracking().SingleOrDefaultAsync(row => row.CostCenterId == costCenterId, cancellationToken);
        if (settings?.Abbreviation is { } prefix) { BudgetDocumentNumber.RequireCanonicalPrefix(prefix); }
        if (settings?.ConcurrencyStamp == Guid.Empty || counter?.LastIssuedOrdinal < 0)
        {
            throw new DomainRuleViolationException("Saved budget directory state is invalid.");
        }
        var members = await (
            from member in db.BudgetCostCenterMemberships.AsNoTracking()
            where member.CostCenterId == costCenterId
            join employee in db.Employees.AsNoTracking() on member.EmployeeId equals employee.Id
            orderby employee.Code, employee.Id
            select new BudgetDirectoryMemberDto(employee.Id, employee.Code, employee.Name,
                member.MembershipKind, member.IsActive, employee.IsActive)).ToArrayAsync(cancellationToken);
        return new(costCenterId, center.Code, center.Name, settings?.Abbreviation,
            settings?.ConcurrencyStamp, counter is not null, members, purpose?.OnlyCostDepartment ?? false, purpose?.ConcurrencyStamp);
    }

    public async Task<BudgetDirectoryOptionPageDto> ListCostCentersAsync(
        string query, int skip, int take, CancellationToken cancellationToken)
    {
        EnsureSerializable();
        var rows = db.CostCenters.AsNoTracking().Where(row => !row.IsGroup);
        if (query.Length > 0) { rows = rows.Where(row => row.Code.Contains(query) || row.Name.Contains(query)); }
        var total = await rows.CountAsync(cancellationToken);
        var items = await rows.OrderBy(row => row.Code).ThenBy(row => row.Id).Skip(skip).Take(take)
            .Select(row => new BudgetDirectoryOptionDto(row.Id, row.Code, row.Name, row.IsActive,
                db.CostCenterPurposes457.Any(s => s.CostCenterId == row.Id && s.OnlyCostDepartment),
                db.CostCenterPurposes457.Where(s => s.CostCenterId == row.Id).Select(s => (Guid?)s.ConcurrencyStamp).FirstOrDefault())).ToArrayAsync(cancellationToken);
        return new(items, skip, take, total);
    }

    public async Task<BudgetDirectoryOptionPageDto> ListEmployeesAsync(
        string query, int skip, int take, CancellationToken cancellationToken)
    {
        EnsureSerializable();
        var rows = db.Employees.AsNoTracking().Where(row => row.IsActive);
        if (query.Length > 0) { rows = rows.Where(row => row.Code.Contains(query) || row.Name.Contains(query)); }
        var total = await rows.CountAsync(cancellationToken);
        var items = await rows.OrderBy(row => row.Code).ThenBy(row => row.Id).Skip(skip).Take(take)
            .Select(row => new BudgetDirectoryOptionDto(row.Id, row.Code, row.Name, row.IsActive)).ToArrayAsync(cancellationToken);
        return new(items, skip, take, total);
    }

    public async Task<BudgetDirectorySettingsDto> ReplaceAsync(
        Guid costCenterId, UpdateBudgetDirectoryCommand command, CancellationToken cancellationToken)
    {
        EnsureLocked(costCenterId);
        command = BudgetDirectoryService.ValidateCommand(command);
        var center = await db.CostCenters.AsNoTracking().SingleOrDefaultAsync(row => row.Id == costCenterId, cancellationToken)
            ?? throw new BudgetDirectoryNotFoundException();
        if (center.IsGroup) { throw new DomainRuleViolationException("A budget directory requires a non-group cost center."); }
        var settings = await db.BudgetCostCenterSettings.SingleOrDefaultAsync(row => row.CostCenterId == costCenterId, cancellationToken);
        if (settings?.ConcurrencyStamp != command.ExpectedConcurrencyStamp)
        {
            throw new BudgetInstanceConcurrencyConflictException();
        }
        if (settings?.Abbreviation is { } oldPrefix) { BudgetDocumentNumber.RequireCanonicalPrefix(oldPrefix); }

        var members = await db.BudgetCostCenterMemberships.Where(row => row.CostCenterId == costCenterId).ToListAsync(cancellationToken);
        var ids = command.Members.Select(member => member.EmployeeId).Concat(members.Select(member => member.EmployeeId)).Distinct().ToArray();
        var employees = await db.Employees.AsNoTracking().Where(row => ids.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        if (command.Members.Any(member => !employees.TryGetValue(member.EmployeeId, out var employee)
            || (member.IsActive && !employee.IsActive)))
        {
            throw new DomainRuleViolationException("An active budget membership requires an existing active employee.");
        }
        if (members.Any(member => !employees.ContainsKey(member.EmployeeId)))
        {
            throw new DomainRuleViolationException("Saved budget membership is inconsistent.");
        }

        // Only the requested key is locked, after the CFO anchor. Never modify
        // a foreign owner's row or delete the previous prefix during a rename.
        var prefixOwner = await db.BudgetPrefixOwners.FromSqlInterpolated($"""
            SELECT [p].* FROM [bms_web].[BudgetPrefixOwners] AS [p] WITH (UPDLOCK, HOLDLOCK)
            WHERE [p].[CanonicalPrefix] = {command.Abbreviation}
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (prefixOwner is not null && prefixOwner.CostCenterId != costCenterId) { throw new BudgetPrefixConflictException(); }
        if (prefixOwner is null)
        {
            // SQL034 preserved old documents but did not invent prefix owners.
            // Check their actual prefix with the same Unicode semantics as the
            // new writer; checking only next-number equality misses IT-0042.
            // Stream two scalar fields (no document bodies/all-list allocation).
            await foreach (var legacy in db.BudgetInstances.IgnoreQueryFilters().AsNoTracking()
                .Where(row => row.CostCenterId != costCenterId)
                .Select(row => new { row.Number, row.CostCenterId })
                .AsAsyncEnumerable().WithCancellation(cancellationToken))
            {
                if (BudgetDocumentNumber.UsesPrefix(legacy.Number, command.Abbreviation))
                {
                    throw new BudgetPrefixConflictException();
                }
            }
        }

        var counter = await db.BudgetCostCenterCounters.SingleOrDefaultAsync(row => row.CostCenterId == costCenterId, cancellationToken);
        if (counter?.LastIssuedOrdinal < 0) { throw new DomainRuleViolationException("The budget number sequence is invalid."); }
        // Evaluate BEFORE adding the first prefix. A missing counter with any
        // legacy documents/history remains uninitialized; no MAX+1 adoption.
        var mayInitialize = counter is null
            && !await db.BudgetPrefixOwners.AnyAsync(row => row.CostCenterId == costCenterId, cancellationToken)
            && !await db.BudgetInstances.IgnoreQueryFilters().AnyAsync(row => row.CostCenterId == costCenterId, cancellationToken);

        if (prefixOwner is null)
        {
            db.BudgetPrefixOwners.Add(new BudgetPrefixOwner { CanonicalPrefix = command.Abbreviation, CostCenterId = costCenterId });
        }
        if (settings is null)
        {
            settings = new BudgetCostCenterSettings { CostCenterId = costCenterId };
            db.BudgetCostCenterSettings.Add(settings);
        }
        settings.Abbreviation = command.Abbreviation;
        settings.ConcurrencyStamp = Guid.NewGuid();
        if (mayInitialize)
        {
            counter = new BudgetCostCenterCounter { CostCenterId = costCenterId, LastIssuedOrdinal = 0 };
            db.BudgetCostCenterCounters.Add(counter);
        }

        var byEmployee = members.ToDictionary(member => member.EmployeeId);
        foreach (var member in members) { member.IsActive = false; }
        foreach (var input in command.Members)
        {
            if (!byEmployee.TryGetValue(input.EmployeeId, out var member))
            {
                member = new BudgetCostCenterMembership { CostCenterId = costCenterId, EmployeeId = input.EmployeeId };
                db.BudgetCostCenterMemberships.Add(member);
                members.Add(member);
            }
            member.MembershipKind = input.Kind;
            member.IsActive = input.IsActive;
        }
        var resultMembers = members.Select(member =>
        {
            var employee = employees[member.EmployeeId];
            return new BudgetDirectoryMemberDto(employee.Id, employee.Code, employee.Name,
                member.MembershipKind, member.IsActive, employee.IsActive);
        }).OrderBy(member => member.Code, StringComparer.Ordinal).ThenBy(member => member.EmployeeId).ToArray();
        return new(costCenterId, center.Code, center.Name, settings.Abbreviation, settings.ConcurrencyStamp,
            counter is not null, resultMembers);
    }

    public Task<string> AllocateNumberAsync(Guid costCenterId, DateOnly documentDate, CancellationToken cancellationToken)
    {
        EnsureLocked(costCenterId);
        return BudgetInstanceNumberAllocation.AllocateLockedAsync(db, costCenterId, documentDate, cancellationToken);
    }

    private void EnsureLocked(Guid costCenterId)
    {
        var transactionId = EnsureSerializable();
        if (costCenterId == Guid.Empty || lockedCostCenterId != costCenterId
            || lockedTransactionId != transactionId)
        {
            throw new InvalidOperationException("The exact cost center must be locked in the current transaction.");
        }
    }

    private Guid EnsureSerializable()
    {
        var transaction = db.Database.CurrentTransaction;
        if (transaction is null || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
        {
            throw new InvalidOperationException("Budget directory operations require an active Serializable transaction.");
        }
        return transaction.TransactionId;
    }
}
