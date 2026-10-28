using Bms.Domain.Common;

namespace Bms.Application.Budgeting;

/// <summary>
/// Deterministic preview over the exact keys present in one explicit patch.
/// The preview is not an authorization, source-freshness claim or reusable
/// write plan: a writer must reread and rebuild inside its transaction.
/// </summary>
internal sealed class BudgetLimitImportDryRun
{
    private BudgetLimitImportDryRun(
        BudgetLimitImportBatchEvidence evidence,
        BudgetLimitImportPlan plan,
        int currentPositionCount,
        int currentKnownLimitCount,
        int resultingPositionCount,
        int resultingKnownLimitCount,
        decimal currentKnownLimitTotalKzt,
        decimal resultingKnownLimitTotalKzt,
        int overLimitAfterCount)
    {
        Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        CurrentPositionCount = currentPositionCount;
        CurrentKnownLimitCount = currentKnownLimitCount;
        ResultingPositionCount = resultingPositionCount;
        ResultingKnownLimitCount = resultingKnownLimitCount;
        CurrentKnownLimitTotalKzt = currentKnownLimitTotalKzt;
        ResultingKnownLimitTotalKzt = resultingKnownLimitTotalKzt;
        KnownLimitDeltaKzt = SubtractTotals(
            resultingKnownLimitTotalKzt,
            currentKnownLimitTotalKzt);
        OverLimitAfterCount = overLimitAfterCount;
    }

    internal BudgetLimitImportBatchEvidence Evidence { get; }

    internal BudgetLimitImportPlan Plan { get; }

    internal int CurrentPositionCount { get; }

    internal int CurrentKnownLimitCount { get; }

    internal int ResultingPositionCount { get; }

    internal int ResultingKnownLimitCount { get; }

    internal decimal CurrentKnownLimitTotalKzt { get; }

    internal decimal ResultingKnownLimitTotalKzt { get; }

    internal decimal KnownLimitDeltaKzt { get; }

    internal int OverLimitAfterCount { get; }

    internal bool HasChanges => Plan.ChangeCount != 0;

    internal static BudgetLimitImportDryRun Create(
        BudgetLimitImportBatchEvidence evidence,
        BudgetLimitImportPlan plan,
        int currentPositionCount,
        int currentKnownLimitCount,
        int resultingPositionCount,
        int resultingKnownLimitCount,
        decimal currentKnownLimitTotalKzt,
        decimal resultingKnownLimitTotalKzt,
        int overLimitAfterCount)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(plan);
        if (evidence.ItemCount != plan.Assignments.Count ||
            currentPositionCount is < 0 || currentPositionCount > plan.Assignments.Count ||
            currentKnownLimitCount is < 0 || currentKnownLimitCount > currentPositionCount ||
            resultingPositionCount is < 0 || resultingPositionCount > plan.Assignments.Count ||
            resultingKnownLimitCount is < 0 ||
            resultingKnownLimitCount > resultingPositionCount ||
            evidence.LimitValueCount != resultingKnownLimitCount ||
            evidence.ExplicitLimitTotalKzt != resultingKnownLimitTotalKzt ||
            overLimitAfterCount is < 0 || overLimitAfterCount > resultingKnownLimitCount ||
            currentKnownLimitTotalKzt < 0m || resultingKnownLimitTotalKzt < 0m)
        {
            throw new ArgumentException("The budget-limit dry-run summary is inconsistent.");
        }

        return new BudgetLimitImportDryRun(
            evidence,
            plan,
            currentPositionCount,
            currentKnownLimitCount,
            resultingPositionCount,
            resultingKnownLimitCount,
            currentKnownLimitTotalKzt,
            resultingKnownLimitTotalKzt,
            overLimitAfterCount);
    }

    private static decimal SubtractTotals(decimal left, decimal right)
    {
        try
        {
            return checked(left - right);
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException(
                "Budget-limit dry-run totals exceed the supported aggregate range.");
        }
    }
}

internal static class BudgetLimitImportDryRunBuilder
{
    internal static BudgetLimitImportDryRun Build(
        BudgetLimitImportBatch batch,
        IEnumerable<BudgetLimitPositionSnapshot> currentPositions)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(currentPositions);

        var currentSnapshot = currentPositions.ToArray();
        var plan = BudgetLimitImportPlanBuilder.Build(batch, currentSnapshot);
        var evidence = BudgetLimitImportBatchEvidence.Capture(batch);

        var currentPositionCount = 0;
        var currentKnownLimitCount = 0;
        var resultingPositionCount = 0;
        var resultingKnownLimitCount = 0;
        var currentKnownLimitTotalKzt = 0m;
        var resultingKnownLimitTotalKzt = 0m;
        var overLimitAfterCount = 0;

        foreach (var assignment in plan.Assignments)
        {
            if (assignment.Current is not null)
            {
                currentPositionCount++;
                if (assignment.Current.LimitAmountKzt is decimal currentLimit)
                {
                    currentKnownLimitCount++;
                    currentKnownLimitTotalKzt = AddTotal(
                        currentKnownLimitTotalKzt,
                        currentLimit);
                }
            }

            if (assignment.Current is not null || assignment.Item.LimitAmountKzt.HasValue)
            {
                resultingPositionCount++;
            }

            if (assignment.Item.LimitAmountKzt is decimal resultingLimit)
            {
                resultingKnownLimitCount++;
                resultingKnownLimitTotalKzt = AddTotal(
                    resultingKnownLimitTotalKzt,
                    resultingLimit);
                if (assignment.Current is not null &&
                    AddTotal(
                        assignment.Current.ReservedAmountKzt,
                        assignment.Current.PaidAmountKzt) > resultingLimit)
                {
                    overLimitAfterCount++;
                }
            }
        }

        return BudgetLimitImportDryRun.Create(
            evidence,
            plan,
            currentPositionCount,
            currentKnownLimitCount,
            resultingPositionCount,
            resultingKnownLimitCount,
            currentKnownLimitTotalKzt,
            resultingKnownLimitTotalKzt,
            overLimitAfterCount);
    }

    private static decimal AddTotal(decimal total, decimal value)
    {
        try
        {
            return checked(total + value);
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException(
                "Budget-limit dry-run totals exceed the supported aggregate range.");
        }
    }
}
