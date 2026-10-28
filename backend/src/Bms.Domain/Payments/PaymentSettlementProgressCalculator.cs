using Bms.Domain.Common;

namespace Bms.Domain.Payments;

/// <summary>
/// Factual settlement progress. This is not a persisted workflow status and
/// does not approve any OPEN status, command or transition code.
/// </summary>
public enum PaymentSettlementProgress
{
    Unpaid = 0,
    PartiallySettled = 1,
    FullySettled = 2,
}

/// <summary>
/// Immutable line values captured for payment calculation. KZT values are the
/// original submitted equivalents; a current exchange rate is never accepted.
/// </summary>
public sealed record PaymentSettlementLineSnapshot(
    Guid LineId,
    int SortOrder,
    decimal AmountInRequestCurrency,
    decimal OriginalAmountKzt);

public sealed record PaymentSettlementLineAllocation(
    Guid LineId,
    int SortOrder,
    decimal ObligationAmountInRequestCurrency,
    decimal OriginalAmountKzt,
    decimal SettledAmountInRequestCurrency,
    decimal ReleasedReserveKzt,
    decimal RemainingReserveKzt);

public sealed record PaymentSettlementCalculation(
    string CurrencyCode,
    PaymentSettlementProgress Progress,
    decimal ObligationAmountInRequestCurrency,
    decimal SettledAmountInRequestCurrency,
    decimal ActualSettledAmountKzt,
    decimal OriginalReserveKzt,
    decimal ReleasedReserveKzt,
    decimal RemainingReserveKzt,
    decimal CurrencyDeviationKzt,
    IReadOnlyList<PaymentSettlementLineAllocation> Lines);

/// <summary>
/// Rebuilds current payment progress from authoritative aggregate settlement
/// facts. The caller must pass only the amount attributed to the obligation;
/// cash excess remains a separate overpayment and is not accepted here.
/// </summary>
public static class PaymentSettlementProgressCalculator
{
    public static PaymentSettlementCalculation Calculate(
        string currencyCode,
        IEnumerable<PaymentSettlementLineSnapshot> lineSnapshots,
        decimal settledAmountInRequestCurrency,
        decimal actualSettledAmountKzt)
    {
        ValidateCurrencyCode(currencyCode);
        ArgumentNullException.ThrowIfNull(lineSnapshots);
        MoneyAmountPolicy.EnsureNonNegative(
            settledAmountInRequestCurrency,
            "Settled amount in request currency");
        MoneyAmountPolicy.EnsureNonNegative(
            actualSettledAmountKzt,
            "Actual settled KZT amount");

        var lines = lineSnapshots.ToArray();
        if (lines.Length == 0)
        {
            throw new DomainRuleViolationException(
                "Payment settlement requires at least one expense request line.");
        }

        var isKzt = string.Equals(currencyCode, "KZT", StringComparison.Ordinal);
        ValidateLines(lines, isKzt);

        var orderedLines = lines.OrderBy(line => line.SortOrder).ToArray();
        var obligationAmount = SumRepresentable(
            orderedLines.Select(line => line.AmountInRequestCurrency),
            "Total obligation in request currency");
        var originalReserveKzt = SumRepresentable(
            orderedLines.Select(line => line.OriginalAmountKzt),
            "Total original KZT reserve");

        if (settledAmountInRequestCurrency > obligationAmount)
        {
            throw new DomainRuleViolationException(
                "Settlement attributed to the obligation cannot exceed the obligation amount.");
        }
        if ((settledAmountInRequestCurrency == 0m) != (actualSettledAmountKzt == 0m))
        {
            throw new DomainRuleViolationException(
                "Settlement in request currency and actual KZT settlement must both be zero or both be positive.");
        }
        if (isKzt && actualSettledAmountKzt != settledAmountInRequestCurrency)
        {
            throw new DomainRuleViolationException(
                "A KZT request requires equal settlement amounts in request currency and KZT.");
        }

        var remainingSettlement = settledAmountInRequestCurrency;
        var allocations = new PaymentSettlementLineAllocation[orderedLines.Length];
        for (var index = 0; index < orderedLines.Length; index++)
        {
            var line = orderedLines[index];
            var lineSettlement = decimal.Min(
                line.AmountInRequestCurrency,
                remainingSettlement);
            remainingSettlement -= lineSettlement;

            var releasedReserveKzt = CalculateReleasedReserve(
                isKzt,
                line,
                lineSettlement);
            var remainingReserveKzt = line.OriginalAmountKzt - releasedReserveKzt;
            MoneyAmountPolicy.EnsureNonNegative(
                remainingReserveKzt,
                "Line remaining KZT reserve");

            allocations[index] = new PaymentSettlementLineAllocation(
                line.LineId,
                line.SortOrder,
                line.AmountInRequestCurrency,
                line.OriginalAmountKzt,
                lineSettlement,
                releasedReserveKzt,
                remainingReserveKzt);
        }

        if (remainingSettlement != 0m)
        {
            throw new InvalidOperationException(
                "Validated settlement was not fully allocated by the FIFO calculation.");
        }

        var releasedReserveTotal = SumRepresentable(
            allocations.Select(line => line.ReleasedReserveKzt),
            "Total released KZT reserve",
            allowZero: true);
        var remainingReserveTotal = SumRepresentable(
            allocations.Select(line => line.RemainingReserveKzt),
            "Total remaining KZT reserve",
            allowZero: true);
        if (releasedReserveTotal + remainingReserveTotal != originalReserveKzt)
        {
            throw new InvalidOperationException(
                "Payment settlement reserve totals do not reconcile.");
        }

        var currencyDeviationKzt = isKzt
            ? 0m
            : actualSettledAmountKzt - releasedReserveTotal;
        MoneyAmountPolicy.EnsureRepresentable(
            currencyDeviationKzt,
            "Currency deviation KZT");

        var progress = settledAmountInRequestCurrency switch
        {
            0m => PaymentSettlementProgress.Unpaid,
            _ when settledAmountInRequestCurrency == obligationAmount =>
                PaymentSettlementProgress.FullySettled,
            _ => PaymentSettlementProgress.PartiallySettled,
        };

        return new PaymentSettlementCalculation(
            currencyCode,
            progress,
            obligationAmount,
            settledAmountInRequestCurrency,
            actualSettledAmountKzt,
            originalReserveKzt,
            releasedReserveTotal,
            remainingReserveTotal,
            currencyDeviationKzt,
            Array.AsReadOnly(allocations));
    }

    private static decimal CalculateReleasedReserve(
        bool isKzt,
        PaymentSettlementLineSnapshot line,
        decimal lineSettlement)
    {
        if (lineSettlement == 0m)
        {
            return 0m;
        }
        if (isKzt)
        {
            return lineSettlement;
        }
        if (lineSettlement == line.AmountInRequestCurrency)
        {
            return line.OriginalAmountKzt;
        }

        var released = PairedMoneyAllocationPolicy.Allocate(
            line.OriginalAmountKzt,
            lineSettlement,
            line.AmountInRequestCurrency);
        MoneyAmountPolicy.EnsureNonNegative(released, "Line released KZT reserve");
        return decimal.Min(released, line.OriginalAmountKzt);
    }

    private static void ValidateLines(
        IReadOnlyList<PaymentSettlementLineSnapshot> lines,
        bool isKzt)
    {
        var lineIds = new HashSet<Guid>();
        var sortOrders = new HashSet<int>();
        foreach (var line in lines)
        {
            if (line is null)
            {
                throw new DomainRuleViolationException(
                    "Payment settlement line snapshot cannot be null.");
            }
            if (line.LineId == Guid.Empty || !lineIds.Add(line.LineId))
            {
                throw new DomainRuleViolationException(
                    "Payment settlement line ids must be nonempty and unique.");
            }
            if (line.SortOrder <= 0 || !sortOrders.Add(line.SortOrder))
            {
                throw new DomainRuleViolationException(
                    "Payment settlement line sort orders must be positive and unique.");
            }

            MoneyAmountPolicy.EnsurePositive(
                line.AmountInRequestCurrency,
                "Line obligation in request currency");
            MoneyAmountPolicy.EnsurePositive(
                line.OriginalAmountKzt,
                "Line original KZT amount");
            if (isKzt && line.OriginalAmountKzt != line.AmountInRequestCurrency)
            {
                throw new DomainRuleViolationException(
                    "A KZT line requires equal request-currency and original KZT amounts.");
            }
        }
    }

    private static decimal SumRepresentable(
        IEnumerable<decimal> amounts,
        string fieldName,
        bool allowZero = false)
    {
        decimal total;
        try
        {
            total = amounts.Sum();
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException($"{fieldName} exceeds decimal storage range.");
        }

        if (allowZero)
        {
            MoneyAmountPolicy.EnsureNonNegative(total, fieldName);
        }
        else
        {
            MoneyAmountPolicy.EnsurePositive(total, fieldName);
        }

        return total;
    }

    private static void ValidateCurrencyCode(string currencyCode)
    {
        if (currencyCode is not { Length: 3 }
            || currencyCode.Any(character => character is < 'A' or > 'Z'))
        {
            throw new DomainRuleViolationException(
                "Payment settlement currency code must contain exactly three uppercase ASCII letters.");
        }
    }
}
