using Bms.Domain.Budgeting;
using Bms.Domain.Common;

namespace Bms.Application.Budgeting;

public static class CompanyBudgetOverlapMonths
{
    public const int MaximumWarnings = 12000;
    public const int MaximumWarningReferences = 60000;
    public static IReadOnlyList<(Guid CostCenterId, DateOnly Month)> Read(IReadOnlyCollection<BudgetInstance> headers)
    {
        var result = new List<(Guid, DateOnly)>();
        long references = 0;
        static int Index(DateOnly date) => (date.Year - 1) * 12 + date.Month - 1;
        foreach (var center in headers.GroupBy(x => x.CostCenterId))
        {
            if (center.Count() < 2) continue;
            var changes = new SortedDictionary<int, int>();
            foreach (var header in center)
            {
                var start = Index(header.ValidFrom); var end = Index(header.ValidTo) + 1;
                changes[start] = changes.GetValueOrDefault(start) + 1;
                changes[end] = changes.GetValueOrDefault(end) - 1;
            }
            var coverage = 0; int? previous = null;
            foreach (var change in changes)
            {
                if (coverage > 1 && previous.HasValue)
                {
                    var count = change.Key - previous.Value;
                    if (count > MaximumWarnings - result.Count)
                        throw new DomainRuleViolationException("Состав содержит более 12 000 предупреждений о перекрытии месяцев; сократите перекрывающиеся документы.");
                    var occurrences = (long)coverage * count;
                    if (occurrences > MaximumWarningReferences - references)
                        throw new DomainRuleViolationException("Объём ссылок в предупреждениях о перекрытии превышает технический предел 60 000; сократите перекрывающиеся документы.");
                    references += occurrences;
                    for (var index = previous.Value; index < change.Key; index++)
                        result.Add((center.Key, new DateOnly(index / 12 + 1, index % 12 + 1, 1)));
                }
                coverage += change.Value; previous = change.Key;
            }
        }
        return result;
    }
}
