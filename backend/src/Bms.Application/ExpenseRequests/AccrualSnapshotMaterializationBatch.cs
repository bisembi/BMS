using Bms.Application.Abstractions;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

internal sealed class AccrualSnapshotMaterializationBatch
{
    private AccrualSnapshotMaterializationBatch(
        Guid expenseRequestId,
        IEnumerable<AccrualSnapshotSourceLine> sourceLines)
    {
        ArgumentNullException.ThrowIfNull(sourceLines);
        ExpenseRequestId = expenseRequestId;
        SourceLines = Array.AsReadOnly(sourceLines.ToArray());
    }

    internal Guid ExpenseRequestId { get; }

    internal IReadOnlyList<AccrualSnapshotSourceLine> SourceLines { get; }

    internal static AccrualSnapshotMaterializationBatch Create(ExpenseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Id == Guid.Empty)
        {
            throw InvariantViolation();
        }

        ExpenseRequestLine[] lines;
        try
        {
            lines = request.Lines.ToArray();
        }
        catch (NullReferenceException)
        {
            throw InvariantViolation();
        }

        if (lines.Length == 0)
        {
            throw InvariantViolation();
        }

        var seenLineIds = new HashSet<Guid>();
        var previousSortOrder = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line is null ||
                line.Id == Guid.Empty ||
                line.ExpenseRequestId != request.Id ||
                line.SortOrder <= previousSortOrder ||
                !seenLineIds.Add(line.Id))
            {
                throw InvariantViolation();
            }

            previousSortOrder = line.SortOrder;
        }

        var sourceLines = new AccrualSnapshotSourceLine[lines.Length];
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            sourceLines[index] = new AccrualSnapshotSourceLine(
                checked(index + 1),
                line.Id,
                line.CashFlowItemId,
                line.AccrualPeriod);
        }

        return new AccrualSnapshotMaterializationBatch(request.Id, sourceLines);
    }

    internal AccrualSnapshotMaterializationPlan? TryBuildPlan(
        AccrualSnapshotSourceResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        var targets = new AccrualSnapshotMaterializationTarget[SourceLines.Count];
        for (var index = 0; index < SourceLines.Count; index++)
        {
            var line = SourceLines[index];
            targets[index] = new AccrualSnapshotMaterializationTarget(
                line.LineNumber,
                line.LineId);
        }

        return AccrualSnapshotMaterializationPlanBuilder.TryBuild(
            ExpenseRequestId,
            targets,
            resolution);
    }

    private static InvalidOperationException InvariantViolation() => new(
        "Expense request lines violate the accrual snapshot materialization batch invariant.");
}
