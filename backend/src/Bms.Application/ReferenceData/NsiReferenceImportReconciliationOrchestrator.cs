using Bms.Application.Abstractions;

namespace Bms.Application.ReferenceData;

internal enum NsiReferenceImportReconciledExecutionState
{
    BlockedBeforeWrite = 1,
    AppliedAndReconciled = 2,
    AppliedWithMismatch = 3,
    AppliedVerificationUnavailable = 4,
}

internal sealed class NsiReferenceImportReconciledExecutionResult
{
    private NsiReferenceImportReconciledExecutionResult(
        NsiReferenceImportReconciledExecutionState state,
        NsiReferenceImportBatchEvidence evidence,
        NsiReferenceImportReconciliation? reconciliation,
        string? issueCode)
    {
        State = state;
        Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        Reconciliation = reconciliation;
        IssueCode = issueCode;
    }

    internal NsiReferenceImportReconciledExecutionState State { get; }

    internal NsiReferenceImportBatchEvidence Evidence { get; }

    internal NsiReferenceImportReconciliation? Reconciliation { get; }

    internal string? IssueCode { get; }

    internal static NsiReferenceImportReconciledExecutionResult Blocked(
        NsiReferenceImportBatchEvidence evidence,
        string issueCode)
    {
        if (string.IsNullOrWhiteSpace(issueCode) || !NsiReferenceImportIssueCodes.IsKnown(issueCode))
        {
            throw new ArgumentException("A known issue code is required.", nameof(issueCode));
        }

        return new NsiReferenceImportReconciledExecutionResult(
            NsiReferenceImportReconciledExecutionState.BlockedBeforeWrite,
            evidence,
            null,
            issueCode);
    }

    internal static NsiReferenceImportReconciledExecutionResult Applied(
        NsiReferenceImportReconciliation reconciliation) =>
        new(
            reconciliation.AllImportedItemsMatch
                ? NsiReferenceImportReconciledExecutionState.AppliedAndReconciled
                : NsiReferenceImportReconciledExecutionState.AppliedWithMismatch,
            reconciliation.Evidence,
            reconciliation,
            null);

    internal static NsiReferenceImportReconciledExecutionResult VerificationUnavailable(
        NsiReferenceImportBatchEvidence evidence) =>
        new(
            NsiReferenceImportReconciledExecutionState.AppliedVerificationUnavailable,
            evidence,
            null,
            null);
}

internal static class NsiReferenceImportReconciliationOrchestrator
{
    internal static async Task<NsiReferenceImportReconciledExecutionResult> ExecuteAsync(
        INsiReferenceImportSource source,
        INsiReferenceProjectionReader projectionReader,
        IBusinessTransactionRunner transactionRunner,
        Func<NsiReferenceImportPlan, NsiReferenceProjectionReadModel, CancellationToken, Task> apply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(projectionReader);
        ArgumentNullException.ThrowIfNull(transactionRunner);
        ArgumentNullException.ThrowIfNull(apply);
        cancellationToken.ThrowIfCancellationRequested();

        var batch = await source.ReadAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(batch);
        var evidence = NsiReferenceImportBatchEvidence.Capture(batch);

        NsiReferenceImportExecutionResult execution;
        try
        {
            execution = await NsiReferenceImportPlanOrchestrator.ExecuteAsync(
                new CapturedBatchSource(batch),
                projectionReader,
                transactionRunner,
                apply,
                cancellationToken);
        }
        catch (BusinessTransactionCommittedException exception)
            when (!ProcessFatalExceptionClassifier.IsFatal(exception))
        {
            // Commit is explicit, but the same runner/context is not safe for a
            // second read after cleanup failed.
            return NsiReferenceImportReconciledExecutionResult.VerificationUnavailable(evidence);
        }
        if (!execution.IsApplied)
        {
            return NsiReferenceImportReconciledExecutionResult.Blocked(
                evidence,
                execution.IssueCode!);
        }

        try
        {
            var committedProjection = await transactionRunner.ExecuteAsync(
                async transactionCancellationToken =>
                {
                    transactionCancellationToken.ThrowIfCancellationRequested();
                    var current = await projectionReader.ReadAsync(transactionCancellationToken);
                    transactionCancellationToken.ThrowIfCancellationRequested();
                    return current;
                },
                cancellationToken);
            var reconciliation = NsiReferenceImportReconciler.Reconcile(
                batch,
                committedProjection,
                evidence);
            return NsiReferenceImportReconciledExecutionResult.Applied(reconciliation);
        }
        catch (Exception exception)
            when (!ProcessFatalExceptionClassifier.IsFatal(exception))
        {
            // The write transaction has already committed. Returning an explicit
            // unavailable state is safer than surfacing cancellation/failure as if
            // no mutation had happened. No diagnostic payload crosses this boundary.
            return NsiReferenceImportReconciledExecutionResult.VerificationUnavailable(evidence);
        }
    }

    private sealed class CapturedBatchSource(NsiReferenceImportBatch batch) :
        INsiReferenceImportSource
    {
        public Task<NsiReferenceImportBatch> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(batch);
        }
    }
}
