using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bms.Application.Budgeting;

/// Cost items and cash-flow items, including the eligibility of an item for a
/// budget group.
internal static partial class ReferenceBudgetCandidateValidator
{

    private static IReadOnlyList<CostItem> ReadCostItems(JsonElement element)
    {
        var array = ReadArray(element, "$.costItems");
        if (array.Count == 0)
        {
            throw Invalid("$.costItems must not be empty");
        }

        var result = new List<CostItem>(array.Count);
        var ids = new HashSet<Guid>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        string? previousName = null;
        for (var index = 0; index < array.Count; index++)
        {
            var path = $"$.costItems[{index}]";
            var properties = ReadExactObject(array[index], path, ["id", "code", "name"]);
            var id = ReadVersion5Guid(properties["id"], $"{path}.id");
            var code = ReadCanonicalString(properties["code"], $"{path}.code", 100);
            var name = ReadCanonicalString(properties["name"], $"{path}.name", 500);
            var expectedCode = $"G{index + 1:D2}";
            if (!string.Equals(code, expectedCode, StringComparison.Ordinal))
            {
                throw Invalid($"{path}.code must preserve deterministic GNN ordering");
            }

            if (previousName is not null && StringComparer.Ordinal.Compare(previousName, name) >= 0)
            {
                throw Invalid("$.costItems must remain in strict ordinal name order");
            }

            RequireUnique(ids, id, $"{path}.id");
            RequireUnique(codes, code, $"{path}.code");
            RequireUnique(names, name, $"{path}.name");
            result.Add(new CostItem(id, code, name));
            previousName = name;
        }

        return result;
    }

    private static IReadOnlyList<CashFlowItem> ReadCashFlowItems(
        JsonElement element,
        IReadOnlyList<CostItem> costItems)
    {
        var array = ReadArray(element, "$.cashflowItems");
        if (array.Count == 0)
        {
            throw Invalid("$.cashflowItems must not be empty");
        }

        var costItemsById = costItems.ToDictionary(item => item.Id);
        var expectedGroups = costItems.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var seenGroups = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<Guid>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var fullRows = new HashSet<string>(StringComparer.Ordinal);
        var baseCodeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<CashFlowItem>(array.Count);
        for (var index = 0; index < array.Count; index++)
        {
            var path = $"$.cashflowItems[{index}]";
            var properties = ReadExactObject(
                array[index],
                path,
                [
                    "id",
                    "code",
                    "name",
                    "full",
                    "direction",
                    "group",
                    "owner",
                    "flow",
                    "costItemId"
                ]);
            var id = ReadVersion5Guid(properties["id"], $"{path}.id");
            var code = ReadCanonicalString(properties["code"], $"{path}.code", 20);
            var name = ReadCanonicalString(properties["name"], $"{path}.name", 150);
            var full = ReadCanonicalString(properties["full"], $"{path}.full", 500);
            var direction = ReadCanonicalString(properties["direction"], $"{path}.direction", 500);
            var group = ReadCanonicalString(properties["group"], $"{path}.group", 500);
            var owner = ReadCanonicalString(
                properties["owner"],
                $"{path}.owner",
                500,
                allowEmpty: true);
            var flow = ReadCanonicalString(properties["flow"], $"{path}.flow", 20);
            var costItemId = ReadVersion5Guid(properties["costItemId"], $"{path}.costItemId");

            if (flow is not ("Выбытие" or "Поступление"))
            {
                throw Invalid($"{path}.flow is outside the reviewed flow vocabulary");
            }

            if (full.Length < 8 || full[6] != ' ' || !SixDigitCodeRegex().IsMatch(full[..6]))
            {
                throw Invalid($"{path}.full must preserve a six-digit source code and name");
            }

            var baseCode = full[..6];
            if (!string.Equals(full[7..], name, StringComparison.Ordinal))
            {
                throw Invalid($"{path}.full does not match its source code and name");
            }

            baseCodeCounts.TryGetValue(baseCode, out var occurrence);
            occurrence++;
            baseCodeCounts[baseCode] = occurrence;
            var expectedCode = occurrence == 1 ? baseCode : $"{baseCode}-{occurrence}";
            if (!string.Equals(code, expectedCode, StringComparison.Ordinal))
            {
                throw Invalid($"{path}.code does not preserve deterministic duplicate suffixing");
            }

            if (!costItemsById.TryGetValue(costItemId, out var costItem) ||
                !string.Equals(costItem.Name, group, StringComparison.Ordinal))
            {
                throw Invalid($"{path}.costItemId does not resolve to its exact group");
            }

            RequireUnique(ids, id, $"{path}.id");
            RequireUnique(codes, code, $"{path}.code");
            RequireUnique(names, name, $"{path}.name");
            RequireUnique(fullRows, full, $"{path}.full");
            seenGroups.Add(group);
            result.Add(new CashFlowItem(
                index,
                id,
                code,
                name,
                full,
                direction,
                group,
                owner,
                flow,
                costItemId));
        }

        if (!seenGroups.SetEquals(expectedGroups))
        {
            throw Invalid("$.costItems must match the exact cash-flow group set");
        }

        return result;
    }
}
