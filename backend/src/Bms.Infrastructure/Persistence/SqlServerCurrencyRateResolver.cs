using Bms.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class SqlServerCurrencyRateResolver : ICurrencyRateResolver
{
    private readonly Func<string, CancellationToken, Task<CurrencySourceSnapshot?>>
        _readCurrencyAsync;
    private readonly Func<Guid, DateOnly, CancellationToken, Task<CurrencyRateSourceSnapshot?>>
        _readLatestRateAsync;

    public SqlServerCurrencyRateResolver(BmsDbContext dbContext)
        : this(
            (currencyCode, cancellationToken) => dbContext.Currencies
                .AsNoTracking()
                .Where(currency => currency.Code == currencyCode)
                .Select(currency => new CurrencySourceSnapshot(
                    currency.Id,
                    currency.Code,
                    currency.IsActive))
                .SingleOrDefaultAsync(cancellationToken),
            (currencyId, asOf, cancellationToken) => dbContext.CurrencyRates
                .AsNoTracking()
                .Where(rate => rate.CurrencyId == currencyId && rate.RateDate <= asOf)
                .OrderByDescending(rate => rate.RateDate)
                .Select(rate => new CurrencyRateSourceSnapshot(
                    rate.CurrencyId,
                    rate.RateDate,
                    rate.Rate,
                    rate.Multiplicity))
                .FirstOrDefaultAsync(cancellationToken))
    {
        ArgumentNullException.ThrowIfNull(dbContext);
    }

    internal SqlServerCurrencyRateResolver(
        Func<string, CancellationToken, Task<CurrencySourceSnapshot?>> readCurrencyAsync,
        Func<Guid, DateOnly, CancellationToken, Task<CurrencyRateSourceSnapshot?>>
            readLatestRateAsync)
    {
        _readCurrencyAsync = readCurrencyAsync ??
            throw new ArgumentNullException(nameof(readCurrencyAsync));
        _readLatestRateAsync = readLatestRateAsync ??
            throw new ArgumentNullException(nameof(readLatestRateAsync));
    }

    public async Task<CurrencyRateResolution> ResolveAsync(
        string currencyCode,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        EnsureCanonicalCurrencyCode(currencyCode);

        var currency = await _readCurrencyAsync(currencyCode, cancellationToken);
        if (currency is null)
        {
            return CurrencyRateResolution.Unavailable(
                CurrencyRateResolutionIssueCodes.CurrencyMissing);
        }

        if (currency.Id == Guid.Empty ||
            !string.Equals(currency.Code, currencyCode, StringComparison.Ordinal))
        {
            return CurrencyRateResolution.Unavailable(
                CurrencyRateResolutionIssueCodes.CurrencyInvalid);
        }

        if (!currency.IsActive)
        {
            return CurrencyRateResolution.Unavailable(
                CurrencyRateResolutionIssueCodes.CurrencyInactive);
        }

        if (string.Equals(currencyCode, "KZT", StringComparison.Ordinal))
        {
            return CurrencyRateResolution.Resolved(
                CurrencyRateQuote.Create("KZT", null, 1m, 1m));
        }

        var rate = await _readLatestRateAsync(currency.Id, asOf, cancellationToken);
        if (rate is null)
        {
            return CurrencyRateResolution.Unavailable(
                CurrencyRateResolutionIssueCodes.RateMissing);
        }

        if (rate.CurrencyId != currency.Id || rate.RateDate > asOf)
        {
            return CurrencyRateResolution.Unavailable(
                CurrencyRateResolutionIssueCodes.RateInvalid);
        }

        try
        {
            return CurrencyRateResolution.Resolved(CurrencyRateQuote.Create(
                currencyCode,
                rate.RateDate,
                rate.Rate,
                rate.Multiplicity));
        }
        catch (ArgumentException)
        {
            return CurrencyRateResolution.Unavailable(
                CurrencyRateResolutionIssueCodes.RateInvalid);
        }
    }

    private static void EnsureCanonicalCurrencyCode(string currencyCode)
    {
        if (currencyCode is not { Length: 3 } ||
            currencyCode.Any(character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                "Currency code must contain exactly three uppercase ASCII letters.",
                nameof(currencyCode));
        }
    }
}

internal sealed record CurrencySourceSnapshot(Guid Id, string Code, bool IsActive);

internal sealed record CurrencyRateSourceSnapshot(
    Guid CurrencyId,
    DateOnly RateDate,
    decimal Rate,
    decimal Multiplicity);
