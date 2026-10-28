using Bms.Domain.Common;
using Bms.Domain.Payments;

namespace Bms.Infrastructure.Persistence;

internal sealed record TreasuryOverpaymentSourceCreationCommand(
    Guid SourceId,
    Guid OrganizationId,
    Guid CounterpartyId,
    Guid CurrencyId,
    Guid SourceExpenseRequestId,
    Guid SourceEventId,
    decimal AmountKzt,
    DateTimeOffset OccurredAtUtc,
    bool AllowCreate = true);

internal enum TreasuryOverpaymentSourceCreationOutcome
{
    Created = 1,
    AlreadyCreated = 2,
}

internal sealed record TreasuryOverpaymentSourceCreationResult(
    TreasuryOverpaymentSourceCreationOutcome Outcome,
    Guid SourceId,
    decimal AmountKzt,
    int AffectedEntries);

internal sealed class TreasuryOverpaymentSourceCreationUnavailableException : Exception
{
    private const string SafeMessage =
        "The confirmed overpayment source could not be stored consistently.";

    internal TreasuryOverpaymentSourceCreationUnavailableException()
        : base(SafeMessage)
    {
    }

    internal TreasuryOverpaymentSourceCreationUnavailableException(Exception innerException)
        : base(
            SafeMessage,
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}

internal interface ITreasuryOverpaymentSourceCreationRuntime
{
    bool HasSerializableTransaction { get; }

    bool HasPendingChanges { get; }

    Task AcquireExclusiveTransactionLockAsync(
        string resource,
        CancellationToken cancellationToken);

    Task<StoredTreasuryPaymentFact?> FindPaymentFactAsync(
        Guid eventId,
        CancellationToken cancellationToken);

    Task<OverpaymentSource?> FindSourceByEventAsync(
        Guid sourceEventId,
        CancellationToken cancellationToken);

    Task<OverpaymentSource?> FindSourceByIdAsync(
        Guid sourceId,
        CancellationToken cancellationToken);

    Task<bool> IsExactKztScopeAsync(
        Guid sourceExpenseRequestId,
        Guid organizationId,
        Guid counterpartyId,
        Guid currencyId,
        CancellationToken cancellationToken);

    Task<int> InsertSourceAsync(
        OverpaymentSource source,
        CancellationToken cancellationToken);
}

/// <summary>
/// Dormant inner write stage creating one free KZT overpayment source from the
/// excess of a confirmed payment, after the caller has already settled every
/// active deficit of the scope. It neither opens nor commits a transaction and
/// writes no audit or outbox; the orchestrator owns both. One confirmed event
/// creates at most one source, so replay is resolved by the SourceEventId.
/// </summary>
internal static class TreasuryOverpaymentSourceCreationWriter
{
    internal static async Task<TreasuryOverpaymentSourceCreationResult> CreateAsync(
        TreasuryOverpaymentSourceCreationCommand command,
        ITreasuryOverpaymentSourceCreationRuntime runtime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(runtime);

        try
        {
            ValidateCommand(command);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);

            foreach (var resource in BusinessManagedLockResources.Order([
                         BusinessManagedLockResources.TreasuryScope(
                             command.OrganizationId,
                             command.CounterpartyId,
                             command.CurrencyId),
                         BusinessManagedLockResources.TreasurySource(command.SourceId),
                         BusinessManagedLockResources.TreasuryEvent(command.SourceEventId)
                     ]))
            {
                await runtime.AcquireExclusiveTransactionLockAsync(
                    resource,
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);

            var fact = await runtime.FindPaymentFactAsync(
                command.SourceEventId,
                cancellationToken);
            if (fact is null
                || fact.IsReversal
                || fact.ExpenseRequestId != command.SourceExpenseRequestId
                || fact.CurrencyId != command.CurrencyId
                || fact.Amount != fact.AmountKzt
                || fact.PaidAt != command.OccurredAtUtc
                || command.AmountKzt > fact.AmountKzt)
            {
                throw new TreasuryOverpaymentSourceCreationUnavailableException();
            }

            if (!await runtime.IsExactKztScopeAsync(
                    command.SourceExpenseRequestId,
                    command.OrganizationId,
                    command.CounterpartyId,
                    command.CurrencyId,
                    cancellationToken))
            {
                throw new TreasuryOverpaymentSourceCreationUnavailableException();
            }

            var existingByEvent = await runtime.FindSourceByEventAsync(
                command.SourceEventId,
                cancellationToken);
            if (existingByEvent is not null)
            {
                if (!MatchesEventCommand(existingByEvent, command))
                {
                    throw new TreasuryOverpaymentSourceCreationUnavailableException();
                }

                return new TreasuryOverpaymentSourceCreationResult(
                    TreasuryOverpaymentSourceCreationOutcome.AlreadyCreated,
                    existingByEvent.SourceId,
                    existingByEvent.OriginalAmountKzt,
                    0);
            }

            var existingById = await runtime.FindSourceByIdAsync(
                command.SourceId,
                cancellationToken);
            if (existingById is not null)
            {
                throw new TreasuryOverpaymentSourceCreationUnavailableException();
            }

            // An already-stored fact may validate its exact existing source,
            // but must never manufacture a new source from today's cumulative
            // excess. Repair/attribution of a missing historical source is not
            // approved. Keep this gate after normal fact/scope/replay checks.
            if (!command.AllowCreate)
            {
                throw new TreasuryOverpaymentSourceCreationUnavailableException();
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);
            var source = new OverpaymentSource
            {
                OrganizationId = command.OrganizationId,
                CounterpartyId = command.CounterpartyId,
                CurrencyId = command.CurrencyId,
                SourceId = command.SourceId,
                OriginalAmount = command.AmountKzt,
                OriginalAmountKzt = command.AmountKzt,
                ReversedAmount = 0m,
                ReversedAmountKzt = 0m,
                DeficitTransferredAmount = 0m,
                DeficitTransferredAmountKzt = 0m,
                CurrentRemainingAmount = command.AmountKzt,
                CurrentRemainingAmountKzt = command.AmountKzt,
                ReservedAmount = 0m,
                ReservedAmountKzt = 0m,
                SourceExpenseRequestId = command.SourceExpenseRequestId,
                SourceEventId = command.SourceEventId,
                OccurredAt = command.OccurredAtUtc,
            };
            var affectedEntries = await runtime.InsertSourceAsync(source, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (affectedEntries != 1 || runtime.HasPendingChanges)
            {
                throw new TreasuryOverpaymentSourceCreationUnavailableException();
            }

            var persisted = await runtime.FindSourceByEventAsync(
                command.SourceEventId,
                cancellationToken);
            if (!runtime.HasSerializableTransaction
                || runtime.HasPendingChanges
                || persisted is null
                || !MatchesInsertedSource(persisted, command)
                || persisted.CurrentRemainingAmountKzt != command.AmountKzt
                || persisted.ReservedAmountKzt != 0m)
            {
                throw new TreasuryOverpaymentSourceCreationUnavailableException();
            }

            return new TreasuryOverpaymentSourceCreationResult(
                TreasuryOverpaymentSourceCreationOutcome.Created,
                command.SourceId,
                command.AmountKzt,
                affectedEntries);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TreasuryOverpaymentSourceCreationUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TreasuryOverpaymentSourceCreationUnavailableException(exception);
        }
    }

    private static void ValidateCommand(TreasuryOverpaymentSourceCreationCommand command)
    {
        if (command.SourceId == Guid.Empty
            || command.OrganizationId == Guid.Empty
            || command.CounterpartyId == Guid.Empty
            || command.CurrencyId == Guid.Empty
            || command.SourceExpenseRequestId == Guid.Empty
            || command.SourceEventId == Guid.Empty)
        {
            throw new TreasuryOverpaymentSourceCreationUnavailableException();
        }

        MoneyAmountPolicy.EnsurePositive(
            command.AmountKzt,
            "Overpayment source KZT amount");
        ConfirmedPaymentEventContract.ValidateProcessingTime(command.OccurredAtUtc);
    }

    private static bool MatchesEventCommand(
        OverpaymentSource stored,
        TreasuryOverpaymentSourceCreationCommand command) =>
        stored.OrganizationId == command.OrganizationId
        && stored.CounterpartyId == command.CounterpartyId
        && stored.CurrencyId == command.CurrencyId
        && stored.SourceExpenseRequestId == command.SourceExpenseRequestId
        && stored.SourceEventId == command.SourceEventId
        && stored.OriginalAmount == command.AmountKzt
        && stored.OriginalAmountKzt == command.AmountKzt
        && stored.OccurredAt == command.OccurredAtUtc;

    private static bool MatchesInsertedSource(
        OverpaymentSource stored,
        TreasuryOverpaymentSourceCreationCommand command) =>
        stored.SourceId == command.SourceId
        && MatchesEventCommand(stored, command);

    private static void EnsureAttemptReady(ITreasuryOverpaymentSourceCreationRuntime runtime)
    {
        if (!runtime.HasSerializableTransaction || runtime.HasPendingChanges)
        {
            throw new TreasuryOverpaymentSourceCreationUnavailableException();
        }
    }
}
