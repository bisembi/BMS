using Bms.Domain.Common;
using Bms.Domain.Workflow;

namespace Bms.Domain.Budgeting;

/// <summary>
/// Pure, immutable comparison captured when a draft is submitted. The caller
/// must read complete Approved versions in the same Serializable CFO transaction
/// as submission. This policy neither grants authority nor publishes limits.
/// </summary>
public sealed class BudgetApprovalAssessment
{
    private BudgetApprovalAssessment(BudgetInstance proposal,
        IReadOnlyList<BudgetApprovalCellComparison> cells, decimal positiveExcessKzt)
    {
        BudgetInstanceId = proposal.Id;
        CostCenterId = proposal.CostCenterId;
        ProposalConcurrencyStamp = proposal.ConcurrencyStamp;
        Cells = cells;
        PositiveExcessKzt = positiveExcessKzt;
    }

    private BudgetApprovalAssessment(Guid budgetId, Guid costCenterId, Guid proposalStamp,
        IReadOnlyList<BudgetApprovalCellComparison> cells, decimal excess)
    {
        BudgetInstanceId = budgetId;
        CostCenterId = costCenterId;
        ProposalConcurrencyStamp = proposalStamp;
        Cells = cells;
        PositiveExcessKzt = excess;
    }

    public static BudgetApprovalAssessment Restore(Guid budgetId, Guid costCenterId, Guid proposalStamp,
        IReadOnlyCollection<BudgetApprovalCellComparison> cells, decimal excess)
    {
        if (budgetId == Guid.Empty || costCenterId == Guid.Empty || proposalStamp == Guid.Empty || cells is null
            || cells.Any(c => c is null || c.CashFlowItemId == Guid.Empty || c.BudgetMonth.Day != 1
                || c.ProposedPaymentKzt < 0 || c.ApprovedLimitKzt < 0
                || c.PositiveExcessKzt != Math.Max(c.ProposedPaymentKzt - c.ApprovedLimitKzt, 0m))
            || cells.Select(c => (c.CashFlowItemId, c.BudgetMonth)).Distinct().Count() != cells.Count
            || cells.Sum(c => c.PositiveExcessKzt) != excess)
            throw new DomainRuleViolationException("Stored budget comparison is inconsistent.");
        MoneyAmountPolicy.EnsureNonNegative(excess, "Budget excess");
        foreach (var cell in cells)
        {
            MoneyAmountPolicy.EnsureNonNegative(cell.ProposedPaymentKzt, "Proposed payment");
            MoneyAmountPolicy.EnsureNonNegative(cell.ApprovedLimitKzt, "Approved limit");
            MoneyAmountPolicy.EnsureNonNegative(cell.PositiveExcessKzt, "Cell excess");
            if (cell.BaselineBudgetInstanceId == Guid.Empty
                || (cell.BaselineBudgetInstanceId is null && cell.ApprovedLimitKzt != 0m))
                throw new DomainRuleViolationException("Stored baseline provenance is inconsistent.");
        }
        return new BudgetApprovalAssessment(budgetId, costCenterId, proposalStamp,
            Array.AsReadOnly(cells.ToArray()), excess);
    }

    public Guid BudgetInstanceId { get; }
    public Guid CostCenterId { get; }
    public Guid ProposalConcurrencyStamp { get; }
    public IReadOnlyList<BudgetApprovalCellComparison> Cells { get; }
    public decimal PositiveExcessKzt { get; }

    /// <summary>
    /// Each baseline includes both directions explicitly; Receipt cannot be
    /// misinterpreted as an expense limit. Newest covering header replaces the
    /// entire month, including omitted articles whose limit becomes zero.
    /// </summary>
    public static BudgetApprovalAssessment Capture(BudgetInstance proposal,
        IReadOnlyCollection<BudgetApprovalBaselineVersion> approvedVersions)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(approvedVersions);
        if (proposal.Status != BudgetInstanceStatus.Draft)
            throw new DomainRuleViolationException("Only a draft budget can be submitted for comparison.");

        var ids = new HashSet<Guid>();
        var effective = new List<BudgetEffectiveVersion>();
        foreach (var version in approvedVersions)
        {
            if (version is null || version.Id == Guid.Empty || version.Id == proposal.Id
                || !ids.Add(version.Id) || version.CostCenterId != proposal.CostCenterId
                || version.ApprovalOrdinal <= 0 || version.ApprovedAtUtc == default
                || version.ApprovedAtUtc.Offset != TimeSpan.Zero || version.Lines is null)
                throw new DomainRuleViolationException("Approved budget baseline is inconsistent.");
            BudgetInstance.EnsureValidityPeriod(version.ValidFrom, version.ValidTo);
            var payments = new List<BudgetEffectiveLine>();
            foreach (var line in version.Lines)
            {
                if (line is null || line.CashFlowItemId == Guid.Empty || line.BudgetMonth.Day != 1
                    || line.BudgetMonth < version.ValidFrom || line.BudgetMonth > version.ValidTo
                    || !Enum.IsDefined(line.FlowDirection))
                    throw new DomainRuleViolationException("Approved budget baseline lines are inconsistent.");
                MoneyAmountPolicy.EnsureNonNegative(line.AmountKzt, "Approved payment limit");
                if (line.FlowDirection == BudgetFlowDirection.Payment)
                    payments.Add(new BudgetEffectiveLine(line.CashFlowItemId, line.BudgetMonth, line.AmountKzt));
            }
            effective.Add(new BudgetEffectiveVersion(version.Id, version.CostCenterId,
                version.ValidFrom, version.ValidTo, version.ApprovalOrdinal, version.ApprovedAtUtc,
                Array.AsReadOnly(payments.ToArray())));
        }

        var cells = new List<BudgetApprovalCellComparison>();
        decimal excess = 0m;
        foreach (var line in proposal.Lines.Where(line => line.FlowDirection == BudgetFlowDirection.Payment)
            .GroupBy(line => (line.CashFlowItemId, line.BudgetMonth))
            .Select(cell => new { cell.Key.CashFlowItemId, cell.Key.BudgetMonth,
                LimitAmountKzt = cell.Sum(line => line.LimitAmountKzt) })
            .OrderBy(line => line.BudgetMonth).ThenBy(line => line.CashFlowItemId))
        {
            MoneyAmountPolicy.EnsureRepresentable(line.LimitAmountKzt, "Proposed payment");
            var winner = effective.Where(version => version.ValidFrom <= line.BudgetMonth
                    && version.ValidTo >= line.BudgetMonth)
                .OrderByDescending(version => version.ApprovalOrdinal)
                .ThenByDescending(version => version.ApprovedAtUtc)
                .ThenByDescending(version => version.Id).FirstOrDefault();
            var limit = BudgetEffectiveLimitResolver.Resolve(null, proposal.CostCenterId,
                line.CashFlowItemId, line.BudgetMonth, effective);
            var positive = Math.Max(line.LimitAmountKzt - limit, 0m);
            excess += positive;
            MoneyAmountPolicy.EnsureRepresentable(excess, "Budget approval excess");
            cells.Add(new BudgetApprovalCellComparison(line.CashFlowItemId, line.BudgetMonth,
                line.LimitAmountKzt, limit, positive, winner?.Id));
        }
        return new BudgetApprovalAssessment(proposal, Array.AsReadOnly(cells.ToArray()), excess);
    }

    /// <summary>
    /// Every new budget submission starts with the financial director. The
    /// legacy owner flag is rejected for new submissions, not silently ignored.
    /// </summary>
    public IReadOnlyList<ApprovalRouteStage> Plan(bool requiresOwnerReview)
    {
        if (requiresOwnerReview)
            throw new DomainRuleViolationException("New budget submissions cannot require owner review.");
        return BudgetApprovalRoutePlanner.Plan(PositiveExcessKzt);
    }
}

public sealed record BudgetApprovalBaselineVersion(Guid Id, Guid CostCenterId,
    DateOnly ValidFrom, DateOnly ValidTo, long ApprovalOrdinal, DateTimeOffset ApprovedAtUtc,
    IReadOnlyCollection<BudgetApprovalBaselineLine> Lines);

public sealed record BudgetApprovalBaselineLine(Guid CashFlowItemId, DateOnly BudgetMonth,
    decimal AmountKzt, BudgetFlowDirection FlowDirection = BudgetFlowDirection.Payment);

public sealed record BudgetApprovalCellComparison(Guid CashFlowItemId, DateOnly BudgetMonth,
    decimal ProposedPaymentKzt, decimal ApprovedLimitKzt, decimal PositiveExcessKzt,
    Guid? BaselineBudgetInstanceId);
