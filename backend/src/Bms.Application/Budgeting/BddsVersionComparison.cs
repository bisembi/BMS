namespace Bms.Application.Budgeting;

public static class BddsVersionComparison
{
    public static BddsReportDto Attach(BddsReportDto primary, BddsReportDto comparison)
    {
        if (primary.BudgetYear != comparison.BudgetYear || primary.Months is null || comparison.Months is null
            || !primary.Months.SequenceEqual(comparison.Months)) throw new CompanyBudgetPlanUnavailableException();
        var byKey = comparison.Rows.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var rows = primary.Rows.Select(row =>
        {
            var values = byKey.TryGetValue(row.Key, out var match) ? match.Amounts : Enumerable.Repeat(0m, row.Amounts.Count).ToArray();
            if (values.Count != row.Amounts.Count) throw new CompanyBudgetPlanUnavailableException();
            var deltas = values.Select((value, i) => Safe(value - row.Amounts[i])).ToArray();
            var total = Safe(values.Sum());
            var deltaTotal = Safe(total - row.Total);
            return row with { CompareAmounts = values, CompareTotal = total, VersionVarianceAmounts = deltas,
                VersionVarianceTotal = deltaTotal,
                VersionVariancePercentages = deltas.Select((value, i) => Percentage(value, row.Amounts[i])).ToArray(),
                VersionVariancePercentageTotal = Percentage(deltaTotal, row.Total) };
        }).ToArray();
        return primary with { Rows = BddsDisplayHierarchy.RemoveMatchingGroupWrappers(MergeDisplayGroups(BddsDisplayHierarchy.MergeZeroTemplates(rows))), CompareVersion = comparison.PlanVersion,
            CompareReceiptsAmountKzt = comparison.ReceiptsAmountKzt, ComparePaymentsAmountKzt = comparison.PaymentsAmountKzt,
            CompareNetCashFlowAmountKzt = comparison.NetCashFlowAmountKzt };
    }
    // Display projection only: historic group IDs/routes remain intact in the
    // sources. Within one exact activity/direction branch a label is shown once.
    private static IReadOnlyList<BddsReportRowDto> MergeDisplayGroups(BddsReportRowDto[] rows)
    {
        static string Label(string name) => System.Text.RegularExpressions.Regex.Replace(
            name.Normalize(System.Text.NormalizationForm.FormC).Trim(), @"\s+", " ").ToUpperInvariant();
        var buckets = rows.Where(r => r.Kind == "Group")
            .GroupBy(r => (r.ParentKey, Label(r.Name))).Where(g => g.Count() > 1).ToArray();
        if (buckets.Length == 0) return rows;
        if (rows.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count() != rows.Length)
            throw new CompanyBudgetPlanUnavailableException();
        var remap = new Dictionary<string, string>(StringComparer.Ordinal);
        var merged = new Dictionary<string, BddsReportRowDto>(StringComparer.Ordinal);
        foreach (var bucket in buckets)
        {
            if (bucket.Key.ParentKey is null || string.IsNullOrEmpty(bucket.Key.Item2))
                throw new CompanyBudgetPlanUnavailableException();
            var parts = bucket.ToArray();
            var head = parts.OrderBy(r => r.Key, StringComparer.Ordinal).First();
            var length = head.Amounts.Count;
            if (parts.Any(r => r.Amounts.Count != length || r.CompareAmounts?.Count != length
                || (r.ActualAmounts is not null && r.ActualAmounts.Count != length)))
                throw new CompanyBudgetPlanUnavailableException();
            var amounts = Enumerable.Range(0, length).Select(i => Safe(parts.Sum(r => r.Amounts[i]))).ToArray();
            var compare = Enumerable.Range(0, length).Select(i => Safe(parts.Sum(r => r.CompareAmounts![i]))).ToArray();
            var total = Safe(amounts.Sum()); var compareTotal = Safe(compare.Sum());
            var actual = parts.Any(r => r.ActualAmounts is not null)
                ? Enumerable.Range(0, length).Select(i =>
                {
                    var known = parts.Where(r => r.ActualAmounts?[i] is not null).ToArray();
                    return known.Length == 0 ? (decimal?)null : Safe(known.Sum(r => r.ActualAmounts![i]!.Value));
                }).ToArray() : null;
            var actualTotal = actual is not null && actual.Any(v => v.HasValue) ? (decimal?)Safe(actual.Sum(v => v ?? 0m)) : null;
            var versionDeltas = compare.Select((value, i) => Safe(value - amounts[i])).ToArray();
            var versionDeltaTotal = Safe(compareTotal - total);
            var deltas = actual?.Select((value, i) => value.HasValue ? (decimal?)Safe(value.Value - amounts[i]) : null).ToArray();
            merged.Add(head.Key, head with { Amounts = amounts, Total = total,
                CompareAmounts = compare, CompareTotal = compareTotal,
                VersionVarianceAmounts = versionDeltas, VersionVarianceTotal = versionDeltaTotal,
                VersionVariancePercentages = versionDeltas.Select((v, i) => Percentage(v, amounts[i])).ToArray(),
                VersionVariancePercentageTotal = Percentage(versionDeltaTotal, total),
                ActualAmounts = actual, ActualTotal = actualTotal, VarianceAmounts = deltas,
                VarianceTotal = actualTotal.HasValue ? Safe(actualTotal.Value - total) : null,
                VariancePercentages = deltas?.Select((v, i) => v.HasValue ? Percentage(v.Value, amounts[i]) : null).ToArray(),
                VariancePercentageTotal = actualTotal.HasValue ? Percentage(actualTotal.Value - total, total) : null });
            foreach (var part in parts) remap.Add(part.Key, head.Key);
        }
        // Rebuild depth-first order, keeping existing sibling/source order. A
        // moved article must stay below its one group for collapse and exports.
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var projected = rows.Select(r => remap.TryGetValue(r.Key, out var key) ? merged[key] : r)
            .Where(r => emitted.Add(r.Key))
            .Select(r => r.ParentKey is not null && remap.TryGetValue(r.ParentKey, out var parent)
                ? r with { ParentKey = parent } : r).ToArray();
        var keys = projected.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        if (projected.Any(r => r.ParentKey is not null && !keys.Contains(r.ParentKey)))
            throw new CompanyBudgetPlanUnavailableException();
        var children = projected.Where(r => r.ParentKey is not null).ToLookup(r => r.ParentKey!, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<BddsReportRowDto>();
        void Visit(BddsReportRowDto row)
        {
            if (!seen.Add(row.Key)) throw new CompanyBudgetPlanUnavailableException();
            ordered.Add(row); foreach (var child in children[row.Key]) Visit(child);
        }
        foreach (var row in projected.Where(r => r.ParentKey is null)) Visit(row);
        if (ordered.Count != projected.Length) throw new CompanyBudgetPlanUnavailableException();
        return ordered;
    }
    private static decimal Safe(decimal value) => decimal.Round(value, 2) == value && Math.Abs(value) <= CashFlowActualsSource.MaximumSafeMoney
        ? value : throw new CashFlowActualsSourceInvalidException();
    private static decimal? Percentage(decimal delta, decimal baseline)
    {
        if (baseline == 0m) return null;
        var value = decimal.Round(delta / Math.Abs(baseline) * 100m, 1, MidpointRounding.AwayFromZero);
        return Math.Abs(value * 10m) <= 9007199254740991m ? value : null;
    }
}
