using Bms.Application.Abstractions;

namespace Bms.Application.ExpenseRequests;

public sealed record AccrualCashFlowItemSettings(
    Guid Id,
    bool IsActive,
    bool IsGroup,
    bool FormsManagerialAccrual,
    Guid? AccrualExpenseItemId);

public static class AccrualSnapshotSourceEvaluator
{
    public static AccrualSnapshotSourceResolution Evaluate(
        IReadOnlyList<AccrualSnapshotSourceLine> lines,
        IReadOnlyCollection<AccrualCashFlowItemSettings> items,
        IReadOnlySet<Guid> existingExpenseItemIds)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(existingExpenseItemIds);
        ValidateLineIds(lines);
        var byId = items.ToDictionary(item => item.Id);
        var issues = new List<AccrualSnapshotSourceIssue>();
        var candidates = new List<AccrualSnapshotSourceCandidate>();

        foreach (var line in lines.OrderBy(line => line.LineNumber))
        {
            if (line.CashFlowItemId is not { } itemId || itemId == Guid.Empty)
            {
                continue;
            }

            if (!byId.TryGetValue(itemId, out var item))
            {
                Add(line, SubmissionPreflightBlockerCodes.CashFlowItemMissing, issues);
                continue;
            }

            if (item.IsGroup)
            {
                Add(line, SubmissionPreflightBlockerCodes.CashFlowItemGroup, issues);
                continue;
            }

            if (!item.IsActive)
            {
                Add(line, SubmissionPreflightBlockerCodes.CashFlowItemInactive, issues);
                continue;
            }

            if (!item.FormsManagerialAccrual)
            {
                if (item.AccrualExpenseItemId is { } staleMappingId &&
                    staleMappingId != Guid.Empty)
                {
                    AddUnavailable(issues);
                }
                else
                {
                    candidates.Add(new AccrualSnapshotSourceCandidate(
                        line.LineId,
                        false,
                        null));
                }

                if (line.AccrualPeriod is { Day: not 1 })
                {
                    Add(line, SubmissionPreflightBlockerCodes.AccrualPeriodNotMonthStart, issues);
                }

                continue;
            }

            if (!item.AccrualExpenseItemId.HasValue || item.AccrualExpenseItemId == Guid.Empty)
            {
                AddUnavailable(issues);
            }
            else if (!existingExpenseItemIds.Contains(item.AccrualExpenseItemId.Value))
            {
                AddUnavailable(issues);
            }
            else
            {
                candidates.Add(new AccrualSnapshotSourceCandidate(
                    line.LineId,
                    true,
                    item.AccrualExpenseItemId.Value));
            }

            if (!line.AccrualPeriod.HasValue)
            {
                Add(line, SubmissionPreflightBlockerCodes.AccrualPeriodMissing, issues);
            }
            else if (line.AccrualPeriod.Value.Day != 1)
            {
                Add(line, SubmissionPreflightBlockerCodes.AccrualPeriodNotMonthStart, issues);
            }
        }

        return new AccrualSnapshotSourceResolution(
            issues,
            issues.Count == 0 && candidates.Count == lines.Count
                ? candidates
                : []);
    }

    private static void ValidateLineIds(IReadOnlyList<AccrualSnapshotSourceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Any(line => line.LineId == Guid.Empty) ||
            lines.Select(line => line.LineId).Distinct().Count() != lines.Count)
        {
            throw new InvalidOperationException(
                "Accrual snapshot source lines must have non-empty unique persisted ids.");
        }
    }

    private static void AddUnavailable(ICollection<AccrualSnapshotSourceIssue> issues)
    {
        if (issues.Any(issue =>
            issue.Code == SubmissionPreflightBlockerCodes.AccrualSnapshotSourceUnavailable))
        {
            return;
        }

        issues.Add(new AccrualSnapshotSourceIssue(
            null,
            SubmissionPreflightBlockerCodes.AccrualSnapshotSourceUnavailable));
    }

    private static void Add(
        AccrualSnapshotSourceLine line,
        string code,
        ICollection<AccrualSnapshotSourceIssue> issues) =>
        issues.Add(new AccrualSnapshotSourceIssue(line.LineNumber, code));
}
