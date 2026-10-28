using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bms.Application.Budgeting;

/// Cost centres and the units they are derived from.
internal static partial class ReferenceBudgetCandidateValidator
{

    private static IReadOnlyList<CostCenter> ReadCostCenters(JsonElement element)
    {
        var array = ReadArray(element, "$.cfo");
        if (array.Count == 0)
        {
            throw Invalid("$.cfo must not be empty");
        }

        var result = new List<CostCenter>(array.Count);
        var ids = new HashSet<Guid>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < array.Count; index++)
        {
            var path = $"$.cfo[{index}]";
            var properties = ReadAllowedObject(
                array[index],
                path,
                ["id", "code", "name"],
                ["fromItemOwner"]);
            var id = ReadVersion5Guid(properties["id"], $"{path}.id");
            var code = ReadCanonicalString(properties["code"], $"{path}.code", 100);
            var name = ReadCanonicalString(properties["name"], $"{path}.name", 500);
            var expectedCode = (index + 1).ToString("D2", CultureInfo.InvariantCulture);
            if (!string.Equals(code, expectedCode, StringComparison.Ordinal))
            {
                throw Invalid($"{path}.code must preserve deterministic two-digit ordering");
            }

            var fromItemOwner = false;
            if (properties.TryGetValue("fromItemOwner", out var marker))
            {
                if (marker.ValueKind is not JsonValueKind.True)
                {
                    throw Invalid($"{path}.fromItemOwner must be true when present");
                }

                fromItemOwner = true;
            }

            RequireUnique(ids, id, $"{path}.id");
            RequireUnique(codes, code, $"{path}.code");
            RequireUnique(names, name, $"{path}.name");
            result.Add(new CostCenter(id, code, name, fromItemOwner));
        }

        if (result.Count(item => item.FromItemOwner) != 1)
        {
            throw Invalid("$.cfo must identify exactly one owner derived from an item row");
        }

        return result;
    }

    private static IReadOnlyList<ReferenceUnit> ReadUnits(JsonElement element)
    {
        var array = ReadArray(element, "$.units");
        if (array.Count == 0)
        {
            throw Invalid("$.units must not be empty");
        }

        var result = new List<ReferenceUnit>(array.Count);
        var ids = new HashSet<Guid>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < array.Count; index++)
        {
            var path = $"$.units[{index}]";
            var properties = ReadExactObject(array[index], path, ["id", "code", "name"]);
            var id = ReadVersion5Guid(properties["id"], $"{path}.id");
            var code = ReadCanonicalString(properties["code"], $"{path}.code", 100);
            var name = ReadCanonicalString(properties["name"], $"{path}.name", 500);
            var expectedCode = (index + 1).ToString("D3", CultureInfo.InvariantCulture);
            if (!string.Equals(code, expectedCode, StringComparison.Ordinal))
            {
                throw Invalid($"{path}.code must preserve deterministic three-digit ordering");
            }

            RequireUnique(ids, id, $"{path}.id");
            RequireUnique(codes, code, $"{path}.code");
            RequireUnique(names, name, $"{path}.name");
            result.Add(new ReferenceUnit(id, code, name));
        }

        return result;
    }
}
