using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bms.Application.Budgeting;

/// The declared budgets, checked line by line against the expected set and
/// reconciled in whole cents.
internal static partial class ReferenceBudgetCandidateValidator
{

    private static BudgetValidationResult ReadAndValidateBudgets(
        JsonElement element,
        int year,
        IReadOnlyList<DateOnly> months,
        IReadOnlyList<CostCenter> costCenters,
        IReadOnlyList<CashFlowItem> cashFlowItems,
        IReadOnlyList<string> expenseGroups,
        IReadOnlyDictionary<BudgetLineKey, long> expectedLines)
    {
        var array = ReadArray(element, "$.budgetInstances");
        var costCentersById = costCenters.ToDictionary(item => item.Id);
        var costCentersByName = costCenters.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var cashFlowItemsById = cashFlowItems.ToDictionary(item => item.Id);
        var monthSet = months.ToHashSet();
        var expenseGroupSet = expenseGroups.ToHashSet(StringComparer.Ordinal);
        var expectedOwners = cashFlowItems
            .Where(item => expenseGroupSet.Contains(item.Group) && IsEligible(item, item.Group))
            .Select(item => item.Owner)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var actualOwners = new HashSet<string>(StringComparer.Ordinal);
        var instanceIds = new HashSet<Guid>();
        var instanceNumbers = new HashSet<string>(StringComparer.Ordinal);
        var actualLines = new Dictionary<BudgetLineKey, long>();
        var distinctAmounts = new HashSet<long>();
        long totalCents = 0;
        string? previousOwner = null;

        for (var instanceIndex = 0; instanceIndex < array.Count; instanceIndex++)
        {
            var path = $"$.budgetInstances[{instanceIndex}]";
            var properties = ReadExactObject(
                array[instanceIndex],
                path,
                ["id", "number", "cfoId", "cfoName", "validFrom", "validTo", "lines"]);
            var id = ReadVersion5Guid(properties["id"], $"{path}.id");
            var number = ReadCanonicalString(properties["number"], $"{path}.number", 100);
            var costCenterId = ReadVersion5Guid(properties["cfoId"], $"{path}.cfoId");
            var costCenterName = ReadCanonicalString(properties["cfoName"], $"{path}.cfoName", 500);
            var validFrom = ReadDate(properties["validFrom"], $"{path}.validFrom");
            var validTo = ReadDate(properties["validTo"], $"{path}.validTo");
            if (number != $"БЮ-{instanceIndex + 1:D6}")
            {
                throw Invalid($"{path}.number must preserve deterministic ordering");
            }

            if (previousOwner is not null &&
                StringComparer.Ordinal.Compare(previousOwner, costCenterName) >= 0)
            {
                throw Invalid("$.budgetInstances must remain in strict ordinal owner order");
            }

            if (!costCentersById.TryGetValue(costCenterId, out var byId) ||
                !costCentersByName.TryGetValue(costCenterName, out var byName) ||
                byId != byName)
            {
                throw Invalid($"{path} has an inconsistent cost-center reference");
            }

            if (validFrom != new DateOnly(year, 1, 1) ||
                validTo != new DateOnly(year, 12, 31))
            {
                throw Invalid($"{path} must cover the complete candidate year");
            }

            RequireUnique(instanceIds, id, $"{path}.id");
            RequireUnique(instanceNumbers, number, $"{path}.number");
            RequireUnique(actualOwners, costCenterName, $"{path}.cfoName");

            var lines = ReadArray(properties["lines"], $"{path}.lines");
            if (lines.Count == 0)
            {
                throw Invalid($"{path}.lines must not be empty");
            }

            DateOnly? previousPeriod = null;
            string? previousCashFlowItemId = null;
            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var linePath = $"{path}.lines[{lineIndex}]";
                var line = ReadExactObject(
                    lines[lineIndex],
                    linePath,
                    ["cashflowItemId", "budgetPeriod", "limitAmount"]);
                var cashFlowItemId = ReadVersion5Guid(
                    line["cashflowItemId"],
                    $"{linePath}.cashflowItemId");
                var budgetPeriod = ReadDate(line["budgetPeriod"], $"{linePath}.budgetPeriod");
                var limitAmount = ReadLimitAmount(line["limitAmount"], $"{linePath}.limitAmount");
                if (!cashFlowItemsById.TryGetValue(cashFlowItemId, out var item) ||
                    !string.Equals(item.Owner, costCenterName, StringComparison.Ordinal) ||
                    !expenseGroupSet.Contains(item.Group) ||
                    !string.Equals(item.Flow, "Выбытие", StringComparison.Ordinal))
                {
                    throw Invalid($"{linePath} does not resolve to its exact eligible owner and item");
                }

                if (!monthSet.Contains(budgetPeriod))
                {
                    throw Invalid($"{linePath}.budgetPeriod is outside the reviewed month set");
                }

                var idText = cashFlowItemId.ToString("D");
                if (previousPeriod is not null &&
                    (budgetPeriod < previousPeriod ||
                     (budgetPeriod == previousPeriod &&
                      StringComparer.Ordinal.Compare(previousCashFlowItemId, idText) >= 0)))
                {
                    throw Invalid($"{path}.lines must remain ordered by period and item id");
                }

                var key = new BudgetLineKey(cashFlowItemId, budgetPeriod);
                var cents = ToCents(limitAmount);
                if (!expectedLines.TryGetValue(key, out var expectedCents) || cents != expectedCents)
                {
                    throw Invalid($"{linePath}.limitAmount violates the reviewed equal-share model");
                }

                if (!actualLines.TryAdd(key, cents))
                {
                    throw Invalid($"{linePath} duplicates a global item-month key");
                }

                distinctAmounts.Add(cents);
                checked
                {
                    totalCents += cents;
                }

                previousPeriod = budgetPeriod;
                previousCashFlowItemId = idText;
            }

            previousOwner = costCenterName;
        }

        if (!actualOwners.SetEquals(expectedOwners))
        {
            throw Invalid("$.budgetInstances does not match the exact eligible owner set");
        }

        if (actualLines.Count != expectedLines.Count ||
            expectedLines.Keys.Any(key => !actualLines.ContainsKey(key)))
        {
            throw Invalid("$.budgetInstances does not contain the exact eligible item-month key set");
        }

        return new BudgetValidationResult(
            array.Count,
            actualLines.Count,
            distinctAmounts.Count,
            totalCents / 100m);
    }
}
