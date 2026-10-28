using System.Data;
using Bms.Application.Budgeting;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

/// <summary>One permanent CFO counter; caller owns save, commit and rollback.</summary>
public static class BudgetInstanceNumberAllocation
{
    public static async Task<string> AllocateLockedAsync(BmsDbContext db, Guid costCenterId,
        DateOnly documentDate, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Budget numbering requires a Serializable transaction.");
        if (costCenterId == Guid.Empty || documentDate == default)
            throw new DomainRuleViolationException("The CFO and document date are required.");
        var center = await db.CostCenters.FromSqlInterpolated($"""
            SELECT [c].* FROM [bms_web].[CostCenters] AS [c]
            WITH (UPDLOCK, HOLDLOCK, INDEX([PK_bms_web_CostCenters])) WHERE [c].[Id] = {costCenterId}
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (center is null || !center.IsActive || center.IsGroup) throw new BudgetDirectoryNotFoundException();

        // Scoped contexts may outlive a committed transaction. Never use stale
        // tracked counters; never discard unsaved metadata from another action.
        var prior = db.ChangeTracker.Entries().Where(entry => entry.Entity switch
        {
            BudgetCostCenterCounter row => row.CostCenterId == costCenterId,
            BudgetCostCenterSettings row => row.CostCenterId == costCenterId,
            _ => false
        }).ToArray();
        if (prior.Any(entry => entry.State != EntityState.Unchanged))
            throw new InvalidOperationException("Unsaved numbering metadata cannot be replaced.");
        foreach (var entry in prior) entry.State = EntityState.Detached;

        var settings = await db.BudgetCostCenterSettings.SingleOrDefaultAsync(row => row.CostCenterId == costCenterId, cancellationToken);
        var counter = await db.BudgetCostCenterCounters.SingleOrDefaultAsync(row => row.CostCenterId == costCenterId, cancellationToken);
        var prefix = settings?.Abbreviation ?? BudgetDocumentNumber.CanonicalizePrefix(center.Code);
        BudgetDocumentNumber.RequireCanonicalPrefix(prefix);
        if (counter is null && (await db.BudgetInstances.IgnoreQueryFilters().AnyAsync(row => row.CostCenterId == costCenterId, cancellationToken)
            || await db.BudgetPrefixOwners.AnyAsync(row => row.CostCenterId == costCenterId, cancellationToken)))
            throw new BudgetNumberingNotInitializedException();

        var prefixOwner = await db.BudgetPrefixOwners.FromSqlInterpolated($"""
            SELECT [p].* FROM [bms_web].[BudgetPrefixOwners] AS [p] WITH (UPDLOCK, HOLDLOCK)
            WHERE [p].[CanonicalPrefix] = {prefix}
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (prefixOwner is not null && prefixOwner.CostCenterId != costCenterId) throw new BudgetPrefixConflictException();
        if (prefixOwner is null)
        {
            if (settings?.Abbreviation is not null)
                throw new DomainRuleViolationException("The saved budget prefix owner is inconsistent.");
            if (db.BudgetPrefixOwners.Local.Any(row => row.CanonicalPrefix == prefix && row.CostCenterId != costCenterId))
                throw new BudgetPrefixConflictException();
            await foreach (var legacy in db.BudgetInstances.IgnoreQueryFilters().AsNoTracking()
                .Where(row => row.CostCenterId != costCenterId).Select(row => row.Number)
                .AsAsyncEnumerable().WithCancellation(cancellationToken))
                if (BudgetDocumentNumber.UsesPrefix(legacy, prefix)) throw new BudgetPrefixConflictException();
            db.BudgetPrefixOwners.Add(new BudgetPrefixOwner { CanonicalPrefix = prefix, CostCenterId = costCenterId });
        }
        if (settings is null)
        {
            settings = new BudgetCostCenterSettings { CostCenterId = costCenterId, Abbreviation = prefix, ConcurrencyStamp = Guid.NewGuid() };
            db.BudgetCostCenterSettings.Add(settings);
        }
        else if (settings.Abbreviation is null)
        {
            settings.Abbreviation = prefix;
            settings.ConcurrencyStamp = Guid.NewGuid();
        }
        if (counter is null)
        {
            counter = new BudgetCostCenterCounter { CostCenterId = costCenterId, LastIssuedOrdinal = 0 };
            db.BudgetCostCenterCounters.Add(counter);
        }
        var next = BudgetDocumentNumber.Next(documentDate, prefix, counter.LastIssuedOrdinal);
        if (await db.BudgetInstances.IgnoreQueryFilters().AnyAsync(row => row.Number == next.Number, cancellationToken)
            || db.BudgetInstances.Local.Any(row => row.Number == next.Number))
            throw new DomainRuleViolationException("The next budget number is already in use.");
        counter.LastIssuedOrdinal = next.Ordinal;
        return next.Number;
    }
}
