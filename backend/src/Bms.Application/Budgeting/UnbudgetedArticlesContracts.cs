using Bms.Domain.Common;
using Bms.Domain.Budgeting;

namespace Bms.Application.Budgeting;

public sealed record UnbudgetedArticlesOptions(DateOnly FromMonth, DateOnly ToMonth,
    Guid? CostCenterId = null, Guid? GroupId = null, bool OnlyUngrouped = false,
    Guid? VersionId = null, int Skip = 0, int Take = 25)
{
    public IReadOnlyList<DateOnly> Months()
    {
        if (FromMonth.Year is < 2000 or > 2100 || ToMonth.Year is < 2000 or > 2100
            || FromMonth.Day != 1 || ToMonth.Day != 1 || FromMonth > ToMonth
            || CostCenterId == Guid.Empty || GroupId == Guid.Empty || VersionId == Guid.Empty
            || (OnlyUngrouped && GroupId.HasValue) || Skip < 0 || Skip > int.MaxValue - 100
            || Take is < 1 or > 100)
            throw new DomainRuleViolationException("Unbudgeted articles filter is invalid.");
        var months = new List<DateOnly>();
        for (var month = FromMonth; month <= ToMonth; month = month.AddMonths(1)) months.Add(month);
        return months;
    }
}

public sealed record UnbudgetedArticleDto(Guid Id, string Name, string ActivityType,
    Guid CostCenterId, string CostCenterCode, string CostCenterName,
    Guid? GroupId, string GroupName, decimal PlanAmountKzt, int? DisplayOrder);
public sealed record UnbudgetedGroupDto(Guid Id, string Name);
public sealed record UnbudgetedArticlesReportDto(Guid VersionId, string VersionName,
    DateOnly FromMonth, DateOnly ToMonth, bool ScopeLimited, int Skip, int Take, int Total,
    IReadOnlyList<BddsCostCenterDto> CostCenters, IReadOnlyList<UnbudgetedGroupDto> Groups,
    IReadOnlyList<UnbudgetedArticleDto> Items);

public static class UnbudgetedArticlesProjection
{
    public static IReadOnlySet<(Guid CenterId, Guid ArticleId)> Budgeted(
        IReadOnlyCollection<BudgetInstance> instances,
        IReadOnlyDictionary<Guid, DateTimeOffset?> creationTimes,
        IEnumerable<Guid> centers, IReadOnlyList<DateOnly> months, CancellationToken cancellationToken,
        IReadOnlySet<Guid>? articleIds = null)
    {
        var result = new HashSet<(Guid CenterId, Guid ArticleId)>();
        foreach (var center in centers)
        foreach (var month in months)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Do not resolve unrelated draft overlaps outside the filtered catalog.
            // Still select the whole winning header, never a winner per article.
            if (articleIds is not null && !instances.Any(instance => instance.CostCenterId == center
                && instance.ValidFrom <= month && instance.ValidTo >= month
                && instance.Lines.Any(line => line.BudgetMonth == month && articleIds.Contains(line.CashFlowItemId))))
                continue;
            var winner = CompanyBudgetReportSelection.Winner(instances, center, month, creationTimes);
            if (winner is null) continue;
            foreach (var line in winner.Lines.Where(line => line.BudgetMonth == month
                && (articleIds is null || articleIds.Contains(line.CashFlowItemId))))
            {
                MoneyAmountPolicy.EnsureNonNegative(line.LimitAmountKzt, "Budgeted article limit");
                if (line.LimitAmountKzt > 0m) result.Add((center, line.CashFlowItemId));
            }
        }
        return result;
    }

    // Inputs contain ONLY the selected version. Original CFO scope is enforced before
    // projecting permanent article IDs into the current catalog's display CFO.
    public static IReadOnlySet<(Guid CenterId, Guid ArticleId)> BudgetedInCurrentStructure(
        IReadOnlyList<UnbudgetedArticleDto> catalog, IReadOnlyCollection<BudgetInstance> instances,
        IReadOnlyDictionary<Guid, DateTimeOffset?> creationTimes,
        IReadOnlyCollection<Guid> accessibleSourceCenters, IReadOnlyList<DateOnly> months,
        CancellationToken cancellationToken)
    {
        var articleIds = catalog.Select(row => row.Id).ToHashSet();
        var budgetedIds = Budgeted(instances, creationTimes, accessibleSourceCenters, months,
            cancellationToken, articleIds).Select(key => key.ArticleId).ToHashSet();
        return catalog.Where(row => budgetedIds.Contains(row.Id))
            .Select(row => (row.CostCenterId, row.Id)).ToHashSet();
    }

    // A positive raw limit is budgeted, even if its signed net or displayed rounded amount is zero.
    public static IReadOnlyList<UnbudgetedArticleDto> Select(
        IEnumerable<UnbudgetedArticleDto> catalog,
        IReadOnlySet<(Guid CenterId, Guid ArticleId)> budgeted,
        UnbudgetedArticlesOptions options) => catalog
        .Where(row => !budgeted.Contains((row.CostCenterId, row.Id))
            && (!options.GroupId.HasValue || row.GroupId == options.GroupId)
            && (!options.OnlyUngrouped || row.GroupId is null))
        .OrderBy(row => row.CostCenterCode, StringComparer.Ordinal)
        .ThenBy(row => row.DisplayOrder ?? int.MaxValue)
        .ThenBy(row => row.GroupName, StringComparer.Ordinal)
        .ThenBy(row => row.Name, StringComparer.Ordinal).ThenBy(row => row.Id).ToArray();
}
