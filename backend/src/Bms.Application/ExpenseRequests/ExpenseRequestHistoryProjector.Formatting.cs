using System.Globalization;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// Display formatting. Every format here is part of what the user sees and
/// must not drift: dates as dd.MM.yyyy, months as MM.yyyy, money with its
/// currency, and the fixed dictionaries for status, basis, residency and VAT.
internal static partial class ExpenseRequestHistoryProjector
{

    private static string? FormatAmount(decimal? value, string? currency) => value.HasValue
        ? $"{value.Value.ToString("N2", RussianCulture)} {FormatCurrency(currency) ?? "Валюта не указана"}"
        : null;

    private static string? FormatCurrency(string? value)
    {
        var normalized = NormalizeText(value)?.ToUpperInvariant();
        if (normalized is null)
        {
            return null;
        }

        return normalized.Length == 3 && normalized.All(character => character is >= 'A' and <= 'Z')
            ? normalized
            : "Другая валюта";
    }

    private static string? FormatDate(DateOnly? value) => value?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private static string? FormatMonth(DateOnly? value) => value?.ToString("MM.yyyy", CultureInfo.InvariantCulture);

    private static string? FormatBoolean(bool? value) => value switch
    {
        true => "Да",
        false => "Нет",
        null => null
    };

    private static string? FormatStatus(string? value)
    {
        if (value is null)
        {
            return null;
        }

        // Audit rows written before the canonical wire contract used the enum
        // name. Keep that legacy fact readable, while all current values are
        // validated against the closed workflow vocabulary.
        if (value == "Draft")
        {
            return ExpenseRequestStatus.Draft.ToDisplayName();
        }

        return ExpenseRequestStatusCode.TryParse(value, out var status)
            ? status.ToDisplayName()
            : "Другое состояние";
    }

    private static string? FormatPaymentBasis(string? value) => value switch
    {
        null => null,
        "Contract" => "Договор",
        "InvoiceWithoutContract" => "Счёт без договора",
        "PublicOffer" => "Публичная оферта",
        "OtherBasis" => "Иное основание",
        _ => "Другое значение"
    };

    private static string? FormatResidency(string? value) => value switch
    {
        null => null,
        "ResidentKz" => "Резидент РК",
        "NonResident" => "Нерезидент",
        _ => "Другое значение"
    };

    private static string? FormatVatRate(string? value) => value switch
    {
        null => null,
        "Vat16" => "НДС 16%",
        "WithoutVat" => "Без НДС",
        _ => "Другая ставка"
    };

    private static string? NormalizeText(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : value.Trim();
}
