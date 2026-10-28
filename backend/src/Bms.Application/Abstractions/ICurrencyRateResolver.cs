namespace Bms.Application.Abstractions;

public static class CurrencyRateResolutionIssueCodes
{
    public const string CurrencyMissing = "Currency.Missing";

    public const string CurrencyInactive = "Currency.Inactive";

    public const string CurrencyInvalid = "Currency.Invalid";

    public const string RateMissing = "CurrencyRate.Missing";

    public const string RateInvalid = "CurrencyRate.Invalid";

    internal static bool IsSupported(string code) => code is
        CurrencyMissing or
        CurrencyInactive or
        CurrencyInvalid or
        RateMissing or
        RateInvalid;
}

public sealed class CurrencyRateQuote
{
    private const decimal MaximumRate = 999_999_999.999999m;
    private const decimal MaximumMultiplicity = 9_999_999_999m;

    private CurrencyRateQuote(
        string currencyCode,
        DateOnly? rateDate,
        decimal rate,
        decimal multiplicity)
    {
        CurrencyCode = currencyCode;
        RateDate = rateDate;
        Rate = rate;
        Multiplicity = multiplicity;
    }

    public string CurrencyCode { get; }

    public DateOnly? RateDate { get; }

    public decimal Rate { get; }

    public decimal Multiplicity { get; }

    public static CurrencyRateQuote Create(
        string currencyCode,
        DateOnly? rateDate,
        decimal rate,
        decimal multiplicity)
    {
        EnsureCanonicalCurrencyCode(currencyCode, nameof(currencyCode));

        if (string.Equals(currencyCode, "KZT", StringComparison.Ordinal))
        {
            if (rateDate is not null || rate != 1m || multiplicity != 1m)
            {
                throw new ArgumentException(
                    "KZT quote must have no source rate date and must use rate/multiplicity 1/1.");
            }
        }
        else if (rateDate is null)
        {
            throw new ArgumentException(
                "A foreign-currency quote requires its source rate date.",
                nameof(rateDate));
        }

        if (rate <= 0m ||
            rate > MaximumRate ||
            decimal.Round(rate, 6, MidpointRounding.ToEven) != rate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rate),
                "Rate must be positive and representable as decimal(15,6).");
        }

        if (multiplicity <= 0m ||
            multiplicity > MaximumMultiplicity ||
            decimal.Truncate(multiplicity) != multiplicity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(multiplicity),
                "Multiplicity must be positive and representable as decimal(10,0).");
        }

        return new CurrencyRateQuote(currencyCode, rateDate, rate, multiplicity);
    }

    private static void EnsureCanonicalCurrencyCode(string currencyCode, string parameterName)
    {
        if (currencyCode is not { Length: 3 } ||
            currencyCode.Any(character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                "Currency code must contain exactly three uppercase ASCII letters.",
                parameterName);
        }
    }
}

public sealed class CurrencyRateResolution
{
    private CurrencyRateResolution(CurrencyRateQuote? quote, string? issueCode)
    {
        Quote = quote;
        IssueCode = issueCode;
    }

    public CurrencyRateQuote? Quote { get; }

    public string? IssueCode { get; }

    public bool IsResolved => Quote is not null;

    public static CurrencyRateResolution Resolved(CurrencyRateQuote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        return new CurrencyRateResolution(quote, null);
    }

    public static CurrencyRateResolution Unavailable(string issueCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issueCode);
        if (!CurrencyRateResolutionIssueCodes.IsSupported(issueCode))
        {
            throw new ArgumentException("Unsupported currency-rate issue code.", nameof(issueCode));
        }

        return new CurrencyRateResolution(null, issueCode);
    }
}

public interface ICurrencyRateResolver
{
    Task<CurrencyRateResolution> ResolveAsync(
        string currencyCode,
        DateOnly asOf,
        CancellationToken cancellationToken);
}
