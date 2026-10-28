using Bms.Domain.Common;
using Bms.Domain.Overpayments;
using Bms.Domain.Payments;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Dormant inner stage for the W141 manual-deficit-correction lifecycle. Role
/// authority, command idempotency, audit, outbox, notifications and the
/// transaction remain owned by the outer orchestrator.
/// </summary>
internal static class TreasuryManualDeficitCorrectionWriter
{
    private const string PendingState = "PendingApproval";
    private const string ManualSettlementKind = "ManualCorrection";

    internal static async Task<TreasuryManualDeficitCorrectionCreateResult> CreateAsync(
        TreasuryManualDeficitCorrectionCreateCommand command,
        ITreasuryManualDeficitCorrectionRuntime runtime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(runtime);

        try
        {
            ValidateCreateCommand(command);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);

            var initialDeficit = await RequireDeficitAsync(
                command.DeficitId,
                runtime,
                cancellationToken);
            ValidateStoredDeficit(initialDeficit);
            await AcquireStateLocksAsync(
                command.CommandId,
                command.CorrectionId,
                initialDeficit,
                runtime,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);
            await EnsureCreateIsFreshAsync(
                command,
                initialDeficit,
                runtime,
                cancellationToken);
            var lockedDeficit = await RequireDeficitAsync(
                command.DeficitId,
                runtime,
                cancellationToken);
            if (lockedDeficit != initialDeficit)
            {
                throw new TreasuryManualDeficitCorrectionUnavailableException();
            }

            var proposal = ManualOverpaymentDeficitCorrectionPlanner.CreateProposal(
                command.CorrectionId,
                ToSnapshot(lockedDeficit),
                command.CorrectionAmount,
                command.Basis,
                command.ResponsibleAccountantId,
                hasPendingCorrection: false);
            var correction = ToStored(proposal, lockedDeficit.CurrencyId, command.CommandId);
            var affected = await runtime.InsertCorrectionAsync(
                correction,
                cancellationToken);
            if (affected != 1 || runtime.HasPendingChanges)
            {
                throw new TreasuryManualDeficitCorrectionUnavailableException();
            }

            await VerifyCreatedAsync(
                command,
                correction,
                lockedDeficit,
                runtime,
                cancellationToken);
            return new TreasuryManualDeficitCorrectionCreateResult(correction, affected);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TreasuryManualDeficitCorrectionUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TreasuryManualDeficitCorrectionUnavailableException(exception);
        }
    }

    internal static async Task<TreasuryManualDeficitCorrectionDecisionResult> DecideAsync(
        TreasuryManualDeficitCorrectionDecisionCommand command,
        DateTimeOffset decidedAtUtc,
        ITreasuryManualDeficitCorrectionRuntime runtime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(runtime);

        try
        {
            ValidateDecisionCommand(command);
            ConfirmedPaymentEventContract.ValidateProcessingTime(decidedAtUtc);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);

            var initialCorrection = await RequirePendingCorrectionAsync(
                command.CorrectionId,
                runtime,
                cancellationToken);
            var initialDeficit = await RequireDeficitAsync(
                initialCorrection.DeficitId,
                runtime,
                cancellationToken);
            ValidateCorrectionScope(initialCorrection, initialDeficit);
            await AcquireStateLocksAsync(
                command.CommandId,
                command.CorrectionId,
                initialDeficit,
                runtime,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);
            var correction = await RequirePendingCorrectionAsync(
                command.CorrectionId,
                runtime,
                cancellationToken);
            var deficit = await RequireDeficitAsync(
                correction.DeficitId,
                runtime,
                cancellationToken);
            if (correction != initialCorrection || deficit != initialDeficit)
            {
                throw new TreasuryManualDeficitCorrectionUnavailableException();
            }
            ValidateCorrectionScope(correction, deficit);
            if (deficit.OccurredAtUtc > decidedAtUtc
                || deficit.SettledAtUtc > decidedAtUtc)
            {
                throw new TreasuryManualDeficitCorrectionUnavailableException();
            }
            await EnsureDecisionIsFreshAsync(
                command,
                correction,
                deficit,
                runtime,
                cancellationToken);

            var proposal = ToProposal(correction);
            var decision = PlanDecision(command, proposal, deficit);
            var affectedEntries = 0;
            StoredManualDeficitSettlement? settlement = null;
            var expectedDeficit = deficit;
            if (decision.CreatesSettlement)
            {
                var amountAfter = decision.DeficitAmountAfter
                    ?? throw new TreasuryManualDeficitCorrectionUnavailableException();
                var amountKztAfter = decision.DeficitAmountKztAfter
                    ?? throw new TreasuryManualDeficitCorrectionUnavailableException();
                DateTimeOffset? settledAtUtc = amountAfter == 0m ? decidedAtUtc : null;
                var affected = await runtime.UpdateDeficitAsync(
                    deficit,
                    amountAfter,
                    amountKztAfter,
                    command.FinancialDirectorId,
                    settledAtUtc,
                    cancellationToken);
                EnsureSingleCleanWrite(affected, runtime);
                affectedEntries = AddAffectedEntries(affectedEntries, affected);
                expectedDeficit = deficit with
                {
                    CurrentAmount = amountAfter,
                    CurrentAmountKzt = amountKztAfter,
                    ActualCorrectionExecutorId = command.FinancialDirectorId,
                    SettledAtUtc = settledAtUtc
                };

                settlement = new StoredManualDeficitSettlement(
                    deficit.DeficitId,
                    command.CommandId,
                    ManualSettlementKind,
                    correction.CorrectionId,
                    decision.AppliedAmount,
                    decision.AppliedAmountKzt,
                    decidedAtUtc,
                    command.FinancialDirectorId);
                affected = await runtime.InsertSettlementAsync(
                    settlement,
                    cancellationToken);
                EnsureSingleCleanWrite(affected, runtime);
                affectedEntries = AddAffectedEntries(affectedEntries, affected);
            }

            var correctionAffected = await runtime.UpdateCorrectionDecisionAsync(
                correction,
                decision,
                command.CommandId,
                decidedAtUtc,
                cancellationToken);
            EnsureSingleCleanWrite(correctionAffected, runtime);
            affectedEntries = AddAffectedEntries(affectedEntries, correctionAffected);
            var expectedCorrection = correction with
            {
                FinancialDirectorId = command.FinancialDirectorId,
                State = decision.State.ToString(),
                DecidedAtUtc = decidedAtUtc,
                DecisionComment = decision.DecisionComment,
                RejectionReason = decision.RejectionReason,
                DecisionCommandId = command.CommandId
            };

            await VerifyDecidedAsync(
                command,
                expectedCorrection,
                expectedDeficit,
                settlement,
                runtime,
                cancellationToken);
            return new TreasuryManualDeficitCorrectionDecisionResult(
                expectedCorrection,
                expectedDeficit,
                settlement,
                affectedEntries);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TreasuryManualDeficitCorrectionUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TreasuryManualDeficitCorrectionUnavailableException(exception);
        }
    }

    private static async Task AcquireStateLocksAsync(
        Guid commandId,
        Guid correctionId,
        StoredOverpaymentDeficit deficit,
        ITreasuryManualDeficitCorrectionRuntime runtime,
        CancellationToken cancellationToken)
    {
        foreach (var resource in BusinessManagedLockResources.Order([
                     BusinessManagedLockResources.TreasuryCommand(commandId),
                     BusinessManagedLockResources.TreasuryManualCorrection(correctionId),
                     BusinessManagedLockResources.TreasuryScope(
                         deficit.OrganizationId,
                         deficit.CounterpartyId,
                         deficit.CurrencyId),
                     BusinessManagedLockResources.TreasurySource(
                         deficit.SourceOverpaymentId),
                     BusinessManagedLockResources.TreasuryDeficit(deficit.DeficitId)
                 ]))
        {
            await runtime.AcquireExclusiveTransactionLockAsync(resource, cancellationToken);
        }
    }

    private static async Task EnsureCreateIsFreshAsync(
        TreasuryManualDeficitCorrectionCreateCommand command,
        StoredOverpaymentDeficit deficit,
        ITreasuryManualDeficitCorrectionRuntime runtime,
        CancellationToken cancellationToken)
    {
        if (!await runtime.EmployeeExistsAsync(
                command.ResponsibleAccountantId,
                cancellationToken)
            || await runtime.CommandExistsAsync(command.CommandId, cancellationToken)
            || await runtime.FindCorrectionByIdAsync(
                command.CorrectionId,
                cancellationToken) is not null
            || (await runtime.FindPendingCorrectionsByDeficitAsync(
                command.DeficitId,
                cancellationToken)).Count != 0
            || await runtime.FindSettlementByCorrectionAsync(
                command.CorrectionId,
                cancellationToken) is not null
            || !await SourceMatchesScopeAsync(deficit, runtime, cancellationToken))
        {
            throw new TreasuryManualDeficitCorrectionUnavailableException();
        }
    }

    private static async Task EnsureDecisionIsFreshAsync(
        TreasuryManualDeficitCorrectionDecisionCommand command,
        StoredManualOverpaymentDeficitCorrection correction,
        StoredOverpaymentDeficit deficit,
        ITreasuryManualDeficitCorrectionRuntime runtime,
        CancellationToken cancellationToken)
    {
        var pending = await runtime.FindPendingCorrectionsByDeficitAsync(
            correction.DeficitId,
            cancellationToken);
        if (!await runtime.EmployeeExistsAsync(
                command.FinancialDirectorId,
                cancellationToken)
            || await runtime.CommandExistsAsync(command.CommandId, cancellationToken)
            || pending.Count != 1
            || pending[0] != correction
            || await runtime.FindSettlementByCorrectionAsync(
                command.CorrectionId,
                cancellationToken) is not null
            || !await SourceMatchesScopeAsync(deficit, runtime, cancellationToken))
        {
            throw new TreasuryManualDeficitCorrectionUnavailableException();
        }
    }

    private static Task<bool> SourceMatchesScopeAsync(
        StoredOverpaymentDeficit deficit,
        ITreasuryManualDeficitCorrectionRuntime runtime,
        CancellationToken cancellationToken) =>
        runtime.SourceMatchesScopeAsync(
            deficit.SourceOverpaymentId,
            deficit.OrganizationId,
            deficit.CounterpartyId,
            deficit.CurrencyId,
            deficit.SourceExpenseRequestId,
            deficit.OccurredAtUtc,
            cancellationToken);

    private static ManualOverpaymentDeficitCorrectionDecision PlanDecision(
        TreasuryManualDeficitCorrectionDecisionCommand command,
        ManualOverpaymentDeficitCorrectionProposal proposal,
        StoredOverpaymentDeficit deficit) =>
        command.Kind switch
        {
            TreasuryManualDeficitCorrectionDecisionKind.Approve
                when command.RejectionReason is null =>
                ManualOverpaymentDeficitCorrectionPlanner.Approve(
                    proposal,
                    ToSnapshot(deficit),
                    command.FinancialDirectorId,
                    command.DecisionComment),
            TreasuryManualDeficitCorrectionDecisionKind.Reject =>
                ManualOverpaymentDeficitCorrectionPlanner.Reject(
                    proposal,
                    command.FinancialDirectorId,
                    command.RejectionReason
                        ?? throw new TreasuryManualDeficitCorrectionUnavailableException(),
                    command.DecisionComment),
            _ => throw new TreasuryManualDeficitCorrectionUnavailableException()
        };

    private static async Task VerifyCreatedAsync(
        TreasuryManualDeficitCorrectionCreateCommand command,
        StoredManualOverpaymentDeficitCorrection correction,
        StoredOverpaymentDeficit deficit,
        ITreasuryManualDeficitCorrectionRuntime runtime,
        CancellationToken cancellationToken)
    {
        var persisted = await runtime.FindCorrectionByIdAsync(
            command.CorrectionId,
            cancellationToken);
        var pending = await runtime.FindPendingCorrectionsByDeficitAsync(
            command.DeficitId,
            cancellationToken);
        var settlements = await runtime.FindSettlementsByCommandAsync(
            command.CommandId,
            cancellationToken);
        var persistedDeficit = await runtime.FindDeficitByIdAsync(
            command.DeficitId,
            cancellationToken);
        if (persisted != correction
            || pending.Count != 1
            || pending[0] != correction
            || settlements.Count != 0
            || persistedDeficit != deficit
            || !await runtime.CommandExistsAsync(command.CommandId, cancellationToken)
            || !runtime.HasSerializableTransaction
            || runtime.HasPendingChanges)
        {
            throw new TreasuryManualDeficitCorrectionUnavailableException();
        }
    }

    private static async Task VerifyDecidedAsync(
        TreasuryManualDeficitCorrectionDecisionCommand command,
        StoredManualOverpaymentDeficitCorrection correction,
        StoredOverpaymentDeficit deficit,
        StoredManualDeficitSettlement? settlement,
        ITreasuryManualDeficitCorrectionRuntime runtime,
        CancellationToken cancellationToken)
    {
        var persistedCorrection = await runtime.FindCorrectionByIdAsync(
            command.CorrectionId,
            cancellationToken);
        var persistedDeficit = await runtime.FindDeficitByIdAsync(
            correction.DeficitId,
            cancellationToken);
        var persistedSettlement = await runtime.FindSettlementByCorrectionAsync(
            command.CorrectionId,
            cancellationToken);
        var commandSettlements = await runtime.FindSettlementsByCommandAsync(
            command.CommandId,
            cancellationToken);
        var pending = await runtime.FindPendingCorrectionsByDeficitAsync(
            correction.DeficitId,
            cancellationToken);
        var expectedSettlements = settlement is null
            ? Array.Empty<StoredManualDeficitSettlement>()
            : [settlement];
        if (persistedCorrection != correction
            || persistedDeficit != deficit
            || persistedSettlement != settlement
            || !commandSettlements.SequenceEqual(expectedSettlements)
            || pending.Count != 0
            || !await runtime.CommandExistsAsync(command.CommandId, cancellationToken)
            || !runtime.HasSerializableTransaction
            || runtime.HasPendingChanges)
        {
            throw new TreasuryManualDeficitCorrectionUnavailableException();
        }
    }

    private static async Task<StoredOverpaymentDeficit> RequireDeficitAsync(
        Guid deficitId,
        ITreasuryManualDeficitCorrectionRuntime runtime,
        CancellationToken cancellationToken)
    {
        var deficit = await runtime.FindDeficitByIdAsync(deficitId, cancellationToken);
        if (deficit is null)
        {
            throw new TreasuryManualDeficitCorrectionUnavailableException();
        }
        return deficit;
    }

    private static async Task<StoredManualOverpaymentDeficitCorrection>
        RequirePendingCorrectionAsync(
            Guid correctionId,
            ITreasuryManualDeficitCorrectionRuntime runtime,
            CancellationToken cancellationToken)
    {
        var correction = await runtime.FindCorrectionByIdAsync(
            correctionId,
            cancellationToken);
        if (correction is null
            || !string.Equals(correction.State, PendingState, StringComparison.Ordinal))
        {
            throw new TreasuryManualDeficitCorrectionUnavailableException();
        }
        return correction;
    }

    private static void ValidateCorrectionScope(
        StoredManualOverpaymentDeficitCorrection correction,
        StoredOverpaymentDeficit deficit)
    {
        ValidateStoredDeficit(deficit);
        _ = ToProposal(correction);
        if (correction.DeficitId != deficit.DeficitId
            || correction.OrganizationId != deficit.OrganizationId
            || correction.CounterpartyId != deficit.CounterpartyId
            || correction.CurrencyId != deficit.CurrencyId
            || !string.Equals(
                correction.CurrencyCode,
                deficit.CurrencyCode,
                StringComparison.Ordinal))
        {
            throw new TreasuryManualDeficitCorrectionUnavailableException();
        }
    }

    private static ManualOverpaymentDeficitCorrectionProposal ToProposal(
        StoredManualOverpaymentDeficitCorrection correction)
    {
        if (correction.CorrectionId == Guid.Empty
            || correction.DeficitId == Guid.Empty
            || correction.OrganizationId == Guid.Empty
            || correction.CounterpartyId == Guid.Empty
            || correction.CurrencyId == Guid.Empty
            || correction.ResponsibleAccountantId == Guid.Empty
            || correction.CreationCommandId == Guid.Empty
            || correction.FinancialDirectorId is not null
            || correction.DecidedAtUtc is not null
            || correction.DecisionComment is not null
            || correction.RejectionReason is not null
            || correction.DecisionCommandId is not null
            || !string.Equals(correction.State, PendingState, StringComparison.Ordinal))
        {
            throw new TreasuryManualDeficitCorrectionUnavailableException();
        }
        return new ManualOverpaymentDeficitCorrectionProposal(
            correction.CorrectionId,
            correction.DeficitId,
            new OverpaymentScope(
                correction.OrganizationId,
                correction.CounterpartyId,
                correction.CurrencyCode),
            correction.DeficitAmountAtCreation,
            correction.DeficitAmountKztAtCreation,
            correction.CorrectionAmount,
            correction.CorrectionAmountKzt,
            correction.Basis,
            correction.ResponsibleAccountantId,
            ManualOverpaymentDeficitCorrectionState.PendingApproval);
    }

    private static StoredManualOverpaymentDeficitCorrection ToStored(
        ManualOverpaymentDeficitCorrectionProposal proposal,
        Guid currencyId,
        Guid commandId) =>
        new(
            proposal.CorrectionId,
            proposal.DeficitId,
            proposal.Scope.OrganizationId,
            proposal.Scope.CounterpartyId,
            currencyId,
            proposal.Scope.CurrencyCode,
            proposal.DeficitAmountAtCreation,
            proposal.DeficitAmountKztAtCreation,
            proposal.CorrectionAmount,
            proposal.CorrectionAmountKzt,
            proposal.Basis,
            proposal.ResponsibleAccountantId,
            null,
            proposal.State.ToString(),
            null,
            null,
            null,
            commandId,
            null);

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

    private static void ValidateStoredDeficit(StoredOverpaymentDeficit deficit)
    {
        if (deficit.DeficitId == Guid.Empty
            || deficit.OrganizationId == Guid.Empty
            || deficit.CounterpartyId == Guid.Empty
            || deficit.CurrencyId == Guid.Empty
            || deficit.SourceOverpaymentId == Guid.Empty
            || deficit.CreationCommandId == Guid.Empty
            || deficit.SourceExpenseRequestId == Guid.Empty
            || deficit.ActualCorrectionExecutorId == Guid.Empty
            || deficit.CurrencyCode is not { Length: 3 }
            || deficit.CurrencyCode.Any(character => character is < 'A' or > 'Z')
            || string.IsNullOrWhiteSpace(deficit.ExternalReversalId)
            || !string.Equals(
                deficit.ExternalReversalId,
                deficit.ExternalReversalId.Trim(),
                StringComparison.Ordinal)
            || deficit.ExternalReversalId.Length > 150
            || deficit.ExternalReversalId.Any(char.IsControl)
            || deficit.OccurredAtUtc == default
            || deficit.OccurredAtUtc.Ticks % TimeSpan.TicksPerMillisecond != 0
            || deficit.SettledAtUtc is DateTimeOffset settledAtUtc
                && (settledAtUtc.Ticks % TimeSpan.TicksPerMillisecond != 0
                    || settledAtUtc < deficit.OccurredAtUtc))
        {
            throw new TreasuryManualDeficitCorrectionUnavailableException();
        }
        MoneyAmountPolicy.EnsurePositive(deficit.OriginalAmount, "Deficit original amount");
        MoneyAmountPolicy.EnsurePositive(deficit.OriginalAmountKzt, "Deficit original KZT amount");
        MoneyAmountPolicy.EnsureNonNegative(deficit.CurrentAmount, "Deficit current amount");
        MoneyAmountPolicy.EnsureNonNegative(deficit.CurrentAmountKzt, "Deficit current KZT amount");
        if (deficit.CurrentAmount > deficit.OriginalAmount
            || deficit.CurrentAmountKzt > deficit.OriginalAmountKzt
            || (deficit.CurrentAmount == 0m) != (deficit.CurrentAmountKzt == 0m)
            || (deficit.CurrentAmount == 0m) != (deficit.SettledAtUtc is not null)
            || deficit.CurrencyCode == "KZT"
                && (deficit.OriginalAmount != deficit.OriginalAmountKzt
                    || deficit.CurrentAmount != deficit.CurrentAmountKzt))
        {
            throw new TreasuryManualDeficitCorrectionUnavailableException();
        }
    }

    private static void ValidateCreateCommand(
        TreasuryManualDeficitCorrectionCreateCommand command)
    {
        if (command.CommandId == Guid.Empty
            || command.CorrectionId == Guid.Empty
            || command.DeficitId == Guid.Empty
            || command.ResponsibleAccountantId == Guid.Empty)
        {
            throw new TreasuryManualDeficitCorrectionUnavailableException();
        }
        MoneyAmountPolicy.EnsurePositive(command.CorrectionAmount, "Manual correction amount");
        if (string.IsNullOrWhiteSpace(command.Basis) || command.Basis.Length > 1000)
        {
            throw new TreasuryManualDeficitCorrectionUnavailableException();
        }
    }

    private static void ValidateDecisionCommand(
        TreasuryManualDeficitCorrectionDecisionCommand command)
    {
        if (command.CommandId == Guid.Empty
            || command.CorrectionId == Guid.Empty
            || command.FinancialDirectorId == Guid.Empty
            || command.Kind is not TreasuryManualDeficitCorrectionDecisionKind.Approve
                and not TreasuryManualDeficitCorrectionDecisionKind.Reject
            || command.DecisionComment is { Length: > 1000 }
            || command.RejectionReason is { Length: > 1000 })
        {
            throw new TreasuryManualDeficitCorrectionUnavailableException();
        }
        if (command.Kind == TreasuryManualDeficitCorrectionDecisionKind.Approve
                && command.RejectionReason is not null
            || command.Kind == TreasuryManualDeficitCorrectionDecisionKind.Reject
                && string.IsNullOrWhiteSpace(command.RejectionReason))
        {
            throw new TreasuryManualDeficitCorrectionUnavailableException();
        }
    }

    private static void EnsureSingleCleanWrite(
        int affected,
        ITreasuryManualDeficitCorrectionRuntime runtime)
    {
        if (affected != 1 || runtime.HasPendingChanges)
        {
            throw new TreasuryManualDeficitCorrectionUnavailableException();
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
            throw new TreasuryManualDeficitCorrectionUnavailableException(exception);
        }
    }

    private static void EnsureAttemptReady(
        ITreasuryManualDeficitCorrectionRuntime runtime)
    {
        if (!runtime.HasSerializableTransaction || runtime.HasPendingChanges)
        {
            throw new TreasuryManualDeficitCorrectionUnavailableException();
        }
    }
}
