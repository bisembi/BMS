using System.Text.RegularExpressions;
namespace Bms.Application.Budgeting;
internal static class BddsDisplayHierarchy
{
    private static string Label(string name)=>Regex.Replace(name.Normalize(System.Text.NormalizationForm.FormC).Trim(),@"\s+"," ").ToUpperInvariant();
    private static readonly Regex Verified=new(@"^activity\|(Operating|Investing|Financing)\|section\|(Receipt|Payment)\|group\|([0-9a-fA-F-]{36})$");
    internal static BddsReportRowDto[] MergeZeroTemplates(BddsReportRowDto[] rows)
    {
        var keys=rows.ToDictionary(r=>r.Key,StringComparer.Ordinal);
        var children=rows.Where(r=>r.ParentKey is not null).ToLookup(r=>r.ParentKey!,StringComparer.Ordinal);
        var families=new List<(BddsReportRowDto Row,string Activity,Guid Id)>();
        foreach(var row in rows.Where(r=>r.Kind=="Group"))
        {
            var m=Verified.Match(row.Key);if(!m.Success||!Guid.TryParseExact(m.Groups[3].Value,"D",out var id)||id==Guid.Empty)continue;
            var activity="activity|"+m.Groups[1].Value;var section=activity+"|section|"+m.Groups[2].Value;
            if(row.ParentKey!=section||!keys.TryGetValue(section,out var s)||s.Kind!="Section"||s.ParentKey!=activity||!keys.TryGetValue(activity,out var a)||a.Kind!="Activity"||a.ParentKey is not null)continue;
            families.Add((row,activity,id));
        }
        var moved=new Dictionary<string,string>(StringComparer.Ordinal);var affected=new HashSet<string>(StringComparer.Ordinal);
        foreach(var family in families.GroupBy(f=>(f.Activity,f.Id,Name:Label(f.Row.Name))))
        {
            var groups=family.Select(f=>f.Row).ToArray();
            var targets=groups.Where(g=>g.Amounts.Any(v=>v!=0)||g.CompareAmounts?.Any(v=>v!=0)==true).ToArray();
            if(targets.Length!=1)continue;var target=targets[0];
            foreach(var group in groups.Where(g=>g.Key!=target.Key&&g.ParentKey!=target.ParentKey))
            {
                var leaves=children[group.Key].ToArray();
                if(leaves.Length==0||leaves.Any(r=>r.Kind!="Article"||children[r.Key].Any()||r.Amounts.Any(v=>v!=0)||r.CompareAmounts is null||r.CompareAmounts.Any(v=>v!=0)||r.ActualAmounts?.Any(v=>v.HasValue&&v.Value!=0)==true)
                    || !Coherent(group,leaves))continue;
                var targetChildren=children[target.Key].ToArray();
                if(targetChildren.Length==0||targetChildren.Any(r=>r.Kind!="Article"||children[r.Key].Any())||!Coherent(target,targetChildren))continue;
                var oldSection=keys[group.ParentKey!];var targetSection=keys[target.ParentKey!];
                if(!Coherent(oldSection,children[oldSection.Key].ToArray())||!Coherent(targetSection,children[targetSection.Key].ToArray()))continue;
                moved[group.Key]=target.ParentKey!;affected.Add(group.ParentKey!);affected.Add(target.ParentKey!);
            }
        }
        if(moved.Count==0)return rows;
        var projected=rows.Select(r=>moved.TryGetValue(r.Key,out var parent)?r with {ParentKey=parent}:r).ToArray();
        var next=projected.Where(r=>r.ParentKey is not null).ToLookup(r=>r.ParentKey!,StringComparer.Ordinal);
        return Order(projected.Where(r=>!affected.Contains(r.Key)||next[r.Key].Any()).Select(r=>affected.Contains(r.Key)?Aggregate(r,next[r.Key].ToArray()):r).ToArray());
    }
    internal static IReadOnlyList<BddsReportRowDto> RemoveMatchingGroupWrappers(IReadOnlyList<BddsReportRowDto> rows)
    {
        var keys=rows.ToDictionary(r=>r.Key,StringComparer.Ordinal);var children=rows.Where(r=>r.ParentKey is not null).ToLookup(r=>r.ParentKey!,StringComparer.Ordinal);
        var removed=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var group in rows.Where(r=>r.Kind=="Group"))
        {
            if(group.ParentKey is null||!keys.TryGetValue(group.ParentKey,out var section)||section.Kind!="Section")continue;
            if(Label(group.Name)!=Label(section.Name))
            {
                // A zero-plan template may repeat a sibling direction heading. Flatten only
                // its display wrapper; keep every article in its ORIGINAL financial branch.
                var m=Verified.Match(group.Key);
                if(!m.Success||!Guid.TryParseExact(m.Groups[3].Value,"D",out var id)||id==Guid.Empty||group.Amounts.Any(v=>v!=0)||group.CompareAmounts is null||group.CompareAmounts.Any(v=>v!=0)||group.Total!=0||group.CompareTotal!=0)continue;
                var activity="activity|"+m.Groups[1].Value;
                if(section.Key!=activity+"|section|"+m.Groups[2].Value||section.ParentKey!=activity||!keys.TryGetValue(activity,out var a)||a.Kind!="Activity"||a.ParentKey is not null)continue;
                var matches=rows.Where(r=>r.Kind=="Section"&&r.ParentKey==activity&&r.Key!=section.Key&&Label(r.Name)==Label(group.Name)).ToArray();
                if(matches.Length!=1||matches[0].Key!=activity+"|section|"+(m.Groups[2].Value=="Payment"?"Receipt":"Payment"))continue;
            }
            var leaves=children[group.Key].ToArray();var siblings=children[section.Key].ToArray();
            if(leaves.Length==0||leaves.Any(r=>r.Kind!="Article"||children[r.Key].Any())||siblings.Any(r=>r.Kind is not ("Group" or "Article"))||!Coherent(group,leaves)||!Coherent(section,siblings))continue;
            removed[group.Key]=section.Key;
        }
        if(removed.Count==0)return rows;
        return Order(rows.Where(r=>!removed.ContainsKey(r.Key)).Select(r=>r.ParentKey is not null&&removed.TryGetValue(r.ParentKey,out var parent)?r with {ParentKey=parent}:r).ToArray());
    }
    private static bool Same<T>(IReadOnlyList<T>? a,IReadOnlyList<T>? b)=>a is null?b is null:b is not null&&a.SequenceEqual(b);
    private static bool Coherent(BddsReportRowDto row,BddsReportRowDto[] parts)
    {
        if(parts.Length==0||parts.Any(r=>r.Amounts.Count!=row.Amounts.Count||r.CompareAmounts?.Count!=row.Amounts.Count||(r.ActualAmounts is not null&&r.ActualAmounts.Count!=row.Amounts.Count)))return false;
        var sum=Aggregate(row,parts);
        return Same(row.Amounts,sum.Amounts)&&row.Total==sum.Total&&Same(row.CompareAmounts,sum.CompareAmounts)&&row.CompareTotal==sum.CompareTotal&&Same(row.ActualAmounts,sum.ActualAmounts)&&row.ActualTotal==sum.ActualTotal
            &&Same(row.VersionVarianceAmounts,sum.VersionVarianceAmounts)&&row.VersionVarianceTotal==sum.VersionVarianceTotal&&Same(row.VersionVariancePercentages,sum.VersionVariancePercentages)&&row.VersionVariancePercentageTotal==sum.VersionVariancePercentageTotal
            &&Same(row.VarianceAmounts,sum.VarianceAmounts)&&row.VarianceTotal==sum.VarianceTotal&&Same(row.VariancePercentages,sum.VariancePercentages)&&row.VariancePercentageTotal==sum.VariancePercentageTotal;
    }
    private static BddsReportRowDto Aggregate(BddsReportRowDto head,BddsReportRowDto[] parts)
    {
        var n=head.Amounts.Count;
        var amounts=Enumerable.Range(0,n).Select(i=>Safe(parts.Sum(r=>r.Amounts[i]))).ToArray();var compare=Enumerable.Range(0,n).Select(i=>Safe(parts.Sum(r=>r.CompareAmounts![i]))).ToArray();
        var total=Safe(amounts.Sum());var compareTotal=Safe(compare.Sum());
        var actual=parts.Any(r=>r.ActualAmounts is not null)?Enumerable.Range(0,n).Select(i=>{var known=parts.Where(r=>r.ActualAmounts?[i] is not null).ToArray();return known.Length==0?(decimal?)null:Safe(known.Sum(r=>r.ActualAmounts![i]!.Value));}).ToArray():null;
        var actualTotal=actual is not null&&actual.Any(v=>v.HasValue)?(decimal?)Safe(actual.Sum(v=>v??0m)):null;
        var versionDeltas=compare.Select((v,i)=>Safe(v-amounts[i])).ToArray();var versionTotal=Safe(compareTotal-total);var deltas=actual?.Select((v,i)=>v.HasValue?(decimal?)Safe(v.Value-amounts[i]):null).ToArray();
        return head with {Amounts=amounts,Total=total,CompareAmounts=compare,CompareTotal=compareTotal,ActualAmounts=actual,ActualTotal=actualTotal,VersionVarianceAmounts=versionDeltas,VersionVarianceTotal=versionTotal,
            VersionVariancePercentages=versionDeltas.Select((v,i)=>Percent(v,amounts[i])).ToArray(),VersionVariancePercentageTotal=Percent(versionTotal,total),VarianceAmounts=deltas,VarianceTotal=actualTotal.HasValue?Safe(actualTotal.Value-total):null,
            VariancePercentages=deltas?.Select((v,i)=>v.HasValue?Percent(v.Value,amounts[i]):null).ToArray(),VariancePercentageTotal=actualTotal.HasValue?Percent(actualTotal.Value-total,total):null};
    }
    private static BddsReportRowDto[] Order(BddsReportRowDto[] rows)
    {
        var keys=rows.Select(r=>r.Key).ToHashSet(StringComparer.Ordinal);if(keys.Count!=rows.Length||rows.Any(r=>r.ParentKey is not null&&!keys.Contains(r.ParentKey)))throw new CompanyBudgetPlanUnavailableException();
        var children=rows.Where(r=>r.ParentKey is not null).ToLookup(r=>r.ParentKey!,StringComparer.Ordinal);var ordered=new List<BddsReportRowDto>();var seen=new HashSet<string>(StringComparer.Ordinal);
        void Visit(BddsReportRowDto r){if(!seen.Add(r.Key))throw new CompanyBudgetPlanUnavailableException();ordered.Add(r);foreach(var child in children[r.Key])Visit(child);}
        foreach(var root in rows.Where(r=>r.ParentKey is null))Visit(root);if(ordered.Count!=rows.Length)throw new CompanyBudgetPlanUnavailableException();return ordered.ToArray();
    }
    private static decimal Safe(decimal v)=>decimal.Round(v,2)==v&&Math.Abs(v)<=CashFlowActualsSource.MaximumSafeMoney?v:throw new CashFlowActualsSourceInvalidException();
    private static decimal? Percent(decimal delta,decimal basis){if(basis==0)return null;var v=decimal.Round(delta/Math.Abs(basis)*100m,1,MidpointRounding.AwayFromZero);return Math.Abs(v*10m)<=9007199254740991m?v:null;}
}
