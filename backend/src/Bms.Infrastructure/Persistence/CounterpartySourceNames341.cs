using System.Text.Json;

namespace Bms.Infrastructure.Persistence;

internal static class CounterpartySourceNames341
{
    internal static IReadOnlyList<string> Read(string json)
    {
        if (json.Length > 4096) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().Count(p => p.Name == "sources") != 1
                || !root.TryGetProperty("sources", out var sources) || sources.ValueKind != JsonValueKind.Array
                || sources.GetArrayLength() is < 1 or > 3) return [];
            var codes = sources.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : null).ToArray();
            var known = new[] { "ExampleAccountingA", "ExampleAccountingB", "ExampleAccountingC" };
            if (codes.Any(code => code is null || !known.Contains(code, StringComparer.Ordinal))
                || codes.Distinct(StringComparer.Ordinal).Count() != codes.Length) return [];
            return known.Where(code => codes.Contains(code, StringComparer.Ordinal)).Select(code => code switch
            { "ExampleAccountingA" => "Организация A", "ExampleAccountingB" => "Организация B", _ => "Организация C" }).ToArray();
        }
        catch (JsonException) { return []; }
    }
}
