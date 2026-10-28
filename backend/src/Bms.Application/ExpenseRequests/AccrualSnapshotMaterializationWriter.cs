using Bms.Application.Abstractions;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

internal enum AccrualSnapshotMaterializationWriteOutcome
{
    Materialized = 1,
    AlreadyMaterialized = 2,
    BlockedBySourceIssues = 3
}

internal sealed record AccrualSnapshotMaterializationWriteResult(
    AccrualSnapshotMaterializationWriteOutcome Outcome,
    Guid RequestVersion,
    int AffectedEntries);

internal sealed class AccrualSnapshotMaterializationWriteUnavailableException : Exception
{
    private const string SafeMessage =
        "The accrual snapshot could not be materialized consistently.";

    internal AccrualSnapshotMaterializationWriteUnavailableException()
        : base(SafeMessage)
    {
    }

    internal AccrualSnapshotMaterializationWriteUnavailableException(Exception innerException)
        : base(
            SafeMessage,
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}

internal interface IAccrualSnapshotMaterializationRuntime :
    IAccrualSnapshotSourceResolver
{
    bool HasSerializableTransaction { get; }

    bool HasPendingChanges { get; }

    bool HasExactTrackedAggregate(ExpenseRequest request);

    Task<int> SaveAsync(
        ExpenseRequest request,
        Guid expectedOriginalVersion,
        CancellationToken cancellationToken);
}

internal static class AccrualSnapshotMaterializationWriter
{
    internal static async Task<AccrualSnapshotMaterializationWriteResult> ApplyAsync(
        ExpenseRequest request,
        Guid expectedVersion,
        IAccrualSnapshotMaterializationRuntime runtime,
        CancellationToken cancellationToken)
    {
        var prepared = await ApplyToTrackedAggregateAsync(
            request,
            expectedVersion,
            runtime,
            cancellationToken);
        if (prepared.Outcome != AccrualSnapshotMaterializationWriteOutcome.Materialized)
        {
            return prepared;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!runtime.HasSerializableTransaction ||
                !runtime.HasExactTrackedAggregate(request))
            {
                throw new AccrualSnapshotMaterializationWriteUnavailableException();
            }

            var affectedEntries = await runtime.SaveAsync(
                request,
                expectedVersion,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var expectedAffectedEntries = checked(request.Lines.Count + 1);
            if (affectedEntries != expectedAffectedEntries ||
                !runtime.HasSerializableTransaction ||
                runtime.HasPendingChanges ||
                !runtime.HasExactTrackedAggregate(request))
            {
                throw new AccrualSnapshotMaterializationWriteUnavailableException();
            }

            return prepared with { AffectedEntries = affectedEntries };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AccrualSnapshotMaterializationWriteUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AccrualSnapshotMaterializationWriteUnavailableException(exception);
        }
    }

    /// <summary>
    /// Applies a verified accrual snapshot to a clean tracked aggregate, but
    /// deliberately does not save it. A caller may then compose the snapshot
    /// with one workflow transition and persist both in the same Serializable
    /// transaction.
    /// </summary>
    internal static async Task<AccrualSnapshotMaterializationWriteResult>
        ApplyToTrackedAggregateAsync(
            ExpenseRequest request,
            Guid expectedVersion,
            IAccrualSnapshotMaterializationRuntime runtime,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(runtime);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(request, expectedVersion, runtime);

            var plan = await AccrualSnapshotMaterializationPlanOrchestrator.BuildAsync(
                request,
                runtime,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(request, expectedVersion, runtime);
            if (plan is null)
            {
                return new AccrualSnapshotMaterializationWriteResult(
                    AccrualSnapshotMaterializationWriteOutcome.BlockedBySourceIssues,
                    expectedVersion,
                    0);
            }

            var storedState = AccrualSnapshotStoredPairClassifier.Classify(
                request,
                expectedVersion,
                plan);
            if (storedState == AccrualSnapshotStoredPairState.AllPresentAndMatchPlan)
            {
                return new AccrualSnapshotMaterializationWriteResult(
                    AccrualSnapshotMaterializationWriteOutcome.AlreadyMaterialized,
                    expectedVersion,
                    0);
            }

            if (storedState != AccrualSnapshotStoredPairState.AllAbsent)
            {
                throw new AccrualSnapshotMaterializationWriteUnavailableException();
            }

            var assignments = plan.Assignments
                .Select(assignment => new ExpenseRequestLineAccrualSnapshotAssignment(
                    assignment.LineId,
                    assignment.FormsManagerialAccrual,
                    assignment.AccrualExpenseItemId))
                .ToArray();
            request.ApplyInitialAccrualSnapshot(expectedVersion, assignments);
            if (request.ConcurrencyStamp == expectedVersion)
            {
                throw new AccrualSnapshotMaterializationWriteUnavailableException();
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!runtime.HasSerializableTransaction ||
                !runtime.HasExactTrackedAggregate(request))
            {
                throw new AccrualSnapshotMaterializationWriteUnavailableException();
            }

            return new AccrualSnapshotMaterializationWriteResult(
                AccrualSnapshotMaterializationWriteOutcome.Materialized,
                request.ConcurrencyStamp,
                0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AccrualSnapshotMaterializationWriteUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AccrualSnapshotMaterializationWriteUnavailableException(exception);
        }
    }

    private static void EnsureAttemptReady(
        ExpenseRequest request,
        Guid expectedVersion,
        IAccrualSnapshotMaterializationRuntime runtime)
    {
        if (expectedVersion == Guid.Empty ||
            request.ConcurrencyStamp != expectedVersion ||
            !runtime.HasSerializableTransaction ||
            runtime.HasPendingChanges ||
            !runtime.HasExactTrackedAggregate(request))
        {
            throw new AccrualSnapshotMaterializationWriteUnavailableException();
        }
    }
}
