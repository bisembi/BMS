using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bms.Application.Budgeting;

/// Monthly totals per expense group, read from the primary data only.
internal static partial class ReferenceBudgetCandidateValidator
{

    private static IReadOnlyDictionary<string, decimal[]> ReadGroupMonthlyTotals(
        JsonElement element,
        IReadOnlyList<CostItem> costItems,
        int expectedMonthCount)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            throw Invalid("$.groupMonthlyTotals must be an object");
        }

        var costItemNames = costItems.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, decimal[]>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var path = $"$.groupMonthlyTotals.{property.Name}";
            ValidateCanonicalString(property.Name, path, 500, allowEmpty: false);
            if (!costItemNames.Contains(property.Name) || !result.TryAdd(property.Name, []))
            {
                throw Invalid($"{path} is unknown or duplicated");
            }

            var values = ReadArray(property.Value, path);
            if (values.Count != expectedMonthCount)
            {
                throw Invalid($"{path} must contain exactly twelve monthly values");
            }

            var totals = new decimal[expectedMonthCount];
            for (var index = 0; index < totals.Length; index++)
            {
                totals[index] = ReadMoneySourceValue(values[index], $"{path}[{index}]");
            }

            result[property.Name] = totals;
        }

        if (result.Count == 0)
        {
            throw Invalid("$.groupMonthlyTotals must not be empty");
        }

        return result;
    }

    private static IReadOnlyList<string> ReadExpenseGroups(
        JsonElement element,
        IReadOnlyDictionary<string, decimal[]> groupMonthlyTotals,
        IReadOnlyList<CashFlowItem> cashFlowItems)
    {
        var array = ReadArray(element, "$.expenseGroups");
        if (array.Count == 0)
        {
            throw Invalid("$.expenseGroups must not be empty");
        }

        var result = new List<string>(array.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? previous = null;
        for (var index = 0; index < array.Count; index++)
        {
            var group = ReadCanonicalString(array[index], $"$.expenseGroups[{index}]", 500);
            if (!groupMonthlyTotals.ContainsKey(group) || !seen.Add(group))
            {
                throw Invalid($"$.expenseGroups[{index}] is unknown or duplicated");
            }

            if (previous is not null && StringComparer.Ordinal.Compare(previous, group) >= 0)
            {
                throw Invalid("$.expenseGroups must remain in strict ordinal order");
            }

            if (!cashFlowItems.Any(item => IsEligible(item, group)))
            {
                throw Invalid($"$.expenseGroups[{index}] has no eligible cash-flow item");
            }

            if (groupMonthlyTotals[group].Any(value => value > 0))
            {
                throw Invalid($"$.expenseGroups[{index}] contains a positive source amount");
            }

            result.Add(group);
            previous = group;
        }

        return result;
    }
}
