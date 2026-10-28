using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

internal enum AccrualSnapshotStoredPairState
{
    AllAbsent = 1,
    AllPresentAndMatchPlan = 2,
    AllPresentWithDifference = 3,
    MixedAbsentAndPresent = 4,
    InvalidStoredPair = 5
}

internal static class AccrualSnapshotStoredPairClassifier
{
    internal static AccrualSnapshotStoredPairState Classify(
        ExpenseRequest request,
        Guid expectedRequestVersion,
        AccrualSnapshotMaterializationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);
        if (expectedRequestVersion == Guid.Empty ||
            request.ConcurrencyStamp != expectedRequestVersion)
        {
            throw InvariantViolation();
        }

        var batch = AccrualSnapshotMaterializationBatch.Create(request);

        ExpenseRequestLine[] lines;
        try
        {
            lines = request.Lines.ToArray();
        }
        catch (NullReferenceException)
        {
            throw InvariantViolation();
        }

        if (lines.Length != batch.SourceLines.Count)
        {
            throw InvariantViolation();
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var captured = batch.SourceLines[index];
            if (line is null ||
                captured.LineNumber != checked(index + 1) ||
                line.Id != captured.LineId ||
                line.CashFlowItemId != captured.CashFlowItemId ||
                line.AccrualPeriod != captured.AccrualPeriod)
            {
                throw InvariantViolation();
            }
        }

        var assignments = plan.Assignments?.ToArray() ?? throw InvariantViolation();
        if (plan.ExpenseRequestId != batch.ExpenseRequestId ||
            assignments.Length != lines.Length)
        {
            throw InvariantViolation();
        }

        for (var index = 0; index < assignments.Length; index++)
        {
            var assignment = assignments[index];
            if (assignment is null ||
                assignment.LineId != batch.SourceLines[index].LineId ||
                (!assignment.FormsManagerialAccrual &&
                 assignment.AccrualExpenseItemId is not null) ||
                (assignment.FormsManagerialAccrual &&
                 (assignment.AccrualExpenseItemId is null ||
                  assignment.AccrualExpenseItemId == Guid.Empty)))
            {
                throw InvariantViolation();
            }
        }

        var absentCount = 0;
        var presentCount = 0;
        var hasInvalidStoredPair = false;
        var allPresentPairsMatchPlan = true;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var storedFormsAccrual = line.AccrualSnapshotFormsManagerialAccrual;
            var storedExpenseItemId = line.AccrualSnapshotExpenseItemId;
            var assignment = assignments[index];

            if (storedFormsAccrual is null && storedExpenseItemId is null)
            {
                absentCount++;
                continue;
            }

            if ((storedFormsAccrual is false && storedExpenseItemId is null) ||
                (storedFormsAccrual is true &&
                 storedExpenseItemId is { } presentExpenseItemId &&
                 presentExpenseItemId != Guid.Empty))
            {
                presentCount++;
                allPresentPairsMatchPlan &=
                    storedFormsAccrual.Value == assignment.FormsManagerialAccrual &&
                    storedExpenseItemId == assignment.AccrualExpenseItemId;
                continue;
            }

            hasInvalidStoredPair = true;
        }

        var result = hasInvalidStoredPair
            ? AccrualSnapshotStoredPairState.InvalidStoredPair
            : absentCount == lines.Length
                ? AccrualSnapshotStoredPairState.AllAbsent
                : absentCount > 0 && presentCount > 0
                    ? AccrualSnapshotStoredPairState.MixedAbsentAndPresent
                    : allPresentPairsMatchPlan
                        ? AccrualSnapshotStoredPairState.AllPresentAndMatchPlan
                        : AccrualSnapshotStoredPairState.AllPresentWithDifference;

        if (request.ConcurrencyStamp != expectedRequestVersion)
        {
            throw InvariantViolation();
        }

        return result;
    }

    private static InvalidOperationException InvariantViolation() => new(
        "Stored accrual snapshot classification inputs violate the internal invariant.");
}
