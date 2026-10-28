namespace Bms.Application.ReferenceData;

internal sealed class NsiReferenceProjectionWriteResult
{
    internal NsiReferenceProjectionWriteResult(
        int counterpartyCodeVacates,
        int counterpartyWrites,
        int contractCodeVacates,
        int contractWrites)
    {
        EnsureNonNegative(counterpartyCodeVacates, nameof(counterpartyCodeVacates));
        EnsureNonNegative(counterpartyWrites, nameof(counterpartyWrites));
        EnsureNonNegative(contractCodeVacates, nameof(contractCodeVacates));
        EnsureNonNegative(contractWrites, nameof(contractWrites));

        CounterpartyCodeVacates = counterpartyCodeVacates;
        CounterpartyWrites = counterpartyWrites;
        ContractCodeVacates = contractCodeVacates;
        ContractWrites = contractWrites;
    }

    internal int CounterpartyCodeVacates { get; }

    internal int CounterpartyWrites { get; }

    internal int ContractCodeVacates { get; }

    internal int ContractWrites { get; }

    internal long TotalAffectedRows =>
        (long)CounterpartyCodeVacates + CounterpartyWrites +
        ContractCodeVacates + ContractWrites;

    private static void EnsureNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Write result counts cannot be negative.");
        }
    }
}

internal sealed class NsiReferenceProjectionWriteUnavailableException : Exception
{
    private const string SafeMessage =
        "The NSI projection write could not be completed consistently.";

    internal NsiReferenceProjectionWriteUnavailableException()
        : base(SafeMessage)
    {
    }

    internal NsiReferenceProjectionWriteUnavailableException(Exception innerException)
        : base(
            SafeMessage,
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}

internal interface INsiReferenceProjectionWriteSink
{
    bool HasSerializableTransaction { get; }

    bool HasPendingChanges { get; }

    bool HasTrackedProjectionEntities { get; }

    Task<int> VacateCounterpartyCodesAsync(
        IReadOnlyList<NsiCounterpartyCodeVacate> vacates,
        NsiReferenceProjectionReadModel current,
        CancellationToken cancellationToken);

    Task<int> WriteCounterpartiesAsync(
        IReadOnlyList<NsiCounterpartyImportAssignment> writes,
        IReadOnlyList<NsiCounterpartyCodeVacate> vacates,
        NsiReferenceProjectionReadModel current,
        CancellationToken cancellationToken);

    Task<int> VacateContractCodesAsync(
        IReadOnlyList<NsiContractCodeVacate> vacates,
        NsiReferenceProjectionReadModel current,
        CancellationToken cancellationToken);

    Task<int> WriteContractsAsync(
        IReadOnlyList<NsiContractImportAssignment> writes,
        IReadOnlyList<NsiContractCodeVacate> vacates,
        NsiReferenceProjectionReadModel current,
        CancellationToken cancellationToken);
}

internal static class NsiReferenceProjectionWriter
{
    internal static async Task<NsiReferenceProjectionWriteResult> ApplyAsync(
        NsiReferenceImportPlan plan,
        NsiReferenceProjectionReadModel current,
        INsiReferenceProjectionWriteSink sink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(sink);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sink.HasSerializableTransaction ||
                sink.HasPendingChanges ||
                sink.HasTrackedProjectionEntities)
            {
                throw new NsiReferenceProjectionWriteUnavailableException();
            }

            var writePlan = NsiReferenceProjectionWritePlanBuilder.Build(plan, current);
            var counterpartyCodeVacates = await ApplyPhaseAsync(
                writePlan.CounterpartyCodeVacates.Count,
                token => sink.VacateCounterpartyCodesAsync(
                    writePlan.CounterpartyCodeVacates,
                    current,
                    token),
                cancellationToken);
            var counterpartyWrites = await ApplyPhaseAsync(
                writePlan.CounterpartyWrites.Count,
                token => sink.WriteCounterpartiesAsync(
                    writePlan.CounterpartyWrites,
                    writePlan.CounterpartyCodeVacates,
                    current,
                    token),
                cancellationToken);
            var contractCodeVacates = await ApplyPhaseAsync(
                writePlan.ContractCodeVacates.Count,
                token => sink.VacateContractCodesAsync(
                    writePlan.ContractCodeVacates,
                    current,
                    token),
                cancellationToken);
            var contractWrites = await ApplyPhaseAsync(
                writePlan.ContractWrites.Count,
                token => sink.WriteContractsAsync(
                    writePlan.ContractWrites,
                    writePlan.ContractCodeVacates,
                    current,
                    token),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (!sink.HasSerializableTransaction || sink.HasPendingChanges)
            {
                throw new NsiReferenceProjectionWriteUnavailableException();
            }

            return new NsiReferenceProjectionWriteResult(
                counterpartyCodeVacates,
                counterpartyWrites,
                contractCodeVacates,
                contractWrites);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NsiReferenceProjectionWriteUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new NsiReferenceProjectionWriteUnavailableException(exception);
        }
    }

    private static async Task<int> ApplyPhaseAsync(
        int expectedAffectedRows,
        Func<CancellationToken, Task<int>> apply,
        CancellationToken cancellationToken)
    {
        if (expectedAffectedRows == 0)
        {
            return 0;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var affectedRows = await apply(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (affectedRows != expectedAffectedRows)
        {
            throw new NsiReferenceProjectionWriteUnavailableException();
        }

        return affectedRows;
    }
}
