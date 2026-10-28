using System.Collections.ObjectModel;
using System.Globalization;

namespace Bms.Application.ReferenceData;

internal sealed class NsiCounterpartyCodeVacate
{
    internal NsiCounterpartyCodeVacate(Guid id, string temporaryCode)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Counterparty id is required.", nameof(id));
        }

        Id = id;
        TemporaryCode = NsiImportText.Validate(
            temporaryCode,
            20,
            nameof(temporaryCode),
            "Temporary counterparty code");
    }

    internal Guid Id { get; }

    internal string TemporaryCode { get; }
}

internal sealed class NsiContractCodeVacate
{
    internal NsiContractCodeVacate(Guid id, string temporaryCode)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Contract id is required.", nameof(id));
        }

        Id = id;
        TemporaryCode = NsiImportText.Validate(
            temporaryCode,
            30,
            nameof(temporaryCode),
            "Temporary contract code");
    }

    internal Guid Id { get; }

    internal string TemporaryCode { get; }
}

internal sealed class NsiReferenceProjectionWritePlan
{
    internal NsiReferenceProjectionWritePlan(
        IEnumerable<NsiCounterpartyCodeVacate> counterpartyCodeVacates,
        IEnumerable<NsiCounterpartyImportAssignment> counterpartyWrites,
        IEnumerable<NsiContractCodeVacate> contractCodeVacates,
        IEnumerable<NsiContractImportAssignment> contractWrites)
    {
        ArgumentNullException.ThrowIfNull(counterpartyCodeVacates);
        ArgumentNullException.ThrowIfNull(counterpartyWrites);
        ArgumentNullException.ThrowIfNull(contractCodeVacates);
        ArgumentNullException.ThrowIfNull(contractWrites);
        var counterpartyVacateSnapshot = Snapshot(
            counterpartyCodeVacates,
            nameof(counterpartyCodeVacates));
        var counterpartyWriteSnapshot = Snapshot(
            counterpartyWrites,
            nameof(counterpartyWrites));
        var contractVacateSnapshot = Snapshot(
            contractCodeVacates,
            nameof(contractCodeVacates));
        var contractWriteSnapshot = Snapshot(contractWrites, nameof(contractWrites));
        ValidateCounterpartyPhases(counterpartyVacateSnapshot, counterpartyWriteSnapshot);
        ValidateContractPhases(contractVacateSnapshot, contractWriteSnapshot);
        CounterpartyCodeVacates = counterpartyVacateSnapshot;
        CounterpartyWrites = counterpartyWriteSnapshot;
        ContractCodeVacates = contractVacateSnapshot;
        ContractWrites = contractWriteSnapshot;
    }

    internal IReadOnlyList<NsiCounterpartyCodeVacate> CounterpartyCodeVacates { get; }

    internal IReadOnlyList<NsiCounterpartyImportAssignment> CounterpartyWrites { get; }

    internal IReadOnlyList<NsiContractCodeVacate> ContractCodeVacates { get; }

    internal IReadOnlyList<NsiContractImportAssignment> ContractWrites { get; }

    private static IReadOnlyList<T> Snapshot<T>(IEnumerable<T> values, string parameterName)
        where T : class
    {
        var snapshot = values.ToArray();
        if (snapshot.Any(value => value is null))
        {
            throw new ArgumentException("Write plan collections cannot contain null.", parameterName);
        }

        return new ReadOnlyCollection<T>(snapshot);
    }

    private static void ValidateCounterpartyPhases(
        IReadOnlyList<NsiCounterpartyCodeVacate> vacates,
        IReadOnlyList<NsiCounterpartyImportAssignment> writes)
    {
        if (!IsStrictlyOrdered(vacates.Select(item => item.Id)) ||
            !IsStrictlyOrdered(writes.Select(item => item.Item.Id)) ||
            writes.Any(item => item.Operation is not (
                NsiReferenceImportOperation.Insert or NsiReferenceImportOperation.Update)) ||
            vacates.Select(item => item.TemporaryCode).Distinct(StringComparer.Ordinal).Count() !=
                vacates.Count ||
            writes.Select(item => item.Item.Code).Distinct(StringComparer.Ordinal).Count() !=
                writes.Count ||
            vacates.Any(vacate => writes.All(write =>
                write.Operation is not NsiReferenceImportOperation.Update ||
                write.Item.Id != vacate.Id)) ||
            vacates.Any(vacate => writes.Any(write => string.Equals(
                write.Item.Code,
                vacate.TemporaryCode,
                StringComparison.Ordinal))))
        {
            throw new ArgumentException(
                "Counterparty write phases are inconsistent.",
                nameof(writes));
        }
    }

    private static void ValidateContractPhases(
        IReadOnlyList<NsiContractCodeVacate> vacates,
        IReadOnlyList<NsiContractImportAssignment> writes)
    {
        if (!IsStrictlyOrdered(vacates.Select(item => item.Id)) ||
            !IsStrictlyOrdered(writes.Select(item => item.Item.Id)) ||
            writes.Any(item => item.Operation is not (
                NsiReferenceImportOperation.Insert or NsiReferenceImportOperation.Update)) ||
            vacates.Select(item => item.TemporaryCode).Distinct(StringComparer.Ordinal).Count() !=
                vacates.Count ||
            writes.Select(item => item.Item.Code).Distinct(StringComparer.Ordinal).Count() !=
                writes.Count ||
            vacates.Any(vacate => writes.All(write =>
                write.Operation is not NsiReferenceImportOperation.Update ||
                write.Item.Id != vacate.Id)) ||
            vacates.Any(vacate => writes.Any(write => string.Equals(
                write.Item.Code,
                vacate.TemporaryCode,
                StringComparison.Ordinal))))
        {
            throw new ArgumentException(
                "Contract write phases are inconsistent.",
                nameof(writes));
        }
    }

    private static bool IsStrictlyOrdered(IEnumerable<Guid> identifiers)
    {
        var previous = Guid.Empty;
        var hasPrevious = false;
        foreach (var identifier in identifiers)
        {
            if (identifier == Guid.Empty || hasPrevious && previous.CompareTo(identifier) >= 0)
            {
                return false;
            }

            previous = identifier;
            hasPrevious = true;
        }

        return true;
    }
}

internal static class NsiReferenceProjectionWritePlanBuilder
{
    private const string InconsistentPlanMessage =
        "The NSI write plan is inconsistent with the current projection.";

    internal static NsiReferenceProjectionWritePlan Build(
        NsiReferenceImportPlan plan,
        NsiReferenceProjectionReadModel current)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(current);
        ValidateCanonicalPlan(plan, current);

        var currentCounterparties = current.Counterparties.ToDictionary(item => item.Id);
        var currentContracts = current.Contracts.ToDictionary(item => item.Id);
        var counterpartyWrites = plan.Counterparties
            .Where(assignment => assignment.Operation is not NsiReferenceImportOperation.Unchanged)
            .OrderBy(assignment => assignment.Item.Id)
            .ToArray();
        var contractWrites = plan.Contracts
            .Where(assignment => assignment.Operation is not NsiReferenceImportOperation.Unchanged)
            .OrderBy(assignment => assignment.Item.Id)
            .ToArray();

        var counterpartyCodeVacates = BuildCounterpartyCodeVacates(
            counterpartyWrites,
            currentCounterparties,
            current.Counterparties.Select(item => item.Code)
                .Concat(plan.Counterparties.Select(assignment => assignment.Item.Code)));
        var contractCodeVacates = BuildContractCodeVacates(
            contractWrites,
            currentContracts,
            current.Contracts.Select(item => item.Code)
                .Concat(plan.Contracts.Select(assignment => assignment.Item.Code)));

        return new NsiReferenceProjectionWritePlan(
            counterpartyCodeVacates,
            counterpartyWrites,
            contractCodeVacates,
            contractWrites);
    }

    private static void ValidateCanonicalPlan(
        NsiReferenceImportPlan plan,
        NsiReferenceProjectionReadModel current)
    {
        if (plan.Counterparties.Any(assignment => assignment is null) ||
            plan.Contracts.Any(assignment => assignment is null))
        {
            throw new InvalidOperationException(InconsistentPlanMessage);
        }

        NsiReferenceImportBatch batch;
        try
        {
            batch = NsiReferenceImportBatch.Create(
                plan.Counterparties.Select(assignment => assignment.Item),
                plan.Contracts.Select(assignment => assignment.Item));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(InconsistentPlanMessage, exception);
        }

        var canonical = NsiReferenceImportPlanBuilder.Resolve(
            batch,
            NsiReferenceProjectionSnapshot.Create(current));
        if (!canonical.IsReady || canonical.Plan is null ||
            !Matches(plan.Counterparties, canonical.Plan.Counterparties) ||
            !Matches(plan.Contracts, canonical.Plan.Contracts))
        {
            throw new InvalidOperationException(InconsistentPlanMessage);
        }
    }

    private static bool Matches(
        IReadOnlyList<NsiCounterpartyImportAssignment> candidate,
        IReadOnlyList<NsiCounterpartyImportAssignment> canonical)
    {
        if (candidate.Count != canonical.Count)
        {
            return false;
        }

        for (var index = 0; index < candidate.Count; index++)
        {
            if (candidate[index].Operation != canonical[index].Operation ||
                !ReferenceEquals(candidate[index].Item, canonical[index].Item))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Matches(
        IReadOnlyList<NsiContractImportAssignment> candidate,
        IReadOnlyList<NsiContractImportAssignment> canonical)
    {
        if (candidate.Count != canonical.Count)
        {
            return false;
        }

        for (var index = 0; index < candidate.Count; index++)
        {
            if (candidate[index].Operation != canonical[index].Operation ||
                !ReferenceEquals(candidate[index].Item, canonical[index].Item))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<NsiCounterpartyCodeVacate> BuildCounterpartyCodeVacates(
        IEnumerable<NsiCounterpartyImportAssignment> writes,
        IReadOnlyDictionary<Guid, NsiCounterpartyImportItem> current,
        IEnumerable<string> reservedCodes)
    {
        var allocator = new TemporaryCodeAllocator(reservedCodes);
        return writes
            .Where(assignment =>
                assignment.Operation is NsiReferenceImportOperation.Update &&
                !string.Equals(
                    current[assignment.Item.Id].Code,
                    assignment.Item.Code,
                    StringComparison.Ordinal))
            .Select(assignment => new NsiCounterpartyCodeVacate(
                assignment.Item.Id,
                allocator.Next()))
            .ToArray();
    }

    private static IReadOnlyList<NsiContractCodeVacate> BuildContractCodeVacates(
        IEnumerable<NsiContractImportAssignment> writes,
        IReadOnlyDictionary<Guid, NsiContractImportItem> current,
        IEnumerable<string> reservedCodes)
    {
        var allocator = new TemporaryCodeAllocator(reservedCodes);
        return writes
            .Where(assignment =>
                assignment.Operation is NsiReferenceImportOperation.Update &&
                !string.Equals(
                    current[assignment.Item.Id].Code,
                    assignment.Item.Code,
                    StringComparison.Ordinal))
            .Select(assignment => new NsiContractCodeVacate(
                assignment.Item.Id,
                allocator.Next()))
            .ToArray();
    }

    private sealed class TemporaryCodeAllocator
    {
        private readonly HashSet<string> reservedCodes;
        private ulong nextSequence;

        internal TemporaryCodeAllocator(IEnumerable<string> reservedCodes)
        {
            ArgumentNullException.ThrowIfNull(reservedCodes);
            this.reservedCodes = new HashSet<string>(reservedCodes, StringComparer.Ordinal);
        }

        internal string Next()
        {
            while (true)
            {
                var candidate = string.Create(
                    CultureInfo.InvariantCulture,
                    $"~{nextSequence:X16}");
                if (reservedCodes.Add(candidate))
                {
                    if (nextSequence != ulong.MaxValue)
                    {
                        nextSequence++;
                    }
                    return candidate;
                }

                if (nextSequence == ulong.MaxValue)
                {
                    throw new InvalidOperationException(
                        "The temporary NSI code namespace is exhausted.");
                }

                nextSequence++;
            }
        }
    }
}
