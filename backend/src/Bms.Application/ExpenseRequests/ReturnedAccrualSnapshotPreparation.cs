using Bms.Application.Abstractions;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// <summary>
/// A new submission after RETURN is the only path allowed to replace a captured
/// accrual pair. The original first-materialization writer remains immutable.
/// The workflow service owns the audit and the final Serializable commit.
/// </summary>
internal static class ReturnedAccrualSnapshotPreparation
{
    internal static async Task<AccrualSnapshotPreparation> PrepareAsync(
        ExpenseRequest request, Guid expectedVersion, IAccrualSnapshotMaterializationRuntime runtime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(runtime);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireCleanReturnedAttempt(request, expectedVersion, runtime);
            var plan = await AccrualSnapshotMaterializationPlanOrchestrator.BuildAsync(request, runtime, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            RequireCleanReturnedAttempt(request, expectedVersion, runtime);
            if (plan is null) return new(false, expectedVersion);
            var stored = AccrualSnapshotStoredPairClassifier.Classify(request, expectedVersion, plan);
            if (stored == AccrualSnapshotStoredPairState.AllPresentAndMatchPlan)
                return new(true, expectedVersion);
            if (stored is not (AccrualSnapshotStoredPairState.AllAbsent
                or AccrualSnapshotStoredPairState.AllPresentWithDifference
                or AccrualSnapshotStoredPairState.MixedAbsentAndPresent))
                throw new AccrualSnapshotMaterializationWriteUnavailableException();
            request.RefreshReturnedAccrualSnapshot(expectedVersion, plan.Assignments.Select(item =>
                new ExpenseRequestLineAccrualSnapshotAssignment(item.LineId, item.FormsManagerialAccrual, item.AccrualExpenseItemId)).ToArray());
            if (request.ConcurrencyStamp == expectedVersion || request.Status != ExpenseRequestStatus.Returned
                || !runtime.HasSerializableTransaction || !runtime.HasExactTrackedAggregate(request))
                throw new AccrualSnapshotMaterializationWriteUnavailableException();
            cancellationToken.ThrowIfCancellationRequested();
            return new(true, request.ConcurrencyStamp);
        }
        catch (OperationCanceledException) { throw; }
        catch (AccrualSnapshotMaterializationWriteUnavailableException) { throw; }
        catch (Exception exception)
        {
            throw new AccrualSnapshotMaterializationWriteUnavailableException(exception);
        }
    }

    private static void RequireCleanReturnedAttempt(ExpenseRequest request, Guid version,
        IAccrualSnapshotMaterializationRuntime runtime)
    {
        if (version == Guid.Empty || request.ConcurrencyStamp != version || request.Status != ExpenseRequestStatus.Returned
            || !runtime.HasSerializableTransaction || runtime.HasPendingChanges || !runtime.HasExactTrackedAggregate(request))
            throw new AccrualSnapshotMaterializationWriteUnavailableException();
    }
}
