using Bms.Domain.Common;

namespace Bms.Domain.Budgeting;

/// <summary>
/// A successful, immutable budget approval used by the read-time effective
/// limit projection. The storage layer supplies the complete line set for the
/// version; the resolver deliberately does not know how that set was loaded.
/// </summary>
public sealed record BudgetEffectiveVersion(
    Guid Id,
    Guid CostCenterId,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    long ApprovalOrdinal,
    DateTimeOffset ApprovedAtUtc,
    IReadOnlyCollection<BudgetEffectiveLine> Lines);

public sealed record BudgetEffectiveLine(
    Guid CashFlowItemId,
    DateOnly BudgetMonth,
    decimal LimitAmountKzt);

/// <summary>
/// Resolves the published limit without mutating legacy budget positions.
///
/// An approved version replaces the whole covered interval: an omitted line
/// is therefore an explicit zero. A key outside every approved version is also
/// an explicit zero; there is no "undefined budget" state in the published
/// contract. The legacy value is accepted as an input only so callers can
/// project old rows without rewriting them; it is never used as a fallback.
/// </summary>
public static class BudgetEffectiveLimitResolver
{
    public static decimal Resolve(
        decimal? legacyLimitAmountKzt,
        Guid costCenterId,
        Guid cashFlowItemId,
        DateOnly budgetMonth,
        IReadOnlyCollection<BudgetEffectiveVersion> approvedVersions)
    {
        ArgumentNullException.ThrowIfNull(approvedVersions);
        if (costCenterId == Guid.Empty)
        {
            throw new ArgumentException("Cost center is required.", nameof(costCenterId));
        }

        if (cashFlowItemId == Guid.Empty)
        {
            throw new ArgumentException("Cash-flow item is required.", nameof(cashFlowItemId));
        }

        var winner = approvedVersions
            .Where(version => version.CostCenterId == costCenterId
                && version.ValidFrom <= budgetMonth
                && version.ValidTo >= budgetMonth)
            .OrderByDescending(version => version.ApprovalOrdinal)
            .ThenByDescending(version => version.ApprovedAtUtc)
            .ThenByDescending(version => version.Id)
            .FirstOrDefault();

        if (winner is null)
        {
            return 0m;
        }

        var matchingLines = winner.Lines
            .Where(line => line.CashFlowItemId == cashFlowItemId
                && line.BudgetMonth == budgetMonth)
            .ToArray();
        foreach (var line in matchingLines)
            MoneyAmountPolicy.EnsureNonNegative(line.LimitAmountKzt, "Approved payment limit");
        var total = matchingLines.Sum(line => line.LimitAmountKzt);
        MoneyAmountPolicy.EnsureRepresentable(total, "Approved payment limit");
        return total;
    }
}
