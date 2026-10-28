using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Bms.Infrastructure.Time;

public sealed record NationalBankRate(string Code, decimal Rate, decimal Multiplicity);

public static class NationalBankCurrencyFeed
{
    public const int MaximumBytes = 262144;
    public static Uri ForDate(DateOnly date) => new(
        "https://nationalbank.kz/rss/get_rates.cfm?fdate=" + date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));

    public static IReadOnlyList<NationalBankRate> Parse(string xml, DateOnly requestedDate)
    {
        if (xml.Length is 0 or > MaximumBytes) throw new FormatException("Invalid NBK payload size.");
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumBytes });
        var document = XDocument.Load(reader);
        var root = document.Root;
        if (root?.Name != "rates" || root.Elements("date").Count() != 1
            || !DateOnly.TryParseExact(root.Element("date")?.Value.Trim(), "dd.MM.yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date != requestedDate)
            throw new FormatException("NBK effective date does not match the requested date.");
        var items = root.Elements("item").ToArray();
        if (items.Length is 0 or > 200) throw new FormatException("Invalid NBK item count.");
        var rates = items.Select(item =>
        {
            if (new[] { "title", "description", "quant" }.Any(name => item.Elements(name).Count() != 1))
                throw new FormatException("Ambiguous NBK rate.");
            var code = item.Element("title")!.Value.Trim();
            if (code.Length != 3 || code.Any(c => c is < 'A' or > 'Z') || code == "KZT"
                || !decimal.TryParse(item.Element("description")!.Value.Trim(), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var rate) || rate <= 0m || rate >= 1000000000m
                || decimal.Round(rate, 6) != rate
                || !decimal.TryParse(item.Element("quant")!.Value.Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var quantity) || quantity is <= 0m or >= 10000000000m)
                throw new FormatException("Invalid NBK rate or multiplicity.");
            return new NationalBankRate(code, rate, quantity);
        }).ToArray();
        if (rates.Select(r => r.Code).Distinct(StringComparer.Ordinal).Count() != rates.Length)
            throw new FormatException("Duplicate NBK currency.");
        return rates;
    }
}
