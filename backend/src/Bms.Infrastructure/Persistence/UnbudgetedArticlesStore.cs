using System.Data;
using Bms.Application.Abstractions;
using Bms.Application.Budgeting;
using Bms.Application.Common;
using Bms.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class UnbudgetedArticlesStore(BmsDbContext db) : IUnbudgetedArticlesStore
{
    public async Task<UnbudgetedArticlesReportDto> ReadAsync(UnbudgetedArticlesOptions options,
        Guid employeeId, bool canSeeAll, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new CompanyBudgetPlanUnavailableException();
        var months = options.Months();
        var centersQuery = db.CostCenters.AsNoTracking().Where(c => c.IsActive && !c.IsGroup
            && !db.CostCenterPurposes457.Any(s => s.CostCenterId == c.Id && s.OnlyCostDepartment));
        if (!canSeeAll) centersQuery = centersQuery.Where(c =>
            BusinessDataReadScopeQuery.OwnedCostCenterIds(db, employeeId).Contains(c.Id));
        var centers = await centersQuery.OrderBy(c => c.Code).ThenBy(c => c.Id)
            .Select(c => new BddsCostCenterDto(c.Id, c.Code, c.Name)).Take(10_001).ToArrayAsync(cancellationToken);
        if (centers.Length > 10_000) throw new CompanyBudgetPlanUnavailableException();
        var ids = centers.Select(c => c.Id).ToArray();
        if (ids.Length == 0 || options.CostCenterId.HasValue && !ids.Contains(options.CostCenterId.Value))
            throw new ActorForbiddenException();
        // Hide an explicit composition outside the current actor's entire CFO scope.
        if (!canSeeAll && options.VersionId.HasValue &&
            !await db.CompanyBudgetVersionInstances.AnyAsync(m => m.VersionId == options.VersionId
                && db.BudgetInstances.Any(b => b.Id == m.BudgetInstanceId && ids.Contains(b.CostCenterId)),
                cancellationToken))
            throw new CompanyBudgetVersionNotFoundException();
        var selectedIds = options.CostCenterId.HasValue ? new[] { options.CostCenterId.Value } : ids;
        // The CFO filter selects current catalog rows, not the historical plan owner.
        // Read only this version and all authorized original CFOs; never other versions.
        var plan = await CompanyBudgetPlanReader.ReadAsync(db, options.VersionId, ids, cancellationToken);
        var catalog = await (from item in db.CashFlowItems.AsNoTracking()
            join center in db.CostCenters.AsNoTracking() on item.CostCenterId equals (Guid?)center.Id
            join candidateParent in db.CashFlowItems.AsNoTracking().Where(p => p.IsGroup)
                on item.ParentId equals (Guid?)candidateParent.Id into parents
            from parent in parents.DefaultIfEmpty()
            where item.IsActive && !item.IsGroup && !item.IsHistoricalForActuals
                && selectedIds.Contains(center.Id)
            select new UnbudgetedArticleDto(item.Id, item.Name, item.ActivityType,
                center.Id, center.Code, center.Name, item.ParentId,
                parent == null ? "Без группы" : parent.Name, 0m, (int?)null))
            .Take(10_001).ToArrayAsync(cancellationToken);
        if (catalog.Length > 10_000) throw new CompanyBudgetPlanUnavailableException();
        var ranks = await new CashFlowCatalogOrderStore(db).ReadDisplayOrdersAsync(
            catalog.Select(row => row.Id).ToArray(), cancellationToken);
        if (ranks.Count != catalog.Length) throw new CompanyBudgetPlanUnavailableException();
        catalog = catalog.Select(row => row with { DisplayOrder = ranks[row.Id] }).ToArray();
        var parentIds = catalog.Where(row => row.GroupId.HasValue).Select(row => row.GroupId!.Value).Distinct().ToArray();
        if (await db.CashFlowItems.CountAsync(p => p.IsGroup && parentIds.Contains(p.Id), cancellationToken) != parentIds.Length)
            throw new CompanyBudgetPlanUnavailableException();
        var groups = catalog.Where(row => row.GroupId.HasValue)
            .GroupBy(row => row.GroupId!.Value)
            .Select(g => new UnbudgetedGroupDto(g.Key, g.First().GroupName))
            .OrderBy(g => g.Name, StringComparer.Ordinal).ThenBy(g => g.Id).ToArray();
        // Unknown or inaccessible group must not leak another CFO's group or appear as success.
        if (options.GroupId.HasValue && !groups.Any(g => g.Id == options.GroupId))
            throw new DomainRuleViolationException("The selected group is unavailable in this CFO scope.");
        var budgeted = UnbudgetedArticlesProjection.BudgetedInCurrentStructure(catalog, plan.Instances,
            plan.CreationTimes, ids, months, cancellationToken);
        var matching = UnbudgetedArticlesProjection.Select(catalog, budgeted, options);
        return new(plan.Version.Id, plan.Version.Name, options.FromMonth, options.ToMonth, !canSeeAll,
            options.Skip, options.Take, matching.Count, centers, groups,
            matching.Skip(options.Skip).Take(options.Take).ToArray());
    }
}
