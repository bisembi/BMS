using Bms.Domain.Common;
using Bms.Domain.Overpayments;
using Bms.Domain.Payments;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Dormant inner write stage for FIFO reservation of existing overpayment
/// sources. It neither owns the outer transaction nor records command
/// idempotency, audit, outbox or request status changes.
/// </summary>
internal static class TreasuryOverpaymentReservationWriter
{
    private const string ReservedStatus = "Reserved";

    internal static async Task<TreasuryOverpaymentReservationResult> ReserveAsync(
        TreasuryOverpaymentReservationCommand command,
        DateTimeOffset reservedAtUtc,
        ITreasuryOverpaymentReservationRuntime runtime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(runtime);

        try
        {
            ValidateCommand(command);
            ConfirmedPaymentEventContract.ValidateProcessingTime(reservedAtUtc);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);

            await runtime.AcquireExclusiveTransactionLockAsync(
                BusinessManagedLockResources.ExpenseRequest(command.TargetExpenseRequestId),
                cancellationToken);

            var initialScope = await runtime.FindTargetScopeAsync(
                command.TargetExpenseRequestId,
                cancellationToken);
            if (initialScope is null)
            {
                throw new TreasuryOverpaymentReservationUnavailableException();
            }
            ValidateScope(initialScope);

            foreach (var resource in BusinessManagedLockResources.Order([
                         BusinessManagedLockResources.TreasuryCommand(command.CommandId),
                         BusinessManagedLockResources.TreasuryScope(
                             initialScope.OrganizationId,
                             initialScope.CounterpartyId,
                             initialScope.CurrencyId)
                     ]))
            {
                await runtime.AcquireExclusiveTransactionLockAsync(
                    resource,
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);
            var lockedScope = await runtime.FindTargetScopeAsync(
                command.TargetExpenseRequestId,
                cancellationToken);
            if (lockedScope is null || lockedScope != initialScope)
            {
                throw new TreasuryOverpaymentReservationUnavailableException();
            }

            await EnsureCommandIsFreshAsync(command, runtime, cancellationToken);
            var initialSources = await LoadEligibleStateAsync(
                lockedScope,
                runtime,
                cancellationToken);

            var sourceResources = initialSources
                .Select(source => BusinessManagedLockResources.TreasurySource(source.SourceId))
                .ToArray();
            if (sourceResources.Length > 0)
            {
                foreach (var resource in BusinessManagedLockResources.Order(sourceResources))
                {
                    await runtime.AcquireExclusiveTransactionLockAsync(
                        resource,
                        cancellationToken);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);
            var finalScope = await runtime.FindTargetScopeAsync(
                command.TargetExpenseRequestId,
                cancellationToken);
            if (finalScope is null || finalScope != lockedScope)
            {
                throw new TreasuryOverpaymentReservationUnavailableException();
            }

            await EnsureCommandIsFreshAsync(command, runtime, cancellationToken);
            var sources = await LoadEligibleStateAsync(
                finalScope,
                runtime,
                cancellationToken);
            if (!HaveSameSources(initialSources, sources))
            {
                throw new TreasuryOverpaymentReservationUnavailableException();
            }
            var plan = OverpaymentSourceReservationPlanner.Plan(
                new OverpaymentScope(
                    finalScope.OrganizationId,
                    finalScope.CounterpartyId,
                    finalScope.CurrencyCode),
                command.RequestedAmount,
                hasActiveDeficit: false,
                sources.Select(ToSnapshot));

            var storedAllocations = new List<StoredOverpaymentAllocation>();
            var allocationIds = new HashSet<Guid>();
            var affectedEntries = 0;
            foreach (var planned in plan.Allocations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureAttemptReady(runtime);
                var source = sources.Single(item => item.SourceId == planned.SourceId);
                var allocationId = runtime.CreateAllocationId();
                if (allocationId == Guid.Empty || !allocationIds.Add(allocationId))
                {
                    throw new TreasuryOverpaymentReservationUnavailableException();
                }

                var sourceAffected = await runtime.ReserveSourceAsync(
                    source,
                    planned.SelectedAmount,
                    planned.SelectedAmountKzt,
                    cancellationToken);
                if (sourceAffected != 1 || runtime.HasPendingChanges)
                {
                    throw new TreasuryOverpaymentReservationUnavailableException();
                }

                var allocation = new StoredOverpaymentAllocation(
                    command.TargetExpenseRequestId,
                    planned.SourceId,
                    allocationId,
                    planned.SelectedAmount,
                    planned.SelectedAmountKzt,
                    ReservedStatus,
                    command.CommandId,
                    reservedAtUtc,
                    null,
                    command.UserId);
                var allocationAffected = await runtime.InsertAllocationAsync(
                    allocation,
                    cancellationToken);
                if (allocationAffected != 1 || runtime.HasPendingChanges)
                {
                    throw new TreasuryOverpaymentReservationUnavailableException();
                }

                affectedEntries = AddAffectedEntries(
                    affectedEntries,
                    sourceAffected + allocationAffected);
                storedAllocations.Add(allocation);
            }

            await VerifyPersistedAsync(
                command.CommandId,
                sources,
                storedAllocations,
                runtime,
                cancellationToken);
            return new TreasuryOverpaymentReservationResult(
                plan.RequestedAmount,
                plan.SelectedAmount,
                plan.SelectedAmountKzt,
                plan.ShortfallAmount,
                Array.AsReadOnly(storedAllocations.ToArray()),
                affectedEntries);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TreasuryOverpaymentReservationUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TreasuryOverpaymentReservationUnavailableException(exception);
        }
    }

    private static async Task<IReadOnlyList<StoredOverpaymentSource>> LoadEligibleStateAsync(
        StoredOverpaymentScope scope,
        ITreasuryOverpaymentReservationRuntime runtime,
        CancellationToken cancellationToken)
    {
        if (await runtime.HasActiveDeficitAsync(scope, cancellationToken))
        {
            throw new TreasuryOverpaymentReservationUnavailableException();
        }

        var sources = await runtime.FindSourcesAsync(scope, cancellationToken);
        if (sources.Any(source => !MatchesScope(source, scope)))
        {
            throw new TreasuryOverpaymentReservationUnavailableException();
        }
        return sources;
    }

    private static async Task EnsureCommandIsFreshAsync(
        TreasuryOverpaymentReservationCommand command,
        ITreasuryOverpaymentReservationRuntime runtime,
        CancellationToken cancellationToken)
    {
        if (!await runtime.UserExistsAsync(command.UserId, cancellationToken)
            || (await runtime.FindAllocationsByCommandAsync(
                command.CommandId,
                cancellationToken)).Count != 0)
        {
            throw new TreasuryOverpaymentReservationUnavailableException();
        }
    }

    private static async Task VerifyPersistedAsync(
        Guid commandId,
        IReadOnlyList<StoredOverpaymentSource> originalSources,
        IReadOnlyList<StoredOverpaymentAllocation> allocations,
        ITreasuryOverpaymentReservationRuntime runtime,
        CancellationToken cancellationToken)
    {
        foreach (var allocation in allocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var persistedAllocation = await runtime.FindAllocationByIdAsync(
                allocation.AllocationId,
                cancellationToken);
            var persistedSource = await runtime.FindSourceByIdAsync(
                allocation.SourceId,
                cancellationToken);
            var originalSource = originalSources.Single(
                source => source.SourceId == allocation.SourceId);
            if (persistedAllocation != allocation
                || persistedSource is null
                || !MatchesReservedSource(originalSource, persistedSource, allocation)
                || !runtime.HasSerializableTransaction
                || runtime.HasPendingChanges)
            {
                throw new TreasuryOverpaymentReservationUnavailableException();
            }
        }

        var persistedCommandAllocations = await runtime.FindAllocationsByCommandAsync(
            commandId,
            cancellationToken);
        if (!persistedCommandAllocations
                .OrderBy(allocation => allocation.AllocationId)
                .SequenceEqual(allocations.OrderBy(allocation => allocation.AllocationId))
            || !runtime.HasSerializableTransaction
            || runtime.HasPendingChanges)
        {
            throw new TreasuryOverpaymentReservationUnavailableException();
        }
    }

    private static bool HaveSameSources(
        IReadOnlyList<StoredOverpaymentSource> initial,
        IReadOnlyList<StoredOverpaymentSource> final) =>
        initial.OrderBy(source => source.SourceId)
            .SequenceEqual(final.OrderBy(source => source.SourceId));

    private static bool MatchesReservedSource(
        StoredOverpaymentSource original,
        StoredOverpaymentSource actual,
        StoredOverpaymentAllocation allocation) =>
        actual == original with
        {
            ReservedAmount = original.ReservedAmount + allocation.Amount,
            ReservedAmountKzt = original.ReservedAmountKzt + allocation.AmountKzt
        };

    private static bool MatchesScope(
        StoredOverpaymentSource source,
        StoredOverpaymentScope scope) =>
        source.OrganizationId == scope.OrganizationId
        && source.CounterpartyId == scope.CounterpartyId
        && source.CurrencyId == scope.CurrencyId
        && string.Equals(source.CurrencyCode, scope.CurrencyCode, StringComparison.Ordinal);

    private static OverpaymentSourceSnapshot ToSnapshot(StoredOverpaymentSource source) =>
        new(
            source.SourceId,
            source.OrganizationId,
            source.CounterpartyId,
            source.CurrencyCode,
            source.OccurredAtUtc,
            source.CurrentRemainingAmount,
            source.CurrentRemainingAmountKzt,
            source.ReservedAmount,
            source.ReservedAmountKzt);

    private static void ValidateCommand(TreasuryOverpaymentReservationCommand command)
    {
        if (command.CommandId == Guid.Empty
            || command.TargetExpenseRequestId == Guid.Empty
            || command.UserId == Guid.Empty)
        {
            throw new TreasuryOverpaymentReservationUnavailableException();
        }
        MoneyAmountPolicy.EnsurePositive(
            command.RequestedAmount,
            "Requested overpayment reservation");
    }

    private static void ValidateScope(StoredOverpaymentScope scope)
    {
        if (scope.OrganizationId == Guid.Empty
            || scope.CounterpartyId == Guid.Empty
            || scope.CurrencyId == Guid.Empty
            || scope.CurrencyCode is not { Length: 3 }
            || scope.CurrencyCode.Any(character => character is < 'A' or > 'Z'))
        {
            throw new TreasuryOverpaymentReservationUnavailableException();
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
            throw new TreasuryOverpaymentReservationUnavailableException(exception);
        }
    }

    private static void EnsureAttemptReady(ITreasuryOverpaymentReservationRuntime runtime)
    {
        if (!runtime.HasSerializableTransaction || runtime.HasPendingChanges)
        {
            throw new TreasuryOverpaymentReservationUnavailableException();
        }
    }
}
