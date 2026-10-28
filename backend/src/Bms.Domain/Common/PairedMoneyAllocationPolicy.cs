using System.Numerics;

namespace Bms.Domain.Common;

/// <summary>
/// Allocates one decimal(18,2) amount proportionally to a paired
/// decimal(18,2) balance without intermediate decimal overflow. A fully
/// exhausted base balance always transfers the exact remaining pair.
/// </summary>
public static class PairedMoneyAllocationPolicy
{
    public static decimal Allocate(
        decimal availablePairedAmount,
        decimal allocatedBaseAmount,
        decimal availableBaseAmount)
    {
        MoneyAmountPolicy.EnsureNonNegative(availablePairedAmount, "Available paired amount");
        MoneyAmountPolicy.EnsureNonNegative(allocatedBaseAmount, "Allocated base amount");
        MoneyAmountPolicy.EnsurePositive(availableBaseAmount, "Available base amount");

        if (allocatedBaseAmount > availableBaseAmount)
        {
            throw new DomainRuleViolationException(
                "Allocated base amount cannot exceed the available base amount.");
        }
        if (allocatedBaseAmount == 0m || availablePairedAmount == 0m)
        {
            return 0m;
        }
        if (allocatedBaseAmount == availableBaseAmount)
        {
            return availablePairedAmount;
        }

        const decimal minorUnitFactor = 100m;
        var pairedMinorUnits = (BigInteger)(availablePairedAmount * minorUnitFactor);
        var allocatedBaseMinorUnits = (BigInteger)(allocatedBaseAmount * minorUnitFactor);
        var availableBaseMinorUnits = (BigInteger)(availableBaseAmount * minorUnitFactor);
        var numerator = pairedMinorUnits * allocatedBaseMinorUnits;
        var roundedMinorUnits = BigInteger.DivRem(
            numerator,
            availableBaseMinorUnits,
            out var remainder);

        if (remainder * 2 >= availableBaseMinorUnits)
        {
            roundedMinorUnits += BigInteger.One;
        }

        var result = (decimal)roundedMinorUnits / minorUnitFactor;
        MoneyAmountPolicy.EnsureNonNegative(result, "Allocated paired amount");
        return decimal.Min(result, availablePairedAmount);
    }
}
