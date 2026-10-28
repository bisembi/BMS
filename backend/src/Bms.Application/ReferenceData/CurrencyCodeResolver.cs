using System.Collections.Frozen;
using Bms.Domain.Common;

namespace Bms.Application.ReferenceData;

/// <summary>
/// Resolves the ISO 4217 alphabetic code that the expense-request domain uses
/// from the two fields shown to a user in the currency directory.  Older
/// imported rows may contain an internal numeric code in <c>Code</c> and the
/// alphabetic code in <c>Name</c>; that legacy shape is accepted while the
/// row is being repaired.
/// </summary>
public static class CurrencyCodeResolver
{
    private static readonly FrozenDictionary<string, string> NumericToAlphabetic =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["032"] = "ARS",
            ["036"] = "AUD",
            ["051"] = "AMD",
            ["052"] = "BBD",
            ["124"] = "CAD",
            ["144"] = "LKR",
            ["152"] = "CLP",
            ["156"] = "CNY",
            ["191"] = "HRK",
            ["203"] = "CZK",
            ["208"] = "DKK",
            ["214"] = "DOP",
            ["230"] = "ETB",
            ["238"] = "FKP",
            ["242"] = "FJD",
            ["300"] = "GRD",
            ["344"] = "HKD",
            ["348"] = "HUF",
            ["352"] = "ISK",
            ["356"] = "INR",
            ["360"] = "IDR",
            ["364"] = "IRR",
            ["376"] = "ILS",
            ["388"] = "JMD",
            ["392"] = "JPY",
            ["398"] = "KZT",
            ["400"] = "JOD",
            ["404"] = "KES",
            ["410"] = "KRW",
            ["414"] = "KWD",
            ["417"] = "KGS",
            ["418"] = "LAK",
            ["422"] = "LBP",
            ["446"] = "MOP",
            ["458"] = "MYR",
            ["462"] = "MVR",
            ["484"] = "MXN",
            ["496"] = "MNT",
            ["504"] = "MAD",
            ["512"] = "OMR",
            ["516"] = "NAD",
            ["524"] = "NPR",
            ["554"] = "NZD",
            ["566"] = "NGN",
            ["578"] = "NOK",
            ["586"] = "PKR",
            ["604"] = "PEN",
            ["608"] = "PHP",
            ["616"] = "PLN",
            ["634"] = "QAR",
            ["643"] = "RUB",
            ["646"] = "RWF",
            ["682"] = "SAR",
            ["690"] = "SCR",
            ["702"] = "SGD",
            ["704"] = "VND",
            ["710"] = "ZAR",
            ["748"] = "SZL",
            ["752"] = "SEK",
            ["756"] = "CHF",
            ["760"] = "SYP",
            ["764"] = "THB",
            ["776"] = "TOP",
            ["784"] = "AED",
            ["788"] = "TND",
            ["800"] = "UGX",
            ["818"] = "EGP",
            ["826"] = "GBP",
            ["834"] = "TZS",
            ["840"] = "USD",
            ["858"] = "UYU",
            ["860"] = "UZS",
            ["886"] = "YER",
            ["901"] = "TWD",
            ["923"] = "SLE",
            ["933"] = "BYN",
            ["944"] = "AZN",
            ["946"] = "RON",
            ["949"] = "TRY",
            ["950"] = "XAF",
            ["951"] = "XCD",
            ["952"] = "XOF",
            ["953"] = "XPF",
            ["960"] = "XDR",
            ["968"] = "SRD",
            ["969"] = "MGA",
            ["971"] = "AFN",
            ["972"] = "TJS",
            ["973"] = "AOA",
            ["975"] = "BGN",
            ["976"] = "CDF",
            ["977"] = "BAM",
            ["978"] = "EUR",
            ["980"] = "UAH",
            ["981"] = "GEL",
            ["986"] = "BRL",
            ["999"] = "XXX",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static bool IsAlphabeticCode(string? value) =>
        value is { Length: 3 } && value.All(character => character is >= 'A' and <= 'Z');

    public static bool IsNumericCode(string? value) =>
        value is { Length: 3 } && value.All(character => character is >= '0' and <= '9');

    public static bool TryResolve(
        string? requestedCode,
        string? name,
        string? numericCode,
        out string code)
    {
        var requested = requestedCode?.Trim().ToUpperInvariant();
        if (IsAlphabeticCode(requested))
        {
            code = requested!;
            return true;
        }

        var named = name?.Trim().ToUpperInvariant();
        if (IsAlphabeticCode(named))
        {
            code = named!;
            return true;
        }

        if (numericCode is not null
            && NumericToAlphabetic.TryGetValue(numericCode.Trim(), out var mapped))
        {
            code = mapped;
            return true;
        }

        code = string.Empty;
        return false;
    }

    public static string ResolveOrThrow(string? requestedCode, string name, string numericCode)
    {
        if (TryResolve(requestedCode, name, numericCode, out var code)) return code;
        throw new DomainRuleViolationException(
            "Для валюты укажите ISO-код из трёх латинских букв в наименовании или поддерживаемый цифровой код.");
    }

    public static string? TryResolveImported(string? code, string? name, string? numericCode) =>
        TryResolve(code, name, numericCode, out var resolved) ? resolved : null;
}
