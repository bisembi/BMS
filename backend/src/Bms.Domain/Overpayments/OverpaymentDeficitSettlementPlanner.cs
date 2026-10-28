using Bms.Domain.Common;

namespace Bms.Domain.Overpayments;

public enum RealOverpaymentDeficitSettlementKind
{
    NewOverpayment = 1,
    AdditionalPayment = 2,
    RestoredReversedPayment = 3
}

public sealed record OverpaymentDeficitSnapshot(
    Guid DeficitId,
    Guid OrganizationId,
    Guid CounterpartyId,
    string CurrencyCode,
    DateTimeOffset OccurredAtUtc,
    decimal OriginalAmount,
    decimal OriginalAmountKzt,
    decimal CurrentAmount,
    decimal CurrentAmountKzt);

public sealed record OverpaymentDeficitSettlementAllocation(
    Guid DeficitId,
    DateTimeOffset OccurredAtUtc,
    decimal AmountBefore,
    decimal AmountKztBefore,
    decimal AppliedAmount,
    decimal AppliedAmountKzt,
    decimal AmountAfter,
    decimal AmountKztAfter,
    bool IsClosed);

public sealed record OverpaymentDeficitSettlementPlan(
    OverpaymentScope Scope,
    RealOverpaymentDeficitSettlementKind Kind,
    decimal SourceAmount,
    decimal SourceAmountKzt,
    decimal AppliedAmount,
    decimal AppliedAmountKzt,
    decimal RemainingSourceAmount,
    decimal RemainingSourceAmountKzt,
    bool HasUnsettledDeficit,
    IReadOnlyList<OverpaymentDeficitSettlementAllocation> Allocations);

/// <summary>
/// Plans application of one real paired source to active deficits already read
/// under the caller's managed scope lock. Manual corrections and reconsidering
/// an earlier request are addressed commands and are intentionally not planned
/// here. The result performs no writes and assigns no command or audit identity.
/// </summary>
public static class OverpaymentDeficitSettlementPlanner
{
    public static OverpaymentDeficitSettlementPlan Plan(
        OverpaymentScope scope,
        RealOverpaymentDeficitSettlementKind kind,
        decimal sourceAmount,
        decimal sourceAmountKzt,
        IEnumerable<OverpaymentDeficitSnapshot> deficitSnapshots)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ValidateScope(scope);
        ValidateKind(kind);
        MoneyAmountPolicy.EnsurePositive(sourceAmount, "Deficit settlement source amount");
        MoneyAmountPolicy.EnsurePositive(sourceAmountKzt, "Deficit settlement source KZT amount");
        if (string.Equals(scope.CurrencyCode, "KZT", StringComparison.Ordinal)
            && sourceAmount != sourceAmountKzt)
        {
            throw new DomainRuleViolationException(
                "A KZT deficit settlement source requires equal currency and KZT amounts.");
        }
        ArgumentNullException.ThrowIfNull(deficitSnapshots);

        var snapshots = deficitSnapshots.ToArray();
        ValidateSnapshots(snapshots);
        var eligible = snapshots
            .Where(snapshot =>
                snapshot.OrganizationId == scope.OrganizationId
                && snapshot.CounterpartyId == scope.CounterpartyId
                && string.Equals(snapshot.CurrencyCode, scope.CurrencyCode, StringComparison.Ordinal))
            .Where(snapshot => snapshot.CurrentAmount > 0m)
            .OrderBy(snapshot => snapshot.OccurredAtUtc)
            .ThenBy(
                snapshot => snapshot.DeficitId.ToString("D"),
                StringComparer.Ordinal)
            .ToArray();

        var remainingSourceAmount = sourceAmount;
        var remainingSourceAmountKzt = sourceAmountKzt;
        var allocations = new List<OverpaymentDeficitSettlementAllocation>();
        foreach (var deficit in eligible)
        {
            if (remainingSourceAmount == 0m)
            {
                break;
            }

            var appliedAmount = decimal.Min(deficit.CurrentAmount, remainingSourceAmount);
            var appliedAmountKzt = PairedMoneyAllocationPolicy.Allocate(
                remainingSourceAmountKzt,
                appliedAmount,
                remainingSourceAmount);
            if (appliedAmountKzt == 0m)
            {
                throw new DomainRuleViolationException(
                    "Deficit settlement cannot apply a positive currency amount with zero KZT.");
            }
            if (appliedAmountKzt > deficit.CurrentAmountKzt)
            {
                throw new DomainRuleViolationException(
                    "Deficit settlement source KZT amount exceeds the paired deficit balance.");
            }

            var amountAfter = deficit.CurrentAmount - appliedAmount;
            var amountKztAfter = deficit.CurrentAmountKzt - appliedAmountKzt;
            EnsurePairedState(
                amountAfter,
                amountKztAfter,
                "Deficit settlement cannot leave an unpaired deficit balance.");

            remainingSourceAmount -= appliedAmount;
            remainingSourceAmountKzt -= appliedAmountKzt;
            EnsurePairedState(
                remainingSourceAmount,
                remainingSourceAmountKzt,
                "Deficit settlement cannot leave an unpaired source balance.");

            allocations.Add(new OverpaymentDeficitSettlementAllocation(
                deficit.DeficitId,
                deficit.OccurredAtUtc,
                deficit.CurrentAmount,
                deficit.CurrentAmountKzt,
                appliedAmount,
                appliedAmountKzt,
                amountAfter,
                amountKztAfter,
                amountAfter == 0m));
        }

        var appliedAmountTotal = sourceAmount - remainingSourceAmount;
        var appliedAmountKztTotal = sourceAmountKzt - remainingSourceAmountKzt;
        return new OverpaymentDeficitSettlementPlan(
            scope,
            kind,
            sourceAmount,
            sourceAmountKzt,
            appliedAmountTotal,
            appliedAmountKztTotal,
            remainingSourceAmount,
            remainingSourceAmountKzt,
            allocations.Count < eligible.Length
                || allocations.Any(allocation => allocation.AmountAfter > 0m),
            Array.AsReadOnly(allocations.ToArray()));
    }

    private static void ValidateScope(OverpaymentScope scope)
    {
        if (scope.OrganizationId == Guid.Empty || scope.CounterpartyId == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "Deficit settlement requires organization and counterparty scope.");
        }
        ValidateCurrencyCode(scope.CurrencyCode);
    }

    private static void ValidateKind(RealOverpaymentDeficitSettlementKind kind)
    {
        if (kind is not RealOverpaymentDeficitSettlementKind.NewOverpayment
            and not RealOverpaymentDeficitSettlementKind.AdditionalPayment
            and not RealOverpaymentDeficitSettlementKind.RestoredReversedPayment)
        {
            throw new DomainRuleViolationException(
                "Deficit settlement requires a supported real source kind.");
        }
    }

    private static void ValidateSnapshots(IReadOnlyList<OverpaymentDeficitSnapshot> snapshots)
    {
        var deficitIds = new HashSet<Guid>();
        foreach (var snapshot in snapshots)
        {
            if (snapshot is null)
            {
                throw new DomainRuleViolationException(
                    "Overpayment deficit snapshot cannot be null.");
            }
            if (snapshot.DeficitId == Guid.Empty || !deficitIds.Add(snapshot.DeficitId))
            {
                throw new DomainRuleViolationException(
                    "Overpayment deficit ids must be nonempty and unique.");
            }
            if (snapshot.OrganizationId == Guid.Empty
                || snapshot.CounterpartyId == Guid.Empty
                || snapshot.OccurredAtUtc == default)
            {
                throw new DomainRuleViolationException(
                    "Overpayment deficit requires scope and occurrence time.");
            }
            ValidateCurrencyCode(snapshot.CurrencyCode);
            MoneyAmountPolicy.EnsurePositive(
                snapshot.OriginalAmount,
                "Deficit original amount");
            MoneyAmountPolicy.EnsurePositive(
                snapshot.OriginalAmountKzt,
                "Deficit original KZT amount");
            MoneyAmountPolicy.EnsureNonNegative(
                snapshot.CurrentAmount,
                "Deficit current amount");
            MoneyAmountPolicy.EnsureNonNegative(
                snapshot.CurrentAmountKzt,
                "Deficit current KZT amount");
            if (snapshot.CurrentAmount > snapshot.OriginalAmount
                || snapshot.CurrentAmountKzt > snapshot.OriginalAmountKzt)
            {
                throw new DomainRuleViolationException(
                    "Overpayment deficit current pair cannot exceed its original pair.");
            }
            EnsurePairedState(
                snapshot.CurrentAmount,
                snapshot.CurrentAmountKzt,
                "Overpayment deficit current currency and KZT balances must close together.");
            if (string.Equals(snapshot.CurrencyCode, "KZT", StringComparison.Ordinal)
                && (snapshot.OriginalAmount != snapshot.OriginalAmountKzt
                    || snapshot.CurrentAmount != snapshot.CurrentAmountKzt))
            {
                throw new DomainRuleViolationException(
                    "A KZT overpayment deficit requires equal currency and KZT pairs.");
            }
        }
    }

    private static void EnsurePairedState(decimal amount, decimal amountKzt, string message)
    {
        if ((amount == 0m) != (amountKzt == 0m))
        {
            throw new DomainRuleViolationException(message);
        }
    }

    private static void ValidateCurrencyCode(string currencyCode)
    {
        if (currencyCode is not { Length: 3 }
            || currencyCode.Any(character => character is < 'A' or > 'Z'))
        {
            throw new DomainRuleViolationException(
                "Deficit settlement currency must contain exactly three uppercase ASCII letters.");
        }
    }
}
