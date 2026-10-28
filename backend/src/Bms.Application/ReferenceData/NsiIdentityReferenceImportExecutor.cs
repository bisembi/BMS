using Bms.Application.Abstractions;

namespace Bms.Application.ReferenceData;

internal interface INsiIdentityReferenceProjectionReader
{
    Task<NsiIdentityReferenceProjectionSnapshot> ReadAsync(
        CancellationToken cancellationToken);
}

internal sealed class NsiIdentityReferenceImportExecutor(
    INsiIdentityReferenceProjectionReader projectionReader,
    IBusinessTransactionRunner transactionRunner,
    Func<INsiIdentityReferenceProjectionWriteSink> writeSinkFactory) :
    INsiIdentityReferenceImportExecutor
{
    private readonly INsiIdentityReferenceProjectionReader projectionReader =
        projectionReader ?? throw new ArgumentNullException(nameof(projectionReader));
    private readonly IBusinessTransactionRunner transactionRunner =
        transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
    private readonly Func<INsiIdentityReferenceProjectionWriteSink> writeSinkFactory =
        writeSinkFactory ?? throw new ArgumentNullException(nameof(writeSinkFactory));

    public async Task<NsiIdentityReferenceImportResult> ExecuteAsync(
        NsiIdentityReferenceImportBatch batch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await transactionRunner.ExecuteOwnerAssignmentMutationAsync(
                async transactionCancellationToken =>
                {
                    transactionCancellationToken.ThrowIfCancellationRequested();
                    var current = await projectionReader.ReadAsync(
                        transactionCancellationToken);
                    if (current is null)
                    {
                        throw new NsiIdentityReferenceImportUnavailableException();
                    }

                    transactionCancellationToken.ThrowIfCancellationRequested();
                    var resolution = NsiIdentityReferenceImportPlanBuilder.Resolve(
                        batch,
                        current);
                    if (!resolution.IsReady)
                    {
                        var issueCode = resolution.IssueCode;
                        if (issueCode is null)
                        {
                            throw new NsiIdentityReferenceImportUnavailableException();
                        }
                        return NsiIdentityReferenceImportResult.Blocked(
                            issueCode);
                    }

                    var plan = resolution.Plan;
                    if (plan is null)
                    {
                        throw new NsiIdentityReferenceImportUnavailableException();
                    }

                    var sink = writeSinkFactory();
                    if (sink is null)
                    {
                        throw new NsiIdentityReferenceImportUnavailableException();
                    }

                    var writeResult =
                        await NsiIdentityReferenceProjectionWriter.ApplyAsync(
                            plan,
                            current,
                            sink,
                            transactionCancellationToken);
                    transactionCancellationToken.ThrowIfCancellationRequested();
                    return NsiIdentityReferenceImportResult.Applied(
                        writeResult.AffectedRows);
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BusinessTransactionCommittedException exception)
            when (!ProcessFatalExceptionClassifier.IsFatal(exception))
        {
            throw new NsiIdentityReferenceImportCommittedException();
        }
        catch (Exception exception)
            when (!ProcessFatalExceptionClassifier.IsFatal(exception))
        {
            throw new NsiIdentityReferenceImportUnavailableException();
        }
    }
}
