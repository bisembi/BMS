using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bms.Application.Budgeting;

/// The strict primitive readers. Exact and allowed property sets, canonical
/// strings with their case sensitivity, version-5 UUIDs, money rounding and
/// the conversion to whole cents all live here, together with the error texts
/// the tests rely on.
internal static partial class ReferenceBudgetCandidateValidator
{

    private static Dictionary<string, JsonElement> ReadExactObject(
        JsonElement element,
        string path,
        IReadOnlyList<string> expectedProperties) =>
        ReadAllowedObject(element, path, expectedProperties, []);

    private static Dictionary<string, JsonElement> ReadAllowedObject(
        JsonElement element,
        string path,
        IReadOnlyList<string> requiredProperties,
        IReadOnlyList<string> optionalProperties)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            throw Invalid($"{path} must be an object");
        }

        var required = requiredProperties.ToHashSet(StringComparer.Ordinal);
        var allowed = requiredProperties.Concat(optionalProperties).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) || !result.TryAdd(property.Name, property.Value))
            {
                throw Invalid($"{path} contains an unknown or duplicate property '{property.Name}'");
            }
        }

        if (required.Any(property => !result.ContainsKey(property)))
        {
            throw Invalid($"{path} is missing a required property");
        }

        return result;
    }

    private static IReadOnlyList<JsonElement> ReadArray(JsonElement element, string path)
    {
        if (element.ValueKind is not JsonValueKind.Array)
        {
            throw Invalid($"{path} must be an array");
        }

        return element.EnumerateArray().ToArray();
    }

    private static string ReadCanonicalString(
        JsonElement element,
        string path,
        int maximumLength,
        bool allowEmpty = false)
    {
        if (element.ValueKind is not JsonValueKind.String)
        {
            throw Invalid($"{path} must be a string");
        }

        var value = element.GetString()!;
        ValidateCanonicalString(value, path, maximumLength, allowEmpty);
        return value;
    }

    private static void ValidateCanonicalString(
        string value,
        string path,
        int maximumLength,
        bool allowEmpty)
    {
        if ((!allowEmpty && value.Length == 0) ||
            value.Length > maximumLength ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            value.Any(char.IsControl))
        {
            throw Invalid($"{path} is not canonical or exceeds its reviewed length");
        }
    }

    private static int ReadInt32(JsonElement element, string path)
    {
        if (element.ValueKind is not JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw Invalid($"{path} must be an integer");
        }

        return value;
    }

    private static Guid ReadVersion5Guid(JsonElement element, string path)
    {
        var value = ReadCanonicalString(element, path, 36);
        if (!Guid.TryParseExact(value, "D", out var guid) ||
            guid == Guid.Empty ||
            !string.Equals(value, guid.ToString("D"), StringComparison.Ordinal) ||
            value[14] != '5' ||
            !"89ab".Contains(value[19]))
        {
            throw Invalid($"{path} must be a canonical lowercase UUID v5");
        }

        return guid;
    }

    private static DateOnly ReadDate(JsonElement element, string path)
    {
        var value = ReadCanonicalString(element, path, 10);
        if (!DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date) ||
            !string.Equals(value, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw Invalid($"{path} must be a canonical date");
        }

        return date;
    }

    private static decimal ReadMoneySourceValue(JsonElement element, string path)
    {
        if (element.ValueKind is not JsonValueKind.Number || !element.TryGetDecimal(out var value))
        {
            throw Invalid($"{path} must be a finite decimal number");
        }

        if (value < -MaximumMoney || value > MaximumMoney)
        {
            throw Invalid($"{path} exceeds decimal(18,2) capacity");
        }

        return value;
    }

    private static decimal ReadLimitAmount(JsonElement element, string path)
    {
        var value = ReadMoneySourceValue(element, path);
        if (value <= 0 || decimal.Round(value, 2) != value)
        {
            throw Invalid($"{path} must be positive with scale no greater than two");
        }

        return value;
    }

    private static bool IsEligible(CashFlowItem item, string group) =>
        string.Equals(item.Group, group, StringComparison.Ordinal) &&
        string.Equals(item.Flow, "Выбытие", StringComparison.Ordinal) &&
        item.Owner.Length > 0 &&
        !string.Equals(item.Owner, ExcludedOwner, StringComparison.Ordinal);

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static long ToCents(decimal value) => checked(decimal.ToInt64(value * 100m));

    private static void RequireUnique<T>(HashSet<T> values, T value, string path)
        where T : notnull
    {
        if (!values.Add(value))
        {
            throw Invalid($"{path} is duplicated");
        }
    }

    private static ReferenceBudgetCandidateValidationException Invalid(string message) => new(message);

    private static ReferenceBudgetCandidateValidationException Invalid(
        string message,
        Exception innerException) => new(message, innerException);

    [GeneratedRegex(@"^\d{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex SixDigitCodeRegex();
}
