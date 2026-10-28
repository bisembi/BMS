using System.Collections.ObjectModel;
using Bms.Domain.Common;

namespace Bms.Domain.ExpenseRequests;

/// <summary>
/// The immutable current balance of one request line that participates in a
/// managerial-accrual closing cycle. Amounts are paired: the request-currency
/// balance drives allocation, while KZT follows the immutable submission
/// snapshot and never a current exchange rate.
/// </summary>
public sealed record AccrualClosingLineBalance(
    Guid LineId,
    int SortOrder,
    decimal OriginalAmount,
    decimal OriginalAmountKzt,
    decimal ConfirmedClosedAmount,
    decimal ConfirmedClosedAmountKzt)
{
    public decimal RemainingAmount => OriginalAmount - ConfirmedClosedAmount;

    public decimal RemainingAmountKzt => OriginalAmountKzt - ConfirmedClosedAmountKzt;
}

/// <summary>
/// An optional accountant override of the proportional allocation. Omitted
/// lines receive zero; the full supplied set must still consume the exact
/// allocatable control amount and may never exceed a line balance.
/// </summary>
public sealed record AccrualClosingRequestedLineAllocation(
    Guid LineId,
    decimal Amount);

/// <summary>One positive line allocation produced for the current command.</summary>
public sealed record AccrualClosingLineAllocation(
    Guid LineId,
    int SortOrder,
    decimal Amount,
    decimal AmountKzt,
    decimal RemainingAmount,
    decimal RemainingAmountKzt);

/// <summary>
/// Pure closing plan. ControlAmount is what the accountant entered;
/// AllocatedAmount is capped by the active accrual, and ExcessAmount is kept
/// only as audit/control evidence rather than assigned to a line.
/// </summary>
public sealed class AccrualClosingAllocationPlan
{
    internal AccrualClosingAllocationPlan(
        decimal controlAmount,
        decimal allocatedAmount,
        decimal allocatedAmountKzt,
        decimal excessAmount,
        IReadOnlyList<AccrualClosingLineAllocation> lines)
    {
        ControlAmount = controlAmount;
        AllocatedAmount = allocatedAmount;
        AllocatedAmountKzt = allocatedAmountKzt;
        ExcessAmount = excessAmount;
        Lines = lines ?? throw new ArgumentNullException(nameof(lines));
    }

    public decimal ControlAmount { get; }

    public decimal AllocatedAmount { get; }

    public decimal AllocatedAmountKzt { get; }

    public decimal ExcessAmount { get; }

    public IReadOnlyList<AccrualClosingLineAllocation> Lines { get; }
}

/// <summary>
/// Implements the approved partial-closing rules: proportional allocation by
/// current request-currency remainder, two-decimal rounding with the residual
/// assigned to the last positive line, optional exact manual override, per-
/// line caps, and immutable-snapshot KZT pairing.
/// </summary>
public static class AccrualClosingAllocationPlanner
{
    public static AccrualClosingAllocationPlan Plan(
        decimal controlAmount,
        IReadOnlyList<AccrualClosingLineBalance> balances,
        IReadOnlyList<AccrualClosingRequestedLineAllocation>? manualAllocations = null)
    {
        MoneyAmountPolicy.EnsurePositive(controlAmount, "Closing control amount");
        ArgumentNullException.ThrowIfNull(balances);

        var ordered = ValidateAndOrderBalances(balances);
        var activeTotal = SumRepresentable(
            ordered.Select(balance => balance.RemainingAmount),
            "Active closing amount");
        var target = decimal.Min(controlAmount, activeTotal);
        var amountByLine = manualAllocations is null
            ? AllocateProportionally(ordered, target, activeTotal)
            : ValidateManualAllocations(ordered, manualAllocations, target);

        var lines = new List<AccrualClosingLineAllocation>();
        var allocatedKzt = 0m;
        foreach (var balance in ordered)
        {
            var amount = amountByLine.TryGetValue(balance.LineId, out var requested)
                ? requested
                : 0m;
            if (amount == 0m)
            {
                continue;
            }

            var amountKzt = PairedMoneyAllocationPolicy.Allocate(
                balance.RemainingAmountKzt,
                amount,
                balance.RemainingAmount);
            var nextRemaining = balance.RemainingAmount - amount;
            var nextRemainingKzt = balance.RemainingAmountKzt - amountKzt;
            MoneyAmountPolicy.EnsureNonNegative(nextRemaining, "Remaining closing amount");
            MoneyAmountPolicy.EnsureNonNegative(nextRemainingKzt, "Remaining closing KZT amount");
            allocatedKzt = AddRepresentable(
                allocatedKzt,
                amountKzt,
                "Allocated closing KZT amount");
            lines.Add(new AccrualClosingLineAllocation(
                balance.LineId,
                balance.SortOrder,
                amount,
                amountKzt,
                nextRemaining,
                nextRemainingKzt));
        }

        var allocated = SumRepresentable(
            amountByLine.Values,
            "Allocated closing amount");
        if (allocated != target)
        {
            throw InvariantViolation();
        }

        var excess = controlAmount - target;
        MoneyAmountPolicy.EnsureNonNegative(excess, "Closing control excess");
        return new AccrualClosingAllocationPlan(
            controlAmount,
            allocated,
            allocatedKzt,
            excess,
            new ReadOnlyCollection<AccrualClosingLineAllocation>(lines));
    }

    private static AccrualClosingLineBalance[] ValidateAndOrderBalances(
        IReadOnlyList<AccrualClosingLineBalance> balances)
    {
        var lineIds = new HashSet<Guid>();
        var sortOrders = new HashSet<int>();
        foreach (var balance in balances)
        {
            if (balance is null ||
                balance.LineId == Guid.Empty ||
                !lineIds.Add(balance.LineId) ||
                balance.SortOrder <= 0 ||
                !sortOrders.Add(balance.SortOrder))
            {
                throw InvariantViolation();
            }

            MoneyAmountPolicy.EnsurePositive(
                balance.OriginalAmount,
                "Original closing amount");
            MoneyAmountPolicy.EnsureNonNegative(
                balance.OriginalAmountKzt,
                "Original closing KZT amount");
            MoneyAmountPolicy.EnsureNonNegative(
                balance.ConfirmedClosedAmount,
                "Confirmed closed amount");
            MoneyAmountPolicy.EnsureNonNegative(
                balance.ConfirmedClosedAmountKzt,
                "Confirmed closed KZT amount");
            if (balance.ConfirmedClosedAmount > balance.OriginalAmount ||
                balance.ConfirmedClosedAmountKzt > balance.OriginalAmountKzt ||
                (balance.ConfirmedClosedAmount == balance.OriginalAmount &&
                 balance.ConfirmedClosedAmountKzt != balance.OriginalAmountKzt))
            {
                throw InvariantViolation();
            }
        }

        return balances.OrderBy(balance => balance.SortOrder).ToArray();
    }

    private static IReadOnlyDictionary<Guid, decimal> AllocateProportionally(
        IReadOnlyList<AccrualClosingLineBalance> balances,
        decimal target,
        decimal activeTotal)
    {
        if (target == 0m)
        {
            return new Dictionary<Guid, decimal>();
        }

        var active = balances
            .Where(balance => balance.RemainingAmount > 0m)
            .ToArray();
        if (active.Length == 0 || activeTotal <= 0m)
        {
            throw InvariantViolation();
        }

        var allocations = new Dictionary<Guid, decimal>();
        var allocated = 0m;
        var futureCapacity = activeTotal;
        for (var index = 0; index < active.Length; index++)
        {
            var balance = active[index];
            futureCapacity -= balance.RemainingAmount;
            var remainingTarget = target - allocated;
            var minimumHere = decimal.Max(0m, remainingTarget - futureCapacity);
            var maximumHere = decimal.Min(balance.RemainingAmount, remainingTarget);
            decimal amount;
            if (index == active.Length - 1)
            {
                amount = remainingTarget;
            }
            else
            {
                var proportional = PairedMoneyAllocationPolicy.Allocate(
                    target,
                    balance.RemainingAmount,
                    activeTotal);
                amount = decimal.Clamp(proportional, minimumHere, maximumHere);
            }

            MoneyAmountPolicy.EnsureNonNegative(amount, "Proportional closing allocation");
            if (amount > 0m)
            {
                allocations.Add(balance.LineId, amount);
                allocated = AddRepresentable(
                    allocated,
                    amount,
                    "Allocated closing amount");
            }
        }

        if (allocated != target)
        {
            throw InvariantViolation();
        }

        return allocations;
    }

    private static IReadOnlyDictionary<Guid, decimal> ValidateManualAllocations(
        IReadOnlyList<AccrualClosingLineBalance> balances,
        IReadOnlyList<AccrualClosingRequestedLineAllocation> manualAllocations,
        decimal target)
    {
        ArgumentNullException.ThrowIfNull(manualAllocations);
        var balanceById = balances.ToDictionary(balance => balance.LineId);
        var allocations = new Dictionary<Guid, decimal>();
        foreach (var allocation in manualAllocations)
        {
            if (allocation is null ||
                allocation.LineId == Guid.Empty ||
                !balanceById.TryGetValue(allocation.LineId, out var balance) ||
                allocations.ContainsKey(allocation.LineId))
            {
                throw new DomainRuleViolationException(
                    "Manual closing allocations must reference unique cycle lines.");
            }

            MoneyAmountPolicy.EnsureNonNegative(
                allocation.Amount,
                "Manual closing allocation");
            if (allocation.Amount > balance.RemainingAmount)
            {
                throw new DomainRuleViolationException(
                    "A manual closing allocation exceeds the active line balance.");
            }

            allocations.Add(allocation.LineId, allocation.Amount);
        }

        var allocated = SumRepresentable(
            allocations.Values,
            "Manual closing allocation total");
        if (allocated != target)
        {
            throw new DomainRuleViolationException(
                "Manual closing allocations must equal the allocatable control amount.");
        }

        return allocations;
    }

    private static decimal SumRepresentable(
        IEnumerable<decimal> values,
        string fieldName)
    {
        var total = 0m;
        foreach (var value in values)
        {
            total = AddRepresentable(total, value, fieldName);
        }

        return total;
    }

    private static decimal AddRepresentable(
        decimal total,
        decimal value,
        string fieldName)
    {
        try
        {
            total = checked(total + value);
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException($"{fieldName} exceeds decimal storage range.");
        }

        MoneyAmountPolicy.EnsureNonNegative(total, fieldName);
        return total;
    }

    private static DomainRuleViolationException InvariantViolation() => new(
        "Accrual closing balances violate the domain invariant.");
}
