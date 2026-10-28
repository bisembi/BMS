using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bms.Application.Budgeting;

/// The budget lines the primary data implies. Nothing here may invent a
/// balancing line: the expected set is derived from the source and compared,
/// never adjusted to make a total match.
internal static partial class ReferenceBudgetCandidateValidator
{

    private static IReadOnlyDictionary<BudgetLineKey, long> BuildExpectedLines(
        IReadOnlyList<DateOnly> months,
        IReadOnlyList<CashFlowItem> cashFlowItems,
        IReadOnlyDictionary<string, decimal[]> groupMonthlyTotals,
        IReadOnlyList<string> expenseGroups)
    {
        var expected = new Dictionary<BudgetLineKey, long>();
        foreach (var group in expenseGroups)
        {
            var eligibleItems = cashFlowItems
                .Where(item => IsEligible(item, group))
                .OrderBy(item => item.SourceIndex)
                .ToArray();
            for (var monthIndex = 0; monthIndex < months.Count; monthIndex++)
            {
                var targetCents = ToCents(RoundMoney(decimal.Abs(
                    groupMonthlyTotals[group][monthIndex])));
                if (targetCents == 0)
                {
                    continue;
                }

                var baseCents = targetCents / eligibleItems.Length;
                for (var itemIndex = 0; itemIndex < eligibleItems.Length; itemIndex++)
                {
                    var item = eligibleItems[itemIndex];
                    var cents = itemIndex == eligibleItems.Length - 1
                        ? targetCents - (baseCents * (eligibleItems.Length - 1))
                        : baseCents;
                    var key = new BudgetLineKey(item.Id, months[monthIndex]);
                    if (!expected.TryAdd(key, cents))
                    {
                        throw Invalid("the expected budget key set is not unique");
                    }
                }
            }
        }

        return expected;
    }
}
