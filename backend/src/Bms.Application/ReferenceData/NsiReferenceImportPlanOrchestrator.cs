using Bms.Application.Abstractions;

namespace Bms.Application.ReferenceData;

internal sealed class NsiReferenceImportExecutionResult
{
    private NsiReferenceImportExecutionResult(bool isApplied, string? issueCode)
    {
        IsApplied = isApplied;
        IssueCode = issueCode;
    }

    internal bool IsApplied { get; }

    internal string? IssueCode { get; }

    internal static NsiReferenceImportExecutionResult Applied() => new(true, null);

    internal static NsiReferenceImportExecutionResult Blocked(string issueCode)
    {
        if (string.IsNullOrWhiteSpace(issueCode) || !NsiReferenceImportIssueCodes.IsKnown(issueCode))
        {
            throw new ArgumentException("A known issue code is required.", nameof(issueCode));
        }

        return new NsiReferenceImportExecutionResult(false, issueCode);
    }
}

internal static class NsiReferenceImportPlanOrchestrator
{
    internal static async Task<NsiReferenceImportExecutionResult> ExecuteAsync(
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

        return await transactionRunner.ExecuteAsync(
            async transactionCancellationToken =>
            {
                transactionCancellationToken.ThrowIfCancellationRequested();
                var current = await projectionReader.ReadAsync(transactionCancellationToken);
                transactionCancellationToken.ThrowIfCancellationRequested();
                var resolution = NsiReferenceImportPlanBuilder.Resolve(
                    batch,
                    NsiReferenceProjectionSnapshot.Create(current));
                if (!resolution.IsReady)
                {
                    return NsiReferenceImportExecutionResult.Blocked(resolution.IssueCode!);
                }

                await apply(resolution.Plan!, current, transactionCancellationToken);
                transactionCancellationToken.ThrowIfCancellationRequested();
                return NsiReferenceImportExecutionResult.Applied();
            },
            cancellationToken);
    }
}
