using Bms.Application.Abstractions;
using Bms.Domain.Common;

namespace Bms.Application.ExpenseRequests;

public sealed class CurrencyLineConversion
{
    private CurrencyLineConversion(
        decimal amountKzt,
        decimal? vatAmountKzt,
        decimal? amountExcludingVatKzt)
    {
        AmountKzt = amountKzt;
        VatAmountKzt = vatAmountKzt;
        AmountExcludingVatKzt = amountExcludingVatKzt;
    }

    public decimal AmountKzt { get; }

    public decimal? VatAmountKzt { get; }

    public decimal? AmountExcludingVatKzt { get; }

    internal static CurrencyLineConversion Create(
        decimal amountKzt,
        decimal? vatAmountKzt,
        decimal? amountExcludingVatKzt) => new(
        amountKzt,
        vatAmountKzt,
        amountExcludingVatKzt);
}

public static class CurrencyLineConversionCalculator
{
    public static CurrencyLineConversion Calculate(
        decimal amount,
        decimal? vatAmount,
        decimal? amountExcludingVat,
        CurrencyRateQuote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        MoneyAmountPolicy.EnsurePositive(amount, "Line amount");

        if (vatAmount.HasValue != amountExcludingVat.HasValue)
        {
            throw new ArgumentException(
                "VAT and amount excluding VAT must either both be present or both be absent.");
        }

        if (vatAmount is { } vat && amountExcludingVat is { } excludingVat)
        {
            MoneyAmountPolicy.EnsureNonNegative(vat, "Line VAT amount");
            MoneyAmountPolicy.EnsureNonNegative(excludingVat, "Line amount excluding VAT");
            if (vat + excludingVat != amount)
            {
                throw new ArgumentException(
                    "VAT and amount excluding VAT must add up to the line amount.");
            }
        }

        var amountKzt = ConvertToKzt(amount, quote);
        if (amountExcludingVat is not { } sourceExcludingVat)
        {
            return CurrencyLineConversion.Create(amountKzt, null, null);
        }

        var amountExcludingVatKzt = ConvertToKzt(sourceExcludingVat, quote);
        var vatAmountKzt = amountKzt - amountExcludingVatKzt;
        if (!MoneyAmountPolicy.IsRepresentable(vatAmountKzt) || vatAmountKzt < 0m)
        {
            throw new InvalidOperationException(
                "Converted VAT amount cannot be represented by the KZT money contract.");
        }

        return CurrencyLineConversion.Create(
            amountKzt,
            vatAmountKzt,
            amountExcludingVatKzt);
    }

    private static decimal ConvertToKzt(decimal amount, CurrencyRateQuote quote)
    {
        decimal result;
        try
        {
            result = decimal.Round(
                amount * quote.Rate / quote.Multiplicity,
                MoneyAmountPolicy.Scale,
                MidpointRounding.AwayFromZero);
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException(
                "Converted KZT amount exceeds the supported decimal range.",
                exception);
        }

        if (!MoneyAmountPolicy.IsRepresentable(result) || result < 0m)
        {
            throw new InvalidOperationException(
                "Converted KZT amount cannot be represented by the KZT money contract.");
        }

        return result;
    }
}
