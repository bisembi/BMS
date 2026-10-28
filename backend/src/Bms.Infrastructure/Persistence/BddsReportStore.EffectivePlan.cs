using Bms.Application.Budgeting;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed partial class BddsReportStore
{
    private async Task<(BddsSnapshotRow[] Primary, BddsSnapshotRow[]? Comparison)> ReadEffectivePlansAsync(
        BddsReportOptions options, Guid[] permittedCenters, CompanyBudgetPlanSnapshot primary,
        CompanyBudgetPlanSnapshot? comparison, BddsSnapshotRow[] templates, CancellationToken cancellationToken)
    {
        if (permittedCenters.Length == 0) throw new Bms.Application.Common.ActorForbiddenException();
        // Scope is captured inside the existing Serializable attempt. Select
        // the whole winning header before filtering any article, exactly as
        // expense limits do. Never revive missing lines from an older version.
        var months = options.Months(options.FromMonth.Year);
        var first = options.FromMonth;
        (Guid Center, BudgetInstanceLine Line)[] Select(CompanyBudgetPlanSnapshot plan)
        {
            var result = new List<(Guid Center, BudgetInstanceLine Line)>();
            foreach (var center in permittedCenters.Order())
            foreach (var month in months)
            {
                var winner = CompanyBudgetReportSelection.Winner(plan.Instances, center, month, plan.CreationTimes);
                if (winner is not null)
                    result.AddRange(winner.Lines.Where(line => line.BudgetMonth == month)
                        .Select(line => (center, line)));
            }
            return result.ToArray();
        }
        var primaryEntries = Select(primary);
        var comparisonEntries = comparison is null ? null : Select(comparison);
        // Build one financial cell universe by permanent UUID, before either
        // version is aggregated. Template captions below are audit scaffolding,
        // not the presentation authority: the fresh catalogue map in ReadPeriod
        // always supplies the final activity/group/direction/order for A/B/fact.
        var selected = primaryEntries.Concat(comparisonEntries ?? []).ToArray();
        var ids = selected.Select(x => x.Line.CashFlowItemId).Distinct().ToArray();
        var items = await db.CashFlowItems.AsNoTracking().Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (items.Count != ids.Length) throw new InvalidOperationException("Approved budget article is missing.");
        var parentIds = items.Values.Where(x => x.ParentId.HasValue).Select(x => x.ParentId!.Value).Distinct().ToArray();
        var parents = await db.CashFlowItems.AsNoTracking().Where(x => parentIds.Contains(x.Id) && x.IsGroup)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        // Retain zero-only imported hierarchy/identity for fact routing. These
        // are fresh projection objects, never tracked/persisted snapshot rows.
        var ordinal = templates.Length == 0 ? 1 : templates.Max(x => x.SourceOrdinal) + 1;
        var routes = templates.GroupBy(x => x.CashFlowItemId).ToDictionary(x => x.Key,
            x => x.OrderBy(row => row.SourceOrdinal).First());
        foreach (var entry in selected.OrderBy(x => x.Line.BudgetMonth).ThenBy(x => x.Line.CashFlowItemId)
            .ThenBy(x => x.Center).ThenBy(x => x.Line.SortOrder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = entry.Line;
            MoneyAmountPolicy.EnsureNonNegative(line.LimitAmountKzt, "Approved plan amount");
            if (!routes.TryGetValue(line.CashFlowItemId, out var route))
            {
                var item = items[line.CashFlowItemId];
                var activity = item.ActivityType switch
                {
                    "Operating" => "ОПЕРАЦИОННАЯ ДЕЯТЕЛЬНОСТЬ",
                    "Investing" => "ИНВЕСТИЦИОННАЯ ДЕЯТЕЛЬНОСТЬ",
                    "Financing" => "ФИНАНСОВАЯ ДЕЯТЕЛЬНОСТЬ",
                    _ => throw new InvalidOperationException("Approved article activity is invalid.")
                };
                // Do not infer classification from a net balance. A new UUID
                // can legitimately have both directions; keep one fact route
                // and label that route explicitly, preserving both gross sums.
                var directions = BddsPlanRouteDirections.FromOriginalLines(selected.Where(x => x.Line.CashFlowItemId == item.Id)
                    .Select(x => (x.Line.FlowDirection, x.Line.LimitAmountKzt)));
                var section = directions.Length > 1 ? "ПОСТУПЛЕНИЯ И ВЫПЛАТЫ ПО "
                    : directions[0] == BudgetFlowDirection.Receipt ? "ПОСТУПЛЕНИЯ ПО " : "ВЫПЛАТЫ ПО ";
                // Imported route codes and directory codes are different
                // namespaces. Use the permanent UUID for new projection keys,
                // not any code/name. The UI label still comes from the directory.
                route = new BddsSnapshotRow { CashFlowItemId = item.Id, ActivityName = activity,
                    SectionName = section + activity.Replace("АЯ ДЕЯТЕЛЬНОСТЬ", "ОЙ ДЕЯТЕЛЬНОСТИ"),
                    GroupName = item.ParentId is { } parent && parents.TryGetValue(parent, out var group) ? group.Name : "Без группы",
                    ArticleBusinessCode = "approved-budget", ArticleName = item.Id.ToString("D"), SourceOrdinal = ordinal++ };
                routes.Add(item.Id, route);
            }
        }
        BddsSnapshotRow[] Project((Guid Center, BudgetInstanceLine Line)[] entries)
        {
            var output = templates.Select(x => CopyRoute(x, x.MappedCostCenterId, x.BudgetMonth, 0m)).ToList();
            output.AddRange(routes.Values.Where(route => !templates.Any(x => x.CashFlowItemId == route.CashFlowItemId))
                .Select(route => CopyRoute(route, permittedCenters[0], first, 0m)));
            foreach (var entry in entries)
            {
                var line = entry.Line;
                var amount = line.FlowDirection == BudgetFlowDirection.Receipt ? line.LimitAmountKzt : -line.LimitAmountKzt;
                output.Add(CopyRoute(routes[line.CashFlowItemId], entry.Center, line.BudgetMonth, amount));
            }
            return output.ToArray();
        }
        return (Project(primaryEntries), comparisonEntries is null ? null : Project(comparisonEntries));
    }

    private static BddsSnapshotRow CopyRoute(BddsSnapshotRow route, Guid center, DateOnly month, decimal amount) => new()
    {
        CashFlowItemId = route.CashFlowItemId, MappedCostCenterId = center, BudgetMonth = month,
        SignedAmountKzt = amount, SourceOrdinal = route.SourceOrdinal, ActivityName = route.ActivityName,
        SectionName = route.SectionName, GroupName = route.GroupName,
        ArticleBusinessCode = route.ArticleBusinessCode, ArticleName = route.ArticleName
    };
}
