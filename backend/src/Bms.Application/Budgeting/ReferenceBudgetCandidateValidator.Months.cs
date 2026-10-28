using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bms.Application.Budgeting;

/// The twelve budget months of the declared year, in order and without gaps.
internal static partial class ReferenceBudgetCandidateValidator
{

    private static IReadOnlyList<DateOnly> ReadMonths(JsonElement element, int year)
    {
        var array = ReadArray(element, "$.months");
        if (array.Count != 12)
        {
            throw Invalid("$.months must contain exactly twelve periods");
        }

        var result = new DateOnly[12];
        for (var index = 0; index < result.Length; index++)
        {
            var path = $"$.months[{index}]";
            var value = ReadCanonicalString(array[index], path, 10);
            if (!DateOnly.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var month) ||
                month != new DateOnly(year, index + 1, 1))
            {
                throw Invalid($"{path} must be the canonical first day of its yearly sequence");
            }

            result[index] = month;
        }

        return result;
    }
}
