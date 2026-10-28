using Bms.Application.Abstractions;

namespace Bms.Application.ExpenseRequests;

internal sealed record AccrualSnapshotMaterializationTarget(
    int LineNumber,
    Guid LineId);

internal sealed record AccrualSnapshotMaterializationAssignment(
    Guid LineId,
    bool FormsManagerialAccrual,
    Guid? AccrualExpenseItemId);

internal sealed class AccrualSnapshotMaterializationPlan
{
    private AccrualSnapshotMaterializationPlan(
        Guid expenseRequestId,
        IEnumerable<AccrualSnapshotMaterializationAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        ExpenseRequestId = expenseRequestId;
        Assignments = Array.AsReadOnly(assignments.ToArray());
    }

    internal Guid ExpenseRequestId { get; }

    internal IReadOnlyList<AccrualSnapshotMaterializationAssignment> Assignments { get; }

    internal static AccrualSnapshotMaterializationPlan? TryBuild(
        Guid requestId,
        IReadOnlyList<AccrualSnapshotMaterializationTarget> orderedPersistedLines,
        AccrualSnapshotSourceResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(orderedPersistedLines);
        ArgumentNullException.ThrowIfNull(resolution);
        ValidateRequestId(requestId);

        var targets = orderedPersistedLines.ToArray();
        ValidateTargets(targets);
        var issues = resolution.Issues?.ToArray() ?? throw InvariantViolation();
        var candidates = resolution.Candidates?.ToArray() ?? throw InvariantViolation();

        if (issues.Length > 0)
        {
            if (candidates.Length == 0)
            {
                return null;
            }

            throw InvariantViolation();
        }

        if (candidates.Length != targets.Length ||
            candidates.Any(candidate => candidate is null || candidate.LineId == Guid.Empty) ||
            candidates.Select(candidate => candidate.LineId).Distinct().Count() != candidates.Length)
        {
            throw InvariantViolation();
        }

        foreach (var candidate in candidates)
        {
            if ((!candidate.FormsManagerialAccrual && candidate.AccrualExpenseItemId is not null) ||
                (candidate.FormsManagerialAccrual &&
                 (candidate.AccrualExpenseItemId is null ||
                  candidate.AccrualExpenseItemId == Guid.Empty)))
            {
                throw InvariantViolation();
            }
        }

        var targetIds = targets.Select(target => target.LineId).ToHashSet();
        var candidateIds = candidates.Select(candidate => candidate.LineId).ToHashSet();
        if (!targetIds.SetEquals(candidateIds))
        {
            throw InvariantViolation();
        }

        var candidateByLineId = candidates.ToDictionary(candidate => candidate.LineId);
        var assignments = targets
            .OrderBy(target => target.LineNumber)
            .Select(target =>
            {
                var candidate = candidateByLineId[target.LineId];
                return new AccrualSnapshotMaterializationAssignment(
                    target.LineId,
                    candidate.FormsManagerialAccrual,
                    candidate.AccrualExpenseItemId);
            })
            .ToArray();

        return new AccrualSnapshotMaterializationPlan(requestId, assignments);
    }

    private static void ValidateRequestId(Guid requestId)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Expense request id must be non-empty.",
                nameof(requestId));
        }
    }

    private static void ValidateTargets(
        IReadOnlyList<AccrualSnapshotMaterializationTarget> targets)
    {
        if (targets.Count == 0 ||
            targets.Any(target =>
                target is null || target.LineNumber <= 0 || target.LineId == Guid.Empty) ||
            targets.Select(target => target.LineNumber).Distinct().Count() != targets.Count ||
            targets.Select(target => target.LineId).Distinct().Count() != targets.Count)
        {
            throw InvariantViolation();
        }
    }

    private static InvalidOperationException InvariantViolation() => new(
        "Accrual snapshot materialization plan inputs violate the internal invariant.");
}

internal static class AccrualSnapshotMaterializationPlanBuilder
{
    internal static AccrualSnapshotMaterializationPlan? TryBuild(
        Guid requestId,
        IReadOnlyList<AccrualSnapshotMaterializationTarget> orderedPersistedLines,
        AccrualSnapshotSourceResolution resolution) =>
        AccrualSnapshotMaterializationPlan.TryBuild(
            requestId,
            orderedPersistedLines,
            resolution);
}
