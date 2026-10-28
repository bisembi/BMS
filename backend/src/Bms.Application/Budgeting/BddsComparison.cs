using System.Text.RegularExpressions;
using Bms.Domain.Common;

namespace Bms.Application.Budgeting;

public sealed record BddsReportOptions(DateOnly FromMonth, DateOnly ToMonth, bool IncludeActuals,
    Guid? VersionId = null, Guid? CompareVersionId = null, int ActualsYear = 2026)
{
    public static BddsReportOptions Annual(int year) => new(new(year, 1, 1), new(year, 12, 1), false);
    public IReadOnlyList<DateOnly> Months(int year)
    {
        if (year is < 2000 or > 2100 || FromMonth.Year != year || ToMonth.Year != year
            || FromMonth.Day != 1 || ToMonth.Day != 1 || FromMonth > ToMonth
            || !AccountingPeriod467.IsReportYear(ActualsYear)
            || VersionId == Guid.Empty || CompareVersionId == Guid.Empty
            || (VersionId.HasValue && VersionId == CompareVersionId))
            throw new DomainRuleViolationException("BDDS period is invalid.");
        var result = new List<DateOnly>();
        for (var month = FromMonth; month <= ToMonth; month = month.AddMonths(1)) result.Add(month);
        return result;
    }
}

public sealed record BddsPlanCell(Guid ItemId, string ActivityName, string SectionName, string GroupName,
    string BusinessCode, string ArticleName, DateOnly Month, decimal Amount, int Ordinal);
public sealed record BddsActualArticle(Guid ItemId, string Name, string ActivityName, Guid GroupId, string GroupName,
    Guid? CostCenterId = null);
public sealed record BddsActualCell(Guid ItemId, DateOnly Month, decimal Amount);
public sealed record BddsHierarchyItem(string ActivityType, Guid? GroupId, string GroupName,
    int? DisplayOrder = null, string? DisplayBlockId = null, int? CataloguePosition = null,
    string? FlowDirection = null);
public sealed record BddsActualsData(Guid SnapshotId, DateTimeOffset CapturedAtUtc,
    IReadOnlyList<DateOnly> AvailableMonths, IReadOnlyList<BddsActualArticle> Articles,
    IReadOnlyList<BddsActualCell> Cells, int SourceYear = 2026);

// A pure read projection: it never changes the sealed budget or actual snapshots.
public static class BddsComparison
{
    // Invoke only after verifying the entire sealed source. A null scope is the
    // full financial group; an empty scope is never an alias for that group.
    public static BddsActualsData ScopeActuals(BddsActualsData source, IReadOnlySet<Guid>? costCenterIds)
    {
        if (costCenterIds is null) return source;
        var articles = source.Articles.Where(article => article.CostCenterId is { } center
            && costCenterIds.Contains(center)).ToArray();
        var ids = articles.Select(article => article.ItemId).ToHashSet();
        return source with { Articles = articles, Cells = source.Cells.Where(cell => ids.Contains(cell.ItemId)).ToArray() };
    }

    public static BddsReportDto Build(BddsReportDto metadata, IReadOnlyList<BddsPlanCell> plan,
        BddsReportOptions options, bool canIncludeActuals, BddsActualsData? actuals,
        IReadOnlyDictionary<Guid, string>? currentArticleNames = null,
        IReadOnlyDictionary<Guid, string>? currentArticleCostCenterCodes = null,
        IReadOnlyDictionary<Guid, BddsHierarchyItem>? hierarchy = null)
    {
        var months = options.Months(metadata.BudgetYear);
        if (options.IncludeActuals && !canIncludeActuals)
            throw new DomainRuleViolationException("Actuals are unavailable for this report.");
        var sourceYear = actuals?.SourceYear;
        if (options.IncludeActuals && actuals is not null)
        {
            if (actuals.SourceYear != options.ActualsYear
                || actuals.AvailableMonths.Distinct().Count() != actuals.AvailableMonths.Count
                || actuals.AvailableMonths.Any(month => month.Year != options.ActualsYear || month.Day != 1)
                || actuals.Cells.Any(cell => cell.Month.Year != options.ActualsYear || cell.Month.Day != 1))
                throw new CashFlowActualsSourceInvalidException();
            // Calendar alignment is read-only. Source dates/snapshots are not rewritten.
            DateOnly ReportMonth(DateOnly month) => new(metadata.BudgetYear, month.Month, 1);
            actuals = actuals with
            {
                AvailableMonths = actuals.AvailableMonths.Select(ReportMonth).ToArray(),
                Cells = actuals.Cells.Select(cell => cell with { Month = ReportMonth(cell.Month) }).ToArray()
            };
        }
        var index = months.Select((month, position) => (month, position)).ToDictionary(x => x.month, x => x.position);
        var available = actuals?.AvailableMonths.ToHashSet() ?? [];
        var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        var routes = new Dictionary<Guid, string>();
        var actualDefinitions = actuals?.Articles.ToDictionary(row => row.ItemId) ?? [];
        string ArticleName(Guid id, string original) => currentArticleNames is not null
            && currentArticleNames.TryGetValue(id, out var current) && !string.IsNullOrWhiteSpace(current)
                ? current : original;
        string? ArticleCostCenterCode(Guid id) => currentArticleCostCenterCodes is not null
            && currentArticleCostCenterCodes.TryGetValue(id, out var current) && !string.IsNullOrWhiteSpace(current)
                ? current : null;
        Node Add(string key, string? parent, string kind, string name, int order,
            string? costCenterCode = null, int? displayOrder = null)
        {
            if (!nodes.TryGetValue(key, out var node))
                nodes.Add(key, node = new(key, parent, kind, Display(name), order, months.Count,
                    costCenterCode, displayOrder));
            return node;
        }
        // The current catalogue is the only production presentation authority.
        // Signed financial measures are never recomputed from classification.
        string CatalogueRoute(Guid itemId, string originalName, int ordinal)
        {
            if (hierarchy is null || !hierarchy.TryGetValue(itemId, out var identity))
                throw new CashFlowActualsMappingUnavailableException();
            RequireActivity(identity.ActivityType);
            if (identity.GroupId == Guid.Empty || (identity.GroupId.HasValue && string.IsNullOrWhiteSpace(identity.GroupName)))
                throw new CashFlowActualsMappingUnavailableException();
            var activityName = identity.ActivityType switch
            {
                "Operating" => "ОПЕРАЦИОННАЯ ДЕЯТЕЛЬНОСТЬ",
                "Investing" => "ИНВЕСТИЦИОННАЯ ДЕЯТЕЛЬНОСТЬ",
                "Financing" => "ФИНАНСОВАЯ ДЕЯТЕЛЬНОСТЬ",
                _ => throw new CashFlowActualsMappingUnavailableException()
            };
            var direction = identity.FlowDirection switch
            {
                "Receipt" => "Receipt", "Payment" => "Payment", null => "Unknown",
                _ => throw new CashFlowActualsMappingUnavailableException()
            };
            // Missing historical reference direction is explicit, not guessed
            // from an old section, a zero placeholder or the financial net.
            var sectionName = direction == "Unknown" ? "НАПРАВЛЕНИЕ НЕ ЗАДАНО"
                : Section(activityName, direction == "Receipt");
            var activity = $"activity|{identity.ActivityType}";
            var section = $"{activity}|section|{direction}";
            var group = identity.GroupId is { } parent ? $"{section}|group|{parent:D}" : $"{section}|no-group";
            var key = $"{group}|article|{itemId:D}";
            Add(activity, null, "Activity", activityName, ordinal);
            Add(section, activity, "Section", sectionName, ordinal);
            if (identity.GroupId is { } groupId)
                Add(group, section, "Group", identity.GroupName, ordinal).ReferenceGroupIds.Add(groupId);
            var article = Add(key, identity.GroupId.HasValue ? group : section, "Article", ArticleName(itemId, originalName), ordinal,
                ArticleCostCenterCode(itemId), identity.DisplayOrder);
            article.CashFlowItemId = itemId;
            article.DisplayGroupId = identity.GroupId;
            article.DisplayGroupName = identity.GroupId.HasValue ? Display(identity.GroupName) : null;
            article.DisplayBlockId = identity.GroupId.HasValue ? identity.DisplayBlockId : null;
            article.CataloguePosition = identity.CataloguePosition;
            return key;
        }
        // Compatibility callers without a catalogue retain their legacy keys.
        string? ExistingHierarchy(Func<Node, bool> eligible) => nodes.Values.Where(eligible)
            .OrderBy(node => node.Order).ThenBy(node => node.Key, StringComparer.Ordinal).FirstOrDefault()?.Key;
        string Route(BddsPlanCell cell)
        {
            if (hierarchy is not null)
            {
                var current = CatalogueRoute(cell.ItemId, $"{cell.BusinessCode} {cell.ArticleName}", cell.Ordinal);
                routes.TryAdd(cell.ItemId, current);
                return current;
            }
            var activity = $"activity|{cell.ActivityName}";
            var section = $"{activity}|section|{cell.SectionName}";
            var group = $"{section}|group|{cell.GroupName}";
            var item = $"{group}|article|{cell.BusinessCode}|{cell.ArticleName}";
            Add(activity, null, "Activity", cell.ActivityName, cell.Ordinal);
            Add(section, activity, "Section", cell.SectionName, cell.Ordinal);
            var groupNode = Add(group, section, "Group", cell.GroupName, cell.Ordinal);
            if (actualDefinitions.TryGetValue(cell.ItemId, out var definition)
                && definition.GroupId != Guid.Empty && Canonical(definition.GroupName) == Canonical(cell.GroupName))
                groupNode.ReferenceGroupIds.Add(definition.GroupId);
            // Current names are display-only. Historical keys/routes and GUID
            // identity remain unchanged even if two articles are renamed alike.
            var articleNode = Add(item, group, "Article", ArticleName(cell.ItemId, $"{cell.BusinessCode} {cell.ArticleName}"), cell.Ordinal,
                ArticleCostCenterCode(cell.ItemId));
            articleNode.CashFlowItemId = cell.ItemId;
            if (options.IncludeActuals && routes.TryGetValue(cell.ItemId, out var existing) && existing != item)
                throw new CashFlowActualsMappingUnavailableException();
            routes.TryAdd(cell.ItemId, item);
            return item;
        }
        void Accumulate(string key, int position, decimal amount, bool fact)
        {
            for (string? current = key; current is not null; current = nodes[current].Parent)
            {
                var node = nodes[current];
                if (fact) { node.HasActuals = true; node.Actuals[position] += amount; }
                else node.Plan[position] += amount;
            }
        }
        foreach (var cell in plan)
        {
            var route = Route(cell);
            if (index.TryGetValue(cell.Month, out var position)) Accumulate(route, position, cell.Amount, false);
        }
        var net = Add("net", null, "Net", "Чистый денежный поток", int.MaxValue);
        foreach (var cell in plan)
            if (index.TryGetValue(cell.Month, out var position)) net.Plan[position] += cell.Amount;

        if (options.IncludeActuals && actuals is not null)
        {
            var definitions = actuals.Articles.ToDictionary(row => row.ItemId);
            if (actuals.Cells.GroupBy(row => (row.ItemId, row.Month)).Any(group => group.Count() != 1)
                || actuals.Cells.Any(row => !definitions.ContainsKey(row.ItemId) || !available.Contains(row.Month)))
                throw new CashFlowActualsSourceInvalidException();
            foreach (var article in actuals.Articles)
            {
                var cells = actuals.Cells.Where(row => row.ItemId == article.ItemId).ToArray();
                if (cells.Length != available.Count) throw new CashFlowActualsSourceInvalidException();
                if (routes.TryGetValue(article.ItemId, out var route))
                {
                    foreach (var cell in cells)
                        if (index.TryGetValue(cell.Month, out var position)) Accumulate(route, position, cell.Amount, true);
                    // Even a period with no loaded months retains the article's coverage identity.
                    nodes[route].HasActuals = true;
                }
                else
                {
                    if (hierarchy is not null)
                    {
                        // Preserve the existing financial article universe: an
                        // all-zero actual-only definition has no report leaf.
                        // Planned zero cells and zero months of a nonzero
                        // actual article retain their explicit known coverage.
                        if (!cells.Any(cell => cell.Amount != 0m)) continue;
                        var current = CatalogueRoute(article.ItemId, article.Name, int.MaxValue - 1);
                        nodes[current].HasActuals = true;
                        foreach (var cell in cells)
                            if (index.TryGetValue(cell.Month, out var position)) Accumulate(current, position, cell.Amount, true);
                        continue;
                    }
                    if (article.GroupId == Guid.Empty || string.IsNullOrWhiteSpace(article.GroupName))
                        throw new CashFlowActualsMappingUnavailableException();
                    // NET cells are routed once by their observed sign; no gross movements are reconstructed.
                    foreach (var receipt in new[] { true, false })
                    {
                        if (!cells.Any(cell => receipt ? cell.Amount > 0 : cell.Amount < 0)) continue;
                        var activity = ExistingHierarchy(node => node.Kind == "Activity"
                            && Canonical(node.Name) == Canonical(article.ActivityName))
                            ?? $"actual|activity|{article.ActivityName}";
                        Add(activity, null, "Activity", article.ActivityName, int.MaxValue - 1);
                        var sectionName = Section(article.ActivityName, receipt);
                        var section = ExistingHierarchy(node => node.Parent == activity && node.Kind == "Section"
                            && Canonical(node.Name) == Canonical(sectionName))
                            ?? $"{activity}|actual-section|{(receipt ? "receipt" : "payment")}";
                        Add(section, activity, "Section", sectionName, int.MaxValue - 1);
                        var group = ExistingHierarchy(node => node.Parent == section && node.Kind == "Group"
                            && node.ReferenceGroupIds.SetEquals([article.GroupId]))
                            ?? $"{section}|actual-group|{article.GroupId:D}";
                        Add(group, section, "Group", article.GroupName, int.MaxValue - 1);
                        var key = $"{group}|actual-article|{article.ItemId:D}";
                        var articleNode = Add(key, group, "Article", ArticleName(article.ItemId, article.Name), int.MaxValue - 1,
                            ArticleCostCenterCode(article.ItemId));
                        articleNode.HasActuals = true;
                        articleNode.CashFlowItemId = article.ItemId;
                        foreach (var cell in cells)
                            if (index.TryGetValue(cell.Month, out var position))
                                Accumulate(key, position, receipt ? Math.Max(0m, cell.Amount) : Math.Min(0m, cell.Amount), true);
                    }
                }
            }
            net.HasActuals = true;
            foreach (var cell in actuals.Cells)
                if (index.TryGetValue(cell.Month, out var position)) net.Actuals[position] += cell.Amount;
            for (var position = 0; position < months.Count; position++)
                if (nodes.Values.Where(node => node.Kind == "Article").Sum(node => node.Actuals[position]) != net.Actuals[position])
                    throw new CashFlowActualsSourceInvalidException();
        }

        var output = new List<BddsReportRowDto>();
        void Visit(Node node)
        {
            var facts = months.Select((month, position) => available.Contains(month) && node.HasActuals
                ? (decimal?)Safe(node.Actuals[position]) : null).ToArray();
            // A missing future month is not a zero and must not hide known YTD fact.
            var total = options.IncludeActuals && facts.Any(value => value.HasValue) ? (decimal?)Safe(facts.Sum()!.Value) : null;
            var planTotal = Safe(node.Plan.Sum());
            var delta = options.IncludeActuals ? facts.Select((value, position) =>
                value.HasValue ? (decimal?)Safe(value.Value - node.Plan[position]) : null).ToArray() : null;
            output.Add(new(node.Key, node.Parent, node.Kind, node.Name, node.Plan.Select(Safe).ToArray(), planTotal)
            {
                CostCenterCode = node.CostCenterCode,
                DisplayOrder = node.DisplayOrder,
                CashFlowItemId = node.CashFlowItemId,
                DisplayGroupId = node.DisplayGroupId,
                DisplayGroupName = node.DisplayGroupName,
                DisplayBlockId = node.DisplayBlockId,
                CataloguePosition = node.CataloguePosition,
                ActualAmounts = options.IncludeActuals ? facts : null,
                ActualTotal = total,
                VarianceAmounts = delta,
                VarianceTotal = total.HasValue ? Safe(total.Value - planTotal) : null,
                VariancePercentages = delta?.Select((value, position) => Percentage(value, node.Plan[position])).ToArray(),
                VariancePercentageTotal = Percentage(total.HasValue ? total.Value - planTotal : null, planTotal)
            });
            foreach (var child in nodes.Values.Where(child => child.Parent == node.Key)
                .OrderBy(child => child.Order).ThenBy(child => child.Name, StringComparer.Ordinal)) Visit(child);
        }
        foreach (var root in nodes.Values.Where(node => node.Parent is null && node.Kind != "Net")
            .OrderBy(node => node.Order).ThenBy(node => node.Name, StringComparer.Ordinal)) Visit(root);
        Visit(net);
        var selectedPlan = plan.Where(cell => index.ContainsKey(cell.Month)).ToArray();
        var selectedActuals = actuals?.Cells.Where(cell => index.ContainsKey(cell.Month)).ToArray() ?? [];
        var hasAvailableActuals = options.IncludeActuals && actuals is not null && months.Any(available.Contains);
        var receipts = Safe(selectedPlan.Where(cell => cell.Amount > 0).Sum(cell => cell.Amount));
        var payments = Safe(-selectedPlan.Where(cell => cell.Amount < 0).Sum(cell => cell.Amount));
        return metadata with
        {
            Rows = output,
            Months = months,
            CanIncludeActuals = canIncludeActuals,
            IncludesActuals = options.IncludeActuals,
            ActualsYear = options.IncludeActuals ? options.ActualsYear : null,
            ActualsSourceYear = options.IncludeActuals ? sourceYear : null,
            ReceiptsAmountKzt = receipts,
            PaymentsAmountKzt = payments,
            NetCashFlowAmountKzt = Safe(receipts - payments),
            ActualsSnapshotId = options.IncludeActuals ? actuals?.SnapshotId : null,
            ActualsCapturedAtUtc = options.IncludeActuals ? actuals?.CapturedAtUtc : null,
            ActualsAvailableMonths = options.IncludeActuals ? months.Where(available.Contains).ToArray() : null,
            ActualReceiptsAmountKzt = hasAvailableActuals ? Safe(selectedActuals.Where(cell => cell.Amount > 0).Sum(cell => cell.Amount)) : null,
            ActualPaymentsAmountKzt = hasAvailableActuals ? Safe(-selectedActuals.Where(cell => cell.Amount < 0).Sum(cell => cell.Amount)) : null,
            ActualNetCashFlowAmountKzt = hasAvailableActuals ? Safe(selectedActuals.Sum(cell => cell.Amount)) : null
        };
    }

    private static decimal? Percentage(decimal? delta, decimal plan)
    {
        if (!delta.HasValue || plan == 0m) return null;
        var value = decimal.Round(delta.Value / Math.Abs(plan) * 100m, 1, MidpointRounding.AwayFromZero);
        return Math.Abs(value * 10m) <= 9007199254740991m ? value : null;
    }
    private static decimal Safe(decimal value) => Math.Abs(value) <= CashFlowActualsSource.MaximumSafeMoney
        && decimal.Round(value, 2) == value ? value : throw new CashFlowActualsSourceInvalidException();
    private static string Display(string name) => Regex.Replace(name.Trim(), @"^\d+(?:\.\d+)*\.\s*", "");
    private static string Canonical(string name) => Regex.Replace(Display(name), @"\s+", " ").ToUpperInvariant();
    private static string? ActivityType(string name) => Canonical(name) switch
    {
        "ОПЕРАЦИОННАЯ ДЕЯТЕЛЬНОСТЬ" => "Operating",
        "ИНВЕСТИЦИОННАЯ ДЕЯТЕЛЬНОСТЬ" => "Investing",
        "ФИНАНСОВАЯ ДЕЯТЕЛЬНОСТЬ" => "Financing",
        _ => null
    };
    private static void RequireActivity(string type)
    {
        if (type is not ("Operating" or "Investing" or "Financing"))
            throw new CashFlowActualsMappingUnavailableException();
    }
    private static string? SectionKind(string name, string activityType)
    {
        var value = Canonical(name);
        var activity = activityType switch
        {
            "Operating" => "ОПЕРАЦИОННОЙ",
            "Investing" => "ИНВЕСТИЦИОННОЙ",
            "Financing" => "ФИНАНСОВОЙ",
            _ => throw new CashFlowActualsMappingUnavailableException()
        };
        if (value == $"ПОСТУПЛЕНИЯ ПО {activity} ДЕЯТЕЛЬНОСТИ") return "Receipt";
        if (value == $"ВЫПЛАТЫ ПО {activity} ДЕЯТЕЛЬНОСТИ") return "Payment";
        if (value == $"ПОСТУПЛЕНИЯ И ВЫПЛАТЫ ПО {activity} ДЕЯТЕЛЬНОСТИ") return "Mixed";
        return null;
    }
    private static string Section(string activity, bool receipt) => (receipt ? "ПОСТУПЛЕНИЯ ПО " : "ВЫПЛАТЫ ПО ")
        + (Canonical(activity) switch
        {
            "ОПЕРАЦИОННАЯ ДЕЯТЕЛЬНОСТЬ" => "ОПЕРАЦИОННОЙ ДЕЯТЕЛЬНОСТИ",
            "ИНВЕСТИЦИОННАЯ ДЕЯТЕЛЬНОСТЬ" => "ИНВЕСТИЦИОННОЙ ДЕЯТЕЛЬНОСТИ",
            "ФИНАНСОВАЯ ДЕЯТЕЛЬНОСТЬ" => "ФИНАНСОВОЙ ДЕЯТЕЛЬНОСТИ",
            _ => throw new CashFlowActualsMappingUnavailableException()
        });
    private sealed class Node(string key, string? parent, string kind, string name, int order, int count,
        string? costCenterCode, int? displayOrder)
    {
        internal string Key { get; } = key;
        internal string? Parent { get; } = parent;
        internal string Kind { get; } = kind;
        internal string Name { get; } = name;
        internal string? CostCenterCode { get; } = costCenterCode;
        internal int? DisplayOrder { get; } = displayOrder;
        internal Guid? CashFlowItemId { get; set; }
        internal Guid? DisplayGroupId { get; set; }
        internal string? DisplayGroupName { get; set; }
        internal string? DisplayBlockId { get; set; }
        internal int? CataloguePosition { get; set; }
        internal int Order { get; } = order;
        internal decimal[] Plan { get; } = new decimal[count];
        internal decimal[] Actuals { get; } = new decimal[count];
        internal bool HasActuals { get; set; }
        internal HashSet<Guid> ReferenceGroupIds { get; } = [];
    }
}
