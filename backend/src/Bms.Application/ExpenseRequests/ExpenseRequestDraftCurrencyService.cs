using Bms.Application.Abstractions;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

public sealed class ExpenseRequestDraftCurrencyService(
    ICurrencyRateResolver currencyRateResolver,
    ICurrencyRateUsagePolicy currencyRateUsagePolicy)
{
    public async Task RefreshAsync(
        ExpenseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ExpenseRequestBudgetProjectionCalculator
                .IsStructurallyCompleteForCurrencyCalculation(request))
        {
            if (!string.Equals(request.CurrencyCode, "KZT", StringComparison.Ordinal))
            {
                ClearIfPresent(request);
            }

            return;
        }

        if (string.Equals(request.CurrencyCode, "KZT", StringComparison.Ordinal))
        {
            return;
        }

        CurrencyRateUsageContext? usageContext;
        try
        {
            usageContext = currencyRateUsagePolicy.GetCurrentContext();
        }
        catch (Exception exception)
        {
            throw new DraftCurrencyCalculationUnavailableException(
                "Foreign-currency calculation policy is unavailable.",
                exception);
        }

        if (usageContext is null)
        {
            throw new DraftCurrencyCalculationUnavailableException(
                "Foreign-currency calculation policy is not configured.");
        }

        CurrencyRateResolution resolution;
        try
        {
            resolution = await currencyRateResolver.ResolveAsync(
                request.CurrencyCode,
                usageContext.BusinessDate,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new DraftCurrencyCalculationUnavailableException(
                "Currency-rate source is unavailable.",
                exception);
        }

        if (resolution is null)
        {
            throw new DraftCurrencyCalculationUnavailableException(
                "Currency-rate source returned no result.");
        }

        if (!resolution.IsResolved)
        {
            ClearIfPresent(request);
            return;
        }

        var quote = resolution.Quote!;
        if (!string.Equals(quote.CurrencyCode, request.CurrencyCode, StringComparison.Ordinal)
            || quote.RateDate is not { } rateDate
            // The resolver selects the latest loaded quote as of the business date.
            // A date rollover must not invalidate that quote before a new one is loaded.
            || rateDate > usageContext.BusinessDate)
        {
            ClearIfPresent(request);
            return;
        }

        var lines = request.Lines;
        var calculations = new List<ExpenseRequestLineKztCalculation>(lines.Count);
        var amountsUnchanged = true;
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CurrencyLineConversion conversion;
            try
            {
                conversion = CurrencyLineConversionCalculator.Calculate(
                    line.Amount,
                    line.VatAmount,
                    line.AmountExcludingVat,
                    quote);
            }
            catch (InvalidOperationException)
            {
                throw new DomainRuleViolationException(
                    "Converted KZT amount exceeds the supported money range.");
            }

            calculations.Add(new ExpenseRequestLineKztCalculation(
                line.Id,
                conversion.AmountKzt,
                conversion.VatAmountKzt,
                conversion.AmountExcludingVatKzt));
            amountsUnchanged = amountsUnchanged && line.AmountKzt == conversion.AmountKzt
                && line.VatAmountKzt == conversion.VatAmountKzt
                && line.AmountExcludingVatKzt == conversion.AmountExcludingVatKzt;
        }

        if (request.CurrencyRateDate == quote.RateDate && request.CurrencyRate == quote.Rate
            && request.CurrencyMultiplicity == quote.Multiplicity
            && amountsUnchanged) return;

        request.ApplyPreliminaryCurrencyCalculation(
            quote.RateDate,
            quote.Rate,
            quote.Multiplicity,
            calculations);
    }

    private static void ClearIfPresent(ExpenseRequest request)
    {
        if (request.CurrencyRateDate is not null || request.CurrencyRate is not null
            || request.CurrencyMultiplicity is not null || request.Lines.Any(line => line.AmountKzt is not null
                || line.VatAmountKzt is not null || line.AmountExcludingVatKzt is not null))
            request.ClearPreliminaryCurrencyCalculation();
    }
}

public sealed class DraftCurrencyCalculationUnavailableException : Exception
{
    public DraftCurrencyCalculationUnavailableException(string message)
        : base(message)
    {
    }

    public DraftCurrencyCalculationUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
