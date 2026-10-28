using Bms.Domain.Common;

namespace Bms.Domain.Overpayments;

public sealed record OverpaymentScope(
    Guid OrganizationId,
    Guid CounterpartyId,
    string CurrencyCode);

public sealed record OverpaymentSourceSnapshot(
    Guid SourceId,
    Guid OrganizationId,
    Guid CounterpartyId,
    string CurrencyCode,
    DateTimeOffset OccurredAtUtc,
    decimal CurrentRemainingAmount,
    decimal CurrentRemainingAmountKzt,
    decimal ReservedAmount,
    decimal ReservedAmountKzt);

public sealed record OverpaymentSourceReservationAllocation(
    Guid SourceId,
    DateTimeOffset OccurredAtUtc,
    decimal AvailableAmountBefore,
    decimal AvailableAmountKztBefore,
    decimal SelectedAmount,
    decimal SelectedAmountKzt,
    decimal AvailableAmountAfter,
    decimal AvailableAmountKztAfter);

public sealed record OverpaymentSourceReservationPlan(
    OverpaymentScope Scope,
    decimal RequestedAmount,
    decimal SelectedAmount,
    decimal SelectedAmountKzt,
    decimal ShortfallAmount,
    IReadOnlyList<OverpaymentSourceReservationAllocation> Allocations);

/// <summary>
/// Plans source selection over snapshots already read under the caller's
/// managed lock. It performs no writes and does not assign reservation status,
/// actor, command, request or audit identifiers.
/// </summary>
public static class OverpaymentSourceReservationPlanner
{
    public static OverpaymentSourceReservationPlan Plan(
        OverpaymentScope scope,
        decimal requestedAmount,
        bool hasActiveDeficit,
        IEnumerable<OverpaymentSourceSnapshot> sourceSnapshots)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ValidateScope(scope);
        MoneyAmountPolicy.EnsurePositive(requestedAmount, "Requested overpayment reservation");
        ArgumentNullException.ThrowIfNull(sourceSnapshots);

        if (hasActiveDeficit)
        {
            throw new DomainRuleViolationException(
                "An active overpayment deficit blocks new reservations for the scope.");
        }

        var sources = sourceSnapshots.ToArray();
        ValidateSources(sources);

        var eligible = sources
            .Where(source =>
                source.OrganizationId == scope.OrganizationId
                && source.CounterpartyId == scope.CounterpartyId
                && string.Equals(source.CurrencyCode, scope.CurrencyCode, StringComparison.Ordinal))
            .Select(CreateAvailableSource)
            .Where(source => source.AvailableAmount > 0m)
            .OrderBy(source => source.Snapshot.OccurredAtUtc)
            .ThenBy(
                source => source.Snapshot.SourceId.ToString("D"),
                StringComparer.Ordinal)
            .ToArray();

        var remainingRequested = requestedAmount;
        var selectedAmountKzt = 0m;
        var allocations = new List<OverpaymentSourceReservationAllocation>();
        foreach (var source in eligible)
        {
            if (remainingRequested == 0m)
            {
                break;
            }

            var selectedAmount = decimal.Min(source.AvailableAmount, remainingRequested);
            var selectedSourceKzt = PairedMoneyAllocationPolicy.Allocate(
                source.AvailableAmountKzt,
                selectedAmount,
                source.AvailableAmount);
            selectedAmountKzt = AddRepresentable(
                selectedAmountKzt,
                selectedSourceKzt,
                "Selected overpayment KZT total");

            allocations.Add(new OverpaymentSourceReservationAllocation(
                source.Snapshot.SourceId,
                source.Snapshot.OccurredAtUtc,
                source.AvailableAmount,
                source.AvailableAmountKzt,
                selectedAmount,
                selectedSourceKzt,
                source.AvailableAmount - selectedAmount,
                source.AvailableAmountKzt - selectedSourceKzt));
            remainingRequested -= selectedAmount;
        }

        var selectedAmountTotal = requestedAmount - remainingRequested;
        return new OverpaymentSourceReservationPlan(
            scope,
            requestedAmount,
            selectedAmountTotal,
            selectedAmountKzt,
            remainingRequested,
            Array.AsReadOnly(allocations.ToArray()));
    }

    private static AvailableSource CreateAvailableSource(OverpaymentSourceSnapshot source)
    {
        var availableAmount = source.CurrentRemainingAmount - source.ReservedAmount;
        var availableAmountKzt = source.CurrentRemainingAmountKzt - source.ReservedAmountKzt;
        if ((availableAmount == 0m) != (availableAmountKzt == 0m))
        {
            throw new DomainRuleViolationException(
                "An overpayment source available currency and KZT pair must close together.");
        }

        return new AvailableSource(source, availableAmount, availableAmountKzt);
    }

    private static void ValidateScope(OverpaymentScope scope)
    {
        if (scope.OrganizationId == Guid.Empty || scope.CounterpartyId == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "Overpayment scope requires nonempty organization and counterparty ids.");
        }
        ValidateCurrencyCode(scope.CurrencyCode, "Overpayment scope");
    }

    private static void ValidateSources(IReadOnlyList<OverpaymentSourceSnapshot> sources)
    {
        var sourceIds = new HashSet<Guid>();
        foreach (var source in sources)
        {
            if (source is null)
            {
                throw new DomainRuleViolationException("Overpayment source snapshot cannot be null.");
            }
            if (source.SourceId == Guid.Empty || !sourceIds.Add(source.SourceId))
            {
                throw new DomainRuleViolationException(
                    "Overpayment source ids must be nonempty and unique.");
            }
            if (source.OrganizationId == Guid.Empty || source.CounterpartyId == Guid.Empty)
            {
                throw new DomainRuleViolationException(
                    "Overpayment source requires nonempty organization and counterparty ids.");
            }
            ValidateCurrencyCode(source.CurrencyCode, "Overpayment source");
            if (source.OccurredAtUtc == default)
            {
                throw new DomainRuleViolationException(
                    "Overpayment source occurrence time is required for FIFO.");
            }

            MoneyAmountPolicy.EnsureNonNegative(
                source.CurrentRemainingAmount,
                "Overpayment source current remaining amount");
            MoneyAmountPolicy.EnsureNonNegative(
                source.CurrentRemainingAmountKzt,
                "Overpayment source current remaining KZT amount");
            MoneyAmountPolicy.EnsureNonNegative(
                source.ReservedAmount,
                "Overpayment source reserved amount");
            MoneyAmountPolicy.EnsureNonNegative(
                source.ReservedAmountKzt,
                "Overpayment source reserved KZT amount");
            if (source.ReservedAmount > source.CurrentRemainingAmount
                || source.ReservedAmountKzt > source.CurrentRemainingAmountKzt)
            {
                throw new DomainRuleViolationException(
                    "Overpayment source reservation cannot exceed its current paired balance.");
            }
            if ((source.CurrentRemainingAmount == 0m) !=
                    (source.CurrentRemainingAmountKzt == 0m)
                || (source.ReservedAmount == 0m) != (source.ReservedAmountKzt == 0m))
            {
                throw new DomainRuleViolationException(
                    "Overpayment source current and reserved currency/KZT pairs must close together.");
            }
            if (string.Equals(source.CurrencyCode, "KZT", StringComparison.Ordinal)
                && (source.CurrentRemainingAmount != source.CurrentRemainingAmountKzt
                    || source.ReservedAmount != source.ReservedAmountKzt))
            {
                throw new DomainRuleViolationException(
                    "A KZT overpayment source requires equal currency and KZT balances.");
            }
        }
    }

    private static decimal AddRepresentable(decimal current, decimal addition, string fieldName)
    {
        decimal total;
        try
        {
            total = current + addition;
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException($"{fieldName} exceeds decimal storage range.");
        }

        MoneyAmountPolicy.EnsureNonNegative(total, fieldName);
        return total;
    }

    private static void ValidateCurrencyCode(string currencyCode, string fieldName)
    {
        if (currencyCode is not { Length: 3 }
            || currencyCode.Any(character => character is < 'A' or > 'Z'))
        {
            throw new DomainRuleViolationException(
                $"{fieldName} currency must contain exactly three uppercase ASCII letters.");
        }
    }

    private sealed record AvailableSource(
        OverpaymentSourceSnapshot Snapshot,
        decimal AvailableAmount,
        decimal AvailableAmountKzt);
}
