using Bms.Domain.Common;
using Bms.Domain.Overpayments;
using Bms.Domain.Payments;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Dormant inner stage that applies one already-authoritative real source to
/// active deficits. Source intake, leftover persistence, idempotency, audit,
/// outbox and the transaction remain owned by the outer orchestrator.
/// </summary>
internal static class TreasuryDeficitSettlementWriter
{
    internal static async Task<TreasuryDeficitSettlementResult> SettleAsync(
        TreasuryDeficitSettlementCommand command,
        DateTimeOffset appliedAtUtc,
        ITreasuryDeficitSettlementRuntime runtime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(runtime);

        try
        {
            ValidateCommand(command);
            ConfirmedPaymentEventContract.ValidateProcessingTime(appliedAtUtc);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);

            var kind = command.Kind.ToString();
            foreach (var resource in BusinessManagedLockResources.Order([
                         BusinessManagedLockResources.TreasuryCommand(command.CommandId),
                         BusinessManagedLockResources.TreasuryScope(
                             command.OrganizationId,
                             command.CounterpartyId,
                             command.CurrencyId),
                         BusinessManagedLockResources.TreasuryDeficitSettlementSource(
                             kind,
                             command.SourceReference)
                     ]))
            {
                await runtime.AcquireExclusiveTransactionLockAsync(
                    resource,
                    cancellationToken);
            }

            await EnsureCommandIsFreshAsync(command, kind, runtime, cancellationToken);
            var initialDeficits = await LoadActiveDeficitsAsync(
                command,
                appliedAtUtc,
                runtime,
                cancellationToken);
            var stateResources = initialDeficits
                .Select(deficit => BusinessManagedLockResources.TreasurySource(
                    deficit.SourceOverpaymentId))
                .Concat(initialDeficits.Select(deficit =>
                    BusinessManagedLockResources.TreasuryDeficit(deficit.DeficitId)))
                .ToArray();
            if (stateResources.Length > 0)
            {
                foreach (var resource in BusinessManagedLockResources.Order(stateResources))
                {
                    await runtime.AcquireExclusiveTransactionLockAsync(
                        resource,
                        cancellationToken);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);
            await EnsureCommandIsFreshAsync(command, kind, runtime, cancellationToken);
            var deficits = await LoadActiveDeficitsAsync(
                command,
                appliedAtUtc,
                runtime,
                cancellationToken);
            if (!HaveSameDeficits(initialDeficits, deficits))
            {
                throw new TreasuryDeficitSettlementUnavailableException();
            }

            var plan = OverpaymentDeficitSettlementPlanner.Plan(
                new OverpaymentScope(
                    command.OrganizationId,
                    command.CounterpartyId,
                    command.CurrencyCode),
                command.Kind,
                command.SourceAmount,
                command.SourceAmountKzt,
                deficits.Select(ToSnapshot));

            var deficitsById = deficits.ToDictionary(deficit => deficit.DeficitId);
            var settlements = new List<StoredOverpaymentDeficitSettlement>();
            var affectedEntries = 0;
            foreach (var allocation in plan.Allocations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureAttemptReady(runtime);
                var expected = deficitsById[allocation.DeficitId];
                var affected = await runtime.UpdateDeficitAsync(
                    expected,
                    allocation.AmountAfter,
                    allocation.AmountKztAfter,
                    allocation.IsClosed ? appliedAtUtc : null,
                    cancellationToken);
                if (affected != 1 || runtime.HasPendingChanges)
                {
                    throw new TreasuryDeficitSettlementUnavailableException();
                }
                affectedEntries = AddAffectedEntries(affectedEntries, affected);

                var settlement = new StoredOverpaymentDeficitSettlement(
                    allocation.DeficitId,
                    command.CommandId,
                    kind,
                    command.SourceReference,
                    command.SourceExpenseRequestId,
                    allocation.AppliedAmount,
                    allocation.AppliedAmountKzt,
                    appliedAtUtc,
                    command.UserId);
                affected = await runtime.InsertSettlementAsync(
                    settlement,
                    cancellationToken);
                if (affected != 1 || runtime.HasPendingChanges)
                {
                    throw new TreasuryDeficitSettlementUnavailableException();
                }
                affectedEntries = AddAffectedEntries(affectedEntries, affected);
                settlements.Add(settlement);
            }

            await VerifyPersistedAsync(
                command,
                kind,
                appliedAtUtc,
                plan,
                deficitsById,
                settlements,
                runtime,
                cancellationToken);
            return new TreasuryDeficitSettlementResult(
                plan.SourceAmount,
                plan.SourceAmountKzt,
                plan.AppliedAmount,
                plan.AppliedAmountKzt,
                plan.RemainingSourceAmount,
                plan.RemainingSourceAmountKzt,
                plan.HasUnsettledDeficit,
                Array.AsReadOnly(settlements.ToArray()),
                affectedEntries);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TreasuryDeficitSettlementUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TreasuryDeficitSettlementUnavailableException(exception);
        }
    }

    private static async Task EnsureCommandIsFreshAsync(
        TreasuryDeficitSettlementCommand command,
        string kind,
        ITreasuryDeficitSettlementRuntime runtime,
        CancellationToken cancellationToken)
    {
        if (!await runtime.UserExistsAsync(command.UserId, cancellationToken)
            || command.SourceExpenseRequestId is Guid sourceRequestId
                && !await runtime.ExpenseRequestMatchesScopeAsync(
                    sourceRequestId,
                    command.OrganizationId,
                    command.CounterpartyId,
                    command.CurrencyId,
                    cancellationToken)
            || await runtime.SettlementCommandExistsAsync(
                command.CommandId,
                cancellationToken)
            || (await runtime.FindBySourceAsync(
                kind,
                command.SourceReference,
                cancellationToken)).Count != 0)
        {
            throw new TreasuryDeficitSettlementUnavailableException();
        }
    }

    private static async Task<IReadOnlyList<StoredOverpaymentDeficit>>
        LoadActiveDeficitsAsync(
            TreasuryDeficitSettlementCommand command,
            DateTimeOffset appliedAtUtc,
            ITreasuryDeficitSettlementRuntime runtime,
            CancellationToken cancellationToken)
    {
        var deficits = await runtime.FindActiveDeficitsAsync(
            command.OrganizationId,
            command.CounterpartyId,
            command.CurrencyId,
            cancellationToken);
        ValidateDeficits(command, appliedAtUtc, deficits);
        var sourceIds = deficits
            .Select(deficit => deficit.SourceOverpaymentId)
            .Distinct()
            .ToArray();
        var matchingSourceIds = await runtime.FindMatchingOverpaymentSourceIdsAsync(
            sourceIds,
            command.OrganizationId,
            command.CounterpartyId,
            command.CurrencyId,
            cancellationToken);
        if (!sourceIds.Order().SequenceEqual(matchingSourceIds.Distinct().Order()))
        {
            throw new TreasuryDeficitSettlementUnavailableException();
        }
        return deficits;
    }

    private static async Task VerifyPersistedAsync(
        TreasuryDeficitSettlementCommand command,
        string kind,
        DateTimeOffset appliedAtUtc,
        OverpaymentDeficitSettlementPlan plan,
        IReadOnlyDictionary<Guid, StoredOverpaymentDeficit> originalDeficits,
        IReadOnlyList<StoredOverpaymentDeficitSettlement> settlements,
        ITreasuryDeficitSettlementRuntime runtime,
        CancellationToken cancellationToken)
    {
        foreach (var allocation in plan.Allocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var persisted = await runtime.FindDeficitByIdAsync(
                allocation.DeficitId,
                cancellationToken);
            var expected = originalDeficits[allocation.DeficitId] with
            {
                CurrentAmount = allocation.AmountAfter,
                CurrentAmountKzt = allocation.AmountKztAfter,
                SettledAtUtc = allocation.IsClosed ? appliedAtUtc : null
            };
            if (persisted != expected)
            {
                throw new TreasuryDeficitSettlementUnavailableException();
            }
        }

        var byCommand = await runtime.FindByCommandAsync(
            command.CommandId,
            cancellationToken);
        var bySource = await runtime.FindBySourceAsync(
            kind,
            command.SourceReference,
            cancellationToken);
        if (!SameSettlements(byCommand, settlements)
            || !SameSettlements(bySource, settlements)
            || !runtime.HasSerializableTransaction
            || runtime.HasPendingChanges)
        {
            throw new TreasuryDeficitSettlementUnavailableException();
        }
    }

    private static bool SameSettlements(
        IEnumerable<StoredOverpaymentDeficitSettlement> actual,
        IEnumerable<StoredOverpaymentDeficitSettlement> expected) =>
        actual.OrderBy(settlement => settlement.DeficitId)
            .SequenceEqual(expected.OrderBy(settlement => settlement.DeficitId));

    private static bool HaveSameDeficits(
        IEnumerable<StoredOverpaymentDeficit> left,
        IEnumerable<StoredOverpaymentDeficit> right) =>
        left.OrderBy(deficit => deficit.DeficitId)
            .SequenceEqual(right.OrderBy(deficit => deficit.DeficitId));

    private static OverpaymentDeficitSnapshot ToSnapshot(StoredOverpaymentDeficit deficit) =>
        new(
            deficit.DeficitId,
            deficit.OrganizationId,
            deficit.CounterpartyId,
            deficit.CurrencyCode,
            deficit.OccurredAtUtc,
            deficit.OriginalAmount,
            deficit.OriginalAmountKzt,
            deficit.CurrentAmount,
            deficit.CurrentAmountKzt);

    private static void ValidateCommand(TreasuryDeficitSettlementCommand command)
    {
        if (command.CommandId == Guid.Empty
            || command.OrganizationId == Guid.Empty
            || command.CounterpartyId == Guid.Empty
            || command.CurrencyId == Guid.Empty
            || command.UserId == Guid.Empty
            || command.SourceExpenseRequestId == Guid.Empty)
        {
            throw new TreasuryDeficitSettlementUnavailableException();
        }
        _ = OverpaymentDeficitSettlementPlanner.Plan(
            new OverpaymentScope(
                command.OrganizationId,
                command.CounterpartyId,
                command.CurrencyCode),
            command.Kind,
            command.SourceAmount,
            command.SourceAmountKzt,
            []);
        ValidateSourceReference(command.SourceReference);
    }

    private static void ValidateDeficits(
        TreasuryDeficitSettlementCommand command,
        DateTimeOffset appliedAtUtc,
        IReadOnlyList<StoredOverpaymentDeficit> deficits)
    {
        var deficitIds = new HashSet<Guid>();
        foreach (var deficit in deficits)
        {
            if (deficit.DeficitId == Guid.Empty
                || !deficitIds.Add(deficit.DeficitId)
                || deficit.OrganizationId != command.OrganizationId
                || deficit.CounterpartyId != command.CounterpartyId
                || deficit.CurrencyId != command.CurrencyId
                || !string.Equals(
                    deficit.CurrencyCode,
                    command.CurrencyCode,
                    StringComparison.Ordinal)
                || deficit.SourceOverpaymentId == Guid.Empty
                || string.IsNullOrWhiteSpace(deficit.ExternalReversalId)
                || !string.Equals(
                    deficit.ExternalReversalId,
                    deficit.ExternalReversalId.Trim(),
                    StringComparison.Ordinal)
                || deficit.ExternalReversalId.Any(char.IsControl)
                || deficit.ExternalReversalId.Length > 150
                || deficit.CreationCommandId == Guid.Empty
                || deficit.SourceExpenseRequestId == Guid.Empty
                || deficit.ActualCorrectionExecutorId == Guid.Empty
                || deficit.OccurredAtUtc == default
                || deficit.OccurredAtUtc.Ticks % TimeSpan.TicksPerMillisecond != 0
                || deficit.OccurredAtUtc > appliedAtUtc
                || deficit.SettledAtUtc is not null)
            {
                throw new TreasuryDeficitSettlementUnavailableException();
            }
        }

        _ = OverpaymentDeficitSettlementPlanner.Plan(
            new OverpaymentScope(
                command.OrganizationId,
                command.CounterpartyId,
                command.CurrencyCode),
            command.Kind,
            command.SourceAmount,
            command.SourceAmountKzt,
            deficits.Select(ToSnapshot));
    }

    private static void ValidateSourceReference(string sourceReference)
    {
        if (string.IsNullOrWhiteSpace(sourceReference)
            || !string.Equals(sourceReference, sourceReference.Trim(), StringComparison.Ordinal)
            || sourceReference.Length > 150
            || sourceReference.Any(char.IsControl))
        {
            throw new TreasuryDeficitSettlementUnavailableException();
        }
    }

    private static int AddAffectedEntries(int current, int addition)
    {
        try
        {
            return checked(current + addition);
        }
        catch (OverflowException exception)
        {
            throw new TreasuryDeficitSettlementUnavailableException(exception);
        }
    }

    private static void EnsureAttemptReady(ITreasuryDeficitSettlementRuntime runtime)
    {
        if (!runtime.HasSerializableTransaction || runtime.HasPendingChanges)
        {
            throw new TreasuryDeficitSettlementUnavailableException();
        }
    }
}
