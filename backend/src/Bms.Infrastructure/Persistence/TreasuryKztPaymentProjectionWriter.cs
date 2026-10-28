using Bms.Domain.Budgeting;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Payments;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Internal inner write stage that applies an already-computed W444 plan to the
/// request status and the budget projections. It neither opens nor commits a
/// transaction: the caller must provide a Serializable one and own audit,
/// outbox and any overpayment effects.
///
/// The plan is absolute state, so this writer performs a net replacement
/// rather than an accumulating delta. Replaying the same confirmed ledger is
/// therefore a no-op, and a reversal restores reserve and lowers the paid
/// amount by recomputing from the same snapshots.
/// </summary>
internal static class TreasuryKztPaymentProjectionWriter
{
    internal static Task<TreasuryKztPaymentProjectionResult> ApplyAsync(
        KztPaymentApplicationPlan plan,
        DateTimeOffset appliedAtUtc,
        ITreasuryKztPaymentProjectionRuntime runtime,
        CancellationToken cancellationToken) => ApplyCoreAsync(plan, "KZT", appliedAtUtc, runtime, cancellationToken);

    // The accounting intake never allocates excess foreign cash. The domain
    // calculator validates paired native/original-reserve amounts before this
    // internal adapter is allowed to use the absolute projection writer.
    internal static Task<TreasuryKztPaymentProjectionResult> ApplySettlementAsync(
        Guid requestId, PaymentSettlementCalculation settlement, DateTimeOffset now,
        ITreasuryKztPaymentProjectionRuntime runtime, CancellationToken token)
    {
        var target = settlement.Progress switch
        {
            PaymentSettlementProgress.Unpaid => KztPaymentTargetStatus.ToPay,
            PaymentSettlementProgress.PartiallySettled => KztPaymentTargetStatus.PartiallyPaid,
            PaymentSettlementProgress.FullySettled => KztPaymentTargetStatus.Paid,
            _ => throw new TreasuryKztPaymentProjectionUnavailableException()
        };
        var plan = new KztPaymentApplicationPlan(requestId, target, settlement.Progress,
            settlement.OriginalReserveKzt, settlement.ActualSettledAmountKzt,
            settlement.ReleasedReserveKzt, 0m, settlement.ReleasedReserveKzt,
            settlement.RemainingReserveKzt, -settlement.ReleasedReserveKzt,
            settlement.ReleasedReserveKzt, true,
            settlement.Lines.Select(l => new KztPaymentLineApplication(l.LineId, l.SortOrder,
                l.OriginalAmountKzt, l.ReleasedReserveKzt, l.RemainingReserveKzt)).ToArray());
        return ApplyCoreAsync(plan, settlement.CurrencyCode, now, runtime, token);
    }

    private static async Task<TreasuryKztPaymentProjectionResult> ApplyCoreAsync(
        KztPaymentApplicationPlan plan, string expectedCurrency,
        DateTimeOffset appliedAtUtc, ITreasuryKztPaymentProjectionRuntime runtime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(runtime);

        try
        {
            ConfirmedPaymentEventContract.ValidateProcessingTime(appliedAtUtc);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);

            await runtime.AcquireExclusiveTransactionLockAsync(
                BusinessManagedLockResources.ExpenseRequest(plan.ExpenseRequestId),
                cancellationToken);

            var request = await runtime.FindRequestAsync(
                plan.ExpenseRequestId,
                cancellationToken);
            if (request is null
                || !string.Equals(request.CurrencyCode, expectedCurrency, StringComparison.Ordinal))
            {
                throw new TreasuryKztPaymentProjectionUnavailableException();
            }

            // A closed request is outside the payment projection entirely; the
            // reopen decision belongs to a user command, not to a bank event.
            if (request.Status is not (ExpenseRequestStatus.ToPay
                or ExpenseRequestStatus.PartiallyPaid
                or ExpenseRequestStatus.Paid))
            {
                throw new TreasuryKztPaymentProjectionUnavailableException();
            }

            // A payment status never coexists with an active approval route;
            // stored state that claims both is corrupt and must not be paid
            // against.
            if (request.ActiveApprovalRouteSnapshotNumber is not null
                || request.ActiveApprovalRouteStageNumber != 0)
            {
                throw new TreasuryKztPaymentProjectionUnavailableException();
            }

            var lines = await runtime.FindLinesAsync(plan.ExpenseRequestId, cancellationToken);
            EnsureLinesMatchPlan(plan, lines, expectedCurrency == "KZT");

            var projections = await runtime.FindBudgetProjectionsAsync(
                plan.ExpenseRequestId,
                cancellationToken);
            var remainingByLine = plan.Lines.ToDictionary(
                line => line.LineId,
                line => line.RemainingReserveKzt);
            if (projections.Any(projection =>
                    !remainingByLine.ContainsKey(projection.ExpenseRequestLineId)))
            {
                throw new TreasuryKztPaymentProjectionUnavailableException();
            }

            // A fully settled line has no projection row, so a later reversal
            // has to recreate one. The desired set is therefore derived from
            // the request lines and the plan, never from the rows that happen
            // to survive right now.
            var desired = lines
                .Select(line => new StoredPaymentBudgetProjection(
                    line.LineId,
                    line.CostCenterId,
                    line.CashFlowItemId,
                    request.BudgetMonth,
                    remainingByLine[line.LineId]))
                .ToArray();

            foreach (var resource in BudgetLockResources([.. projections, .. desired]))
            {
                await runtime.AcquireExclusiveTransactionLockAsync(resource, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);

            // Re-read under the settled locks and refuse if anything moved.
            var lockedRequest = await runtime.FindRequestAsync(
                plan.ExpenseRequestId,
                cancellationToken);
            var lockedProjections = await runtime.FindBudgetProjectionsAsync(
                plan.ExpenseRequestId,
                cancellationToken);
            if (lockedRequest != request
                || !HaveSameProjections(projections, lockedProjections))
            {
                throw new TreasuryKztPaymentProjectionUnavailableException();
            }

            var affectedEntries = 0;
            var targetStatus = ToStatus(plan.TargetStatus);
            var nextStamp = Guid.NewGuid();

            var budgetOutcome = await ApplyBudgetAsync(
                plan,
                lockedProjections,
                desired,
                runtime,
                cancellationToken);
            affectedEntries += budgetOutcome.AffectedEntries;

            // A second partial payment keeps the status but changes the money,
            // and a reader that trusts the version must observe that. The
            // request row therefore turns whenever the status or any budget
            // row moved; only an exact replay leaves the version untouched.
            var requestChanged = targetStatus != request.Status
                || budgetOutcome.AffectedEntries > 0;
            var expectedRequest = request;
            if (requestChanged)
            {
                if (appliedAtUtc < request.UpdatedAtUtc)
                {
                    throw new TreasuryKztPaymentProjectionUnavailableException();
                }

                var statusRows = await runtime.UpdateRequestStatusAsync(
                    plan.ExpenseRequestId,
                    request.ConcurrencyStamp,
                    targetStatus,
                    nextStamp,
                    appliedAtUtc,
                    cancellationToken);
                if (statusRows != 1)
                {
                    throw new TreasuryKztPaymentProjectionUnavailableException();
                }

                affectedEntries += statusRows;
                expectedRequest = request with
                {
                    Status = targetStatus,
                    ConcurrencyStamp = nextStamp,
                    UpdatedAtUtc = appliedAtUtc,
                };
            }

            cancellationToken.ThrowIfCancellationRequested();
            await VerifyPersistedAsync(
                plan,
                expectedRequest,
                remainingByLine,
                budgetOutcome.ExpectedPositions,
                runtime,
                cancellationToken);

            return new TreasuryKztPaymentProjectionResult(
                request.Status,
                targetStatus,
                plan.ReleasedReserveKzt,
                plan.RemainingReserveKzt,
                affectedEntries);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TreasuryKztPaymentProjectionUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TreasuryKztPaymentProjectionUnavailableException(exception);
        }
    }

    private sealed record BudgetApplicationOutcome(
        int AffectedEntries,
        IReadOnlyList<StoredPaymentBudgetPosition> ExpectedPositions);

    private static async Task<BudgetApplicationOutcome> ApplyBudgetAsync(
        KztPaymentApplicationPlan plan,
        IReadOnlyList<StoredPaymentBudgetProjection> projections,
        IReadOnlyList<StoredPaymentBudgetProjection> desired,
        ITreasuryKztPaymentProjectionRuntime runtime,
        CancellationToken cancellationToken)
    {
        var affectedEntries = 0;
        var expectedPositions = new List<StoredPaymentBudgetPosition>();
        var keys = projections
            .Concat(desired)
            .Select(projection => (
                projection.CostCenterId,
                projection.CashFlowItemId,
                projection.BudgetMonth))
            .Distinct()
            .OrderBy(key => key.CostCenterId.ToString("N"), StringComparer.Ordinal)
            .ThenBy(key => key.CashFlowItemId.ToString("N"), StringComparer.Ordinal)
            .ThenBy(key => key.BudgetMonth)
            .ToArray();

        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var position = await runtime.FindBudgetPositionAsync(
                key.CostCenterId,
                key.CashFlowItemId,
                key.BudgetMonth,
                cancellationToken);
            if (position is null)
            {
                throw new TreasuryKztPaymentProjectionUnavailableException();
            }

            var forKey = projections
                .Where(projection => projection.CostCenterId == key.CostCenterId
                    && projection.CashFlowItemId == key.CashFlowItemId
                    && projection.BudgetMonth == key.BudgetMonth)
                .ToArray();
            var storedTotal = Sum(forKey.Select(projection => projection.ReservedAmountKzt));
            var desiredTotal = Sum(desired
                .Where(projection => projection.CostCenterId == key.CostCenterId
                    && projection.CashFlowItemId == key.CashFlowItemId
                    && projection.BudgetMonth == key.BudgetMonth)
                .Select(projection => projection.ReservedAmountKzt));

            // Net replacement against the stored reserve of this request only:
            // other requests sharing the budget key keep their contribution.
            var nextReserved = position.ReservedAmountKzt - storedTotal + desiredTotal;
            var nextPaid = position.PaidAmountKzt + storedTotal - desiredTotal;
            if (nextReserved < 0m || nextPaid < 0m)
            {
                throw new TreasuryKztPaymentProjectionUnavailableException();
            }

            if (nextReserved != position.ReservedAmountKzt
                || nextPaid != position.PaidAmountKzt)
            {
                var nextPositionStamp = Guid.NewGuid();
                var positionRows = await runtime.UpdateBudgetPositionAsync(
                    key.CostCenterId,
                    key.CashFlowItemId,
                    key.BudgetMonth,
                    position.ConcurrencyStamp,
                    nextReserved,
                    nextPaid,
                    nextPositionStamp,
                    cancellationToken);
                if (positionRows != 1)
                {
                    throw new TreasuryKztPaymentProjectionUnavailableException();
                }

                affectedEntries += positionRows;
                expectedPositions.Add(position with
                {
                    ReservedAmountKzt = nextReserved,
                    PaidAmountKzt = nextPaid,
                    ConcurrencyStamp = nextPositionStamp,
                });
            }
            else
            {
                expectedPositions.Add(position);
            }
        }

        var storedByLine = projections.ToDictionary(
            item => item.ExpenseRequestLineId,
            item => item.ReservedAmountKzt);
        foreach (var target in desired
                     .OrderBy(item => item.ExpenseRequestLineId.ToString("N"), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stored = storedByLine.TryGetValue(
                target.ExpenseRequestLineId,
                out var current)
                ? current
                : 0m;
            if (target.ReservedAmountKzt == stored)
            {
                continue;
            }

            // The projection table refuses non-positive rows, so a fully
            // settled line drops its row instead of storing a zero, and a
            // reversal recreates it.
            var rows = target.ReservedAmountKzt == 0m
                ? await runtime.DeleteBudgetProjectionAsync(
                    plan.ExpenseRequestId,
                    target.ExpenseRequestLineId,
                    cancellationToken)
                : await runtime.ReplaceBudgetProjectionAsync(
                    plan.ExpenseRequestId,
                    target.ExpenseRequestLineId,
                    target.CostCenterId,
                    target.CashFlowItemId,
                    target.BudgetMonth,
                    target.ReservedAmountKzt,
                    cancellationToken);
            if (rows != 1)
            {
                throw new TreasuryKztPaymentProjectionUnavailableException();
            }

            affectedEntries += rows;
        }

        return new BudgetApplicationOutcome(
            affectedEntries,
            expectedPositions.AsReadOnly());
    }

    private static async Task VerifyPersistedAsync(
        KztPaymentApplicationPlan plan,
        StoredPaymentProjectionRequest expectedRequest,
        IReadOnlyDictionary<Guid, decimal> remainingByLine,
        IReadOnlyList<StoredPaymentBudgetPosition> expectedPositions,
        ITreasuryKztPaymentProjectionRuntime runtime,
        CancellationToken cancellationToken)
    {
        var persisted = await runtime.FindRequestAsync(
            plan.ExpenseRequestId,
            cancellationToken);
        if (persisted != expectedRequest)
        {
            throw new TreasuryKztPaymentProjectionUnavailableException();
        }

        foreach (var expectedPosition in expectedPositions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var persistedPosition = await runtime.FindBudgetPositionAsync(
                expectedPosition.CostCenterId,
                expectedPosition.CashFlowItemId,
                expectedPosition.BudgetMonth,
                cancellationToken);
            if (persistedPosition != expectedPosition)
            {
                throw new TreasuryKztPaymentProjectionUnavailableException();
            }
        }

        var persistedProjections = await runtime.FindBudgetProjectionsAsync(
            plan.ExpenseRequestId,
            cancellationToken);
        var expected = remainingByLine
            .Where(pair => pair.Value > 0m)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        if (persistedProjections.Count != expected.Count
            || persistedProjections.Any(projection =>
                !expected.TryGetValue(projection.ExpenseRequestLineId, out var amount)
                || amount != projection.ReservedAmountKzt))
        {
            throw new TreasuryKztPaymentProjectionUnavailableException();
        }

        if (!runtime.HasSerializableTransaction || runtime.HasPendingChanges)
        {
            throw new TreasuryKztPaymentProjectionUnavailableException();
        }
    }

    private static IReadOnlyList<string> BudgetLockResources(
        IReadOnlyList<StoredPaymentBudgetProjection> projections)
    {
        var resources = projections
            .Select(projection => BusinessManagedLockResources.Budget(
                BudgetKey.Create(
                    projection.CostCenterId,
                    projection.CashFlowItemId,
                    projection.BudgetMonth)))
            .ToArray();
        return resources.Length == 0
            ? []
            : BusinessManagedLockResources.Order(resources);
    }

    private static void EnsureLinesMatchPlan(
        KztPaymentApplicationPlan plan,
        IReadOnlyList<StoredPaymentProjectionLine> lines, bool isKzt)
    {
        if (lines.Count != plan.Lines.Count)
        {
            throw new TreasuryKztPaymentProjectionUnavailableException();
        }

        var planByLine = plan.Lines.ToDictionary(line => line.LineId);
        foreach (var line in lines)
        {
            if (!planByLine.TryGetValue(line.LineId, out var planned)
                || planned.SortOrder != line.SortOrder
                || planned.ObligationAmountKzt != (isKzt ? line.Amount : line.AmountKzt)
                || (isKzt && line.AmountKzt is { } amountKzt && amountKzt != line.Amount))
            {
                throw new TreasuryKztPaymentProjectionUnavailableException();
            }
        }
    }

    private static bool HaveSameProjections(
        IReadOnlyList<StoredPaymentBudgetProjection> left,
        IReadOnlyList<StoredPaymentBudgetProjection> right) =>
        left.Count == right.Count
        && left
            .OrderBy(item => item.ExpenseRequestLineId.ToString("N"), StringComparer.Ordinal)
            .SequenceEqual(right.OrderBy(
                item => item.ExpenseRequestLineId.ToString("N"),
                StringComparer.Ordinal));

    private static ExpenseRequestStatus ToStatus(KztPaymentTargetStatus target) => target switch
    {
        KztPaymentTargetStatus.ToPay => ExpenseRequestStatus.ToPay,
        KztPaymentTargetStatus.PartiallyPaid => ExpenseRequestStatus.PartiallyPaid,
        KztPaymentTargetStatus.Paid => ExpenseRequestStatus.Paid,
        _ => throw new TreasuryKztPaymentProjectionUnavailableException(),
    };

    private static decimal Sum(IEnumerable<decimal> amounts)
    {
        var total = 0m;
        foreach (var amount in amounts)
        {
            try
            {
                total += amount;
            }
            catch (OverflowException)
            {
                throw new TreasuryKztPaymentProjectionUnavailableException();
            }
        }

        return total;
    }

    private static void EnsureAttemptReady(ITreasuryKztPaymentProjectionRuntime runtime)
    {
        if (!runtime.HasSerializableTransaction || runtime.HasPendingChanges)
        {
            throw new TreasuryKztPaymentProjectionUnavailableException();
        }
    }
}
