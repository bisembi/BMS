using Bms.Domain.Common;
using Bms.Domain.Payments;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Dormant inner write stage that releases every active overpayment
/// reservation of one target request. The outer command owns status authority,
/// payment-order checks, idempotency, audit, outbox and the transaction.
/// </summary>
internal static class TreasuryOverpaymentReleaseWriter
{
    private const string ReservedStatus = "Reserved";
    private const string ReleasedStatus = "Released";

    internal static async Task<TreasuryOverpaymentReleaseResult> ReleaseAllAsync(
        TreasuryOverpaymentReleaseCommand command,
        DateTimeOffset releasedAtUtc,
        ITreasuryOverpaymentReleaseRuntime runtime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(runtime);

        try
        {
            ValidateCommand(command);
            ConfirmedPaymentEventContract.ValidateProcessingTime(releasedAtUtc);
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
                throw new TreasuryOverpaymentReleaseUnavailableException();
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
                throw new TreasuryOverpaymentReleaseUnavailableException();
            }
            await EnsureCommandIsFreshAsync(command, runtime, cancellationToken);
            var initialState = await LoadStateAsync(
                command.TargetExpenseRequestId,
                lockedScope,
                releasedAtUtc,
                runtime,
                cancellationToken);

            var sourceResources = initialState.Sources
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
                throw new TreasuryOverpaymentReleaseUnavailableException();
            }
            await EnsureCommandIsFreshAsync(command, runtime, cancellationToken);
            var state = await LoadStateAsync(
                command.TargetExpenseRequestId,
                finalScope,
                releasedAtUtc,
                runtime,
                cancellationToken);
            if (!HaveSameState(initialState, state))
            {
                throw new TreasuryOverpaymentReleaseUnavailableException();
            }

            var releaseBySource = BuildReleaseBySource(state);
            var affectedEntries = 0;
            foreach (var release in releaseBySource.Values
                         .OrderBy(item => item.Source.SourceId.ToString("N"), StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureAttemptReady(runtime);
                var affected = await runtime.ReleaseSourceAsync(
                    release.Source,
                    release.Amount,
                    release.AmountKzt,
                    cancellationToken);
                if (affected != 1 || runtime.HasPendingChanges)
                {
                    throw new TreasuryOverpaymentReleaseUnavailableException();
                }
                affectedEntries = AddAffectedEntries(affectedEntries, affected);
            }

            var releasedAllocations = new List<StoredOverpaymentAllocation>();
            foreach (var allocation in state.Allocations
                         .OrderBy(item => item.AllocationId.ToString("N"), StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureAttemptReady(runtime);
                var affected = await runtime.MarkAllocationReleasedAsync(
                    allocation,
                    command.CommandId,
                    command.UserId,
                    releasedAtUtc,
                    cancellationToken);
                if (affected != 1 || runtime.HasPendingChanges)
                {
                    throw new TreasuryOverpaymentReleaseUnavailableException();
                }
                affectedEntries = AddAffectedEntries(affectedEntries, affected);
                releasedAllocations.Add(allocation with
                {
                    Status = ReleasedStatus,
                    CommandId = command.CommandId,
                    AppliedOrReleasedAtUtc = releasedAtUtc,
                    UserId = command.UserId
                });
            }

            await VerifyPersistedAsync(
                command,
                state,
                releaseBySource,
                releasedAllocations,
                runtime,
                cancellationToken);
            return new TreasuryOverpaymentReleaseResult(
                SumRepresentable(
                    state.Allocations.Select(allocation => allocation.Amount),
                    "Released overpayment amount"),
                SumRepresentable(
                    state.Allocations.Select(allocation => allocation.AmountKzt),
                    "Released overpayment KZT amount"),
                Array.AsReadOnly(releasedAllocations.ToArray()),
                affectedEntries);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TreasuryOverpaymentReleaseUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TreasuryOverpaymentReleaseUnavailableException(exception);
        }
    }

    private static async Task EnsureCommandIsFreshAsync(
        TreasuryOverpaymentReleaseCommand command,
        ITreasuryOverpaymentReleaseRuntime runtime,
        CancellationToken cancellationToken)
    {
        if (!await runtime.UserExistsAsync(command.UserId, cancellationToken)
            || (await runtime.FindAllocationsByCommandAsync(
                command.CommandId,
                cancellationToken)).Count != 0)
        {
            throw new TreasuryOverpaymentReleaseUnavailableException();
        }
    }

    private static async Task<ReleaseState> LoadStateAsync(
        Guid targetExpenseRequestId,
        StoredOverpaymentScope scope,
        DateTimeOffset releasedAtUtc,
        ITreasuryOverpaymentReleaseRuntime runtime,
        CancellationToken cancellationToken)
    {
        var allocations = await runtime.FindReservedAllocationsAsync(
            targetExpenseRequestId,
            cancellationToken);
        var sourceIds = allocations.Select(allocation => allocation.SourceId).Distinct().ToArray();
        var sources = await runtime.FindSourcesAsync(sourceIds, cancellationToken);
        var state = new ReleaseState(allocations, sources);
        ValidateState(targetExpenseRequestId, scope, releasedAtUtc, state);
        return state;
    }

    private static Dictionary<Guid, SourceRelease> BuildReleaseBySource(ReleaseState state)
    {
        var sources = state.Sources.ToDictionary(source => source.SourceId);
        var result = new Dictionary<Guid, SourceRelease>();
        foreach (var group in state.Allocations.GroupBy(allocation => allocation.SourceId))
        {
            var amount = SumRepresentable(
                group.Select(allocation => allocation.Amount),
                "Source reservation release amount");
            var amountKzt = SumRepresentable(
                group.Select(allocation => allocation.AmountKzt),
                "Source reservation release KZT amount");
            var source = sources[group.Key];
            if (amount > source.ReservedAmount
                || amountKzt > source.ReservedAmountKzt
                || ((source.ReservedAmount - amount == 0m) !=
                    (source.ReservedAmountKzt - amountKzt == 0m)))
            {
                throw new TreasuryOverpaymentReleaseUnavailableException();
            }
            result.Add(group.Key, new SourceRelease(source, amount, amountKzt));
        }
        return result;
    }

    private static async Task VerifyPersistedAsync(
        TreasuryOverpaymentReleaseCommand command,
        ReleaseState originalState,
        IReadOnlyDictionary<Guid, SourceRelease> releaseBySource,
        IReadOnlyList<StoredOverpaymentAllocation> releasedAllocations,
        ITreasuryOverpaymentReleaseRuntime runtime,
        CancellationToken cancellationToken)
    {
        foreach (var released in releasedAllocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var persisted = await runtime.FindAllocationByIdAsync(
                released.AllocationId,
                cancellationToken);
            if (persisted != released)
            {
                throw new TreasuryOverpaymentReleaseUnavailableException();
            }
        }

        foreach (var release in releaseBySource.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var persisted = await runtime.FindSourceByIdAsync(
                release.Source.SourceId,
                cancellationToken);
            if (persisted != release.Source with
            {
                ReservedAmount = release.Source.ReservedAmount - release.Amount,
                ReservedAmountKzt = release.Source.ReservedAmountKzt - release.AmountKzt
            })
            {
                throw new TreasuryOverpaymentReleaseUnavailableException();
            }
        }

        var commandAllocations = await runtime.FindAllocationsByCommandAsync(
            command.CommandId,
            cancellationToken);
        var remainingReserved = await runtime.FindReservedAllocationsAsync(
            command.TargetExpenseRequestId,
            cancellationToken);
        if (!commandAllocations
                .OrderBy(allocation => allocation.AllocationId)
                .SequenceEqual(releasedAllocations.OrderBy(allocation => allocation.AllocationId))
            || remainingReserved.Count != 0
            || originalState.Allocations.Count != releasedAllocations.Count
            || !runtime.HasSerializableTransaction
            || runtime.HasPendingChanges)
        {
            throw new TreasuryOverpaymentReleaseUnavailableException();
        }
    }

    private static bool HaveSameState(ReleaseState left, ReleaseState right) =>
        left.Allocations.OrderBy(item => item.AllocationId)
            .SequenceEqual(right.Allocations.OrderBy(item => item.AllocationId))
        && left.Sources.OrderBy(item => item.SourceId)
            .SequenceEqual(right.Sources.OrderBy(item => item.SourceId));

    private static void ValidateState(
        Guid targetExpenseRequestId,
        StoredOverpaymentScope scope,
        DateTimeOffset releasedAtUtc,
        ReleaseState state)
    {
        var sourceIds = new HashSet<Guid>();
        foreach (var source in state.Sources)
        {
            if (source.SourceId == Guid.Empty
                || !sourceIds.Add(source.SourceId)
                || source.OrganizationId != scope.OrganizationId
                || source.CounterpartyId != scope.CounterpartyId
                || source.CurrencyId != scope.CurrencyId
                || !string.Equals(source.CurrencyCode, scope.CurrencyCode, StringComparison.Ordinal))
            {
                throw new TreasuryOverpaymentReleaseUnavailableException();
            }
            MoneyAmountPolicy.EnsureNonNegative(
                source.CurrentRemainingAmount,
                "Overpayment source current amount");
            MoneyAmountPolicy.EnsureNonNegative(
                source.CurrentRemainingAmountKzt,
                "Overpayment source current KZT amount");
            MoneyAmountPolicy.EnsureNonNegative(
                source.ReservedAmount,
                "Overpayment source reserved amount");
            MoneyAmountPolicy.EnsureNonNegative(
                source.ReservedAmountKzt,
                "Overpayment source reserved KZT amount");
            if (source.ReservedAmount > source.CurrentRemainingAmount
                || source.ReservedAmountKzt > source.CurrentRemainingAmountKzt
                || ((source.CurrentRemainingAmount == 0m) !=
                    (source.CurrentRemainingAmountKzt == 0m))
                || ((source.ReservedAmount == 0m) != (source.ReservedAmountKzt == 0m))
                || (scope.CurrencyCode == "KZT"
                    && (source.CurrentRemainingAmount != source.CurrentRemainingAmountKzt
                        || source.ReservedAmount != source.ReservedAmountKzt)))
            {
                throw new TreasuryOverpaymentReleaseUnavailableException();
            }
        }

        var allocationIds = new HashSet<Guid>();
        foreach (var allocation in state.Allocations)
        {
            if (allocation.TargetExpenseRequestId != targetExpenseRequestId
                || allocation.SourceId == Guid.Empty
                || !sourceIds.Contains(allocation.SourceId)
                || allocation.AllocationId == Guid.Empty
                || !allocationIds.Add(allocation.AllocationId)
                || allocation.CommandId == Guid.Empty
                || allocation.UserId == Guid.Empty
                || !string.Equals(allocation.Status, ReservedStatus, StringComparison.Ordinal)
                || allocation.AppliedOrReleasedAtUtc is not null
                || allocation.ReservedAtUtc > releasedAtUtc)
            {
                throw new TreasuryOverpaymentReleaseUnavailableException();
            }
            ConfirmedPaymentEventContract.ValidateProcessingTime(allocation.ReservedAtUtc);
            MoneyAmountPolicy.EnsurePositive(
                allocation.Amount,
                "Overpayment allocation amount");
            MoneyAmountPolicy.EnsurePositive(
                allocation.AmountKzt,
                "Overpayment allocation KZT amount");
            if (scope.CurrencyCode == "KZT" && allocation.Amount != allocation.AmountKzt)
            {
                throw new TreasuryOverpaymentReleaseUnavailableException();
            }
        }

        if (state.Allocations.Count == 0 && state.Sources.Count != 0
            || state.Allocations.Count != 0 && state.Sources.Count == 0)
        {
            throw new TreasuryOverpaymentReleaseUnavailableException();
        }
    }

    private static void ValidateCommand(TreasuryOverpaymentReleaseCommand command)
    {
        if (command.CommandId == Guid.Empty
            || command.TargetExpenseRequestId == Guid.Empty
            || command.UserId == Guid.Empty)
        {
            throw new TreasuryOverpaymentReleaseUnavailableException();
        }
    }

    private static void ValidateScope(StoredOverpaymentScope scope)
    {
        if (scope.OrganizationId == Guid.Empty
            || scope.CounterpartyId == Guid.Empty
            || scope.CurrencyId == Guid.Empty
            || scope.CurrencyCode is not { Length: 3 }
            || scope.CurrencyCode.Any(character => character is < 'A' or > 'Z'))
        {
            throw new TreasuryOverpaymentReleaseUnavailableException();
        }
    }

    private static decimal SumRepresentable(IEnumerable<decimal> values, string fieldName)
    {
        var total = 0m;
        foreach (var value in values)
        {
            try
            {
                total += value;
            }
            catch (OverflowException)
            {
                throw new DomainRuleViolationException(
                    $"{fieldName} exceeds decimal storage range.");
            }
            MoneyAmountPolicy.EnsureNonNegative(total, fieldName);
        }
        return total;
    }

    private static int AddAffectedEntries(int current, int addition)
    {
        try
        {
            return checked(current + addition);
        }
        catch (OverflowException exception)
        {
            throw new TreasuryOverpaymentReleaseUnavailableException(exception);
        }
    }

    private static void EnsureAttemptReady(ITreasuryOverpaymentReleaseRuntime runtime)
    {
        if (!runtime.HasSerializableTransaction || runtime.HasPendingChanges)
        {
            throw new TreasuryOverpaymentReleaseUnavailableException();
        }
    }

    private sealed record ReleaseState(
        IReadOnlyList<StoredOverpaymentAllocation> Allocations,
        IReadOnlyList<StoredOverpaymentSource> Sources);

    private sealed record SourceRelease(
        StoredOverpaymentSource Source,
        decimal Amount,
        decimal AmountKzt);
}
