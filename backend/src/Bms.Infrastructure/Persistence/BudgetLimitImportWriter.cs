using Bms.Application.Abstractions;
using Bms.Application.Budgeting;
using Bms.Domain.Budgeting;

namespace Bms.Infrastructure.Persistence;

internal sealed record BudgetLimitImportWriteResult(
    BudgetLimitImportPlan Plan,
    int AffectedEntries,
    IReadOnlyList<BudgetLimitPositionSnapshot> FinalPositions);

internal sealed class BudgetLimitImportWriteUnavailableException : Exception
{
    private const string SafeMessage =
        "The budget-limit patch could not be stored consistently.";

    internal BudgetLimitImportWriteUnavailableException()
        : base(SafeMessage)
    {
    }

    internal BudgetLimitImportWriteUnavailableException(Exception innerException)
        : base(
            SafeMessage,
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}

internal interface IBudgetLimitImportWriteRuntime
{
    bool HasSerializableTransaction { get; }

    bool HasPendingChanges { get; }

    Task AcquireExclusiveTransactionLockAsync(
        string resource,
        CancellationToken cancellationToken);

    Task<bool> ReferencesExistAsync(
        IReadOnlyCollection<Guid> costCenterIds,
        IReadOnlyCollection<Guid> cashFlowItemIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BudgetLimitPositionSnapshot>> ReadPositionsForUpdateAsync(
        IReadOnlyCollection<BudgetKey> keys,
        CancellationToken cancellationToken);

    Task<int> InsertAsync(
        BudgetLimitImportItem item,
        CancellationToken cancellationToken);

    Task<int> SetLimitAsync(
        BudgetLimitImportItem item,
        Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken);
}

/// <summary>
/// Dormant inner stage for an explicit patch produced by a future controlled
/// budget-limit source. It owns neither the outer transaction nor source,
/// authority, idempotency, audit or API decisions.
/// </summary>
internal static class BudgetLimitImportWriter
{
    internal static async Task<BudgetLimitImportWriteResult> WriteAsync(
        BudgetLimitImportBatch batch,
        IBudgetLimitImportWriteRuntime runtime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(runtime);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);

            var keys = batch.Items.Select(item => item.Key).ToArray();
            if (keys.Length == 0)
            {
                return new BudgetLimitImportWriteResult(
                    BudgetLimitImportPlanBuilder.Build(batch, []),
                    0,
                    []);
            }

            foreach (var resource in BusinessManagedLockResources.Order(
                         keys.Select(BusinessManagedLockResources.Budget)))
            {
                await runtime.AcquireExclusiveTransactionLockAsync(
                    resource,
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);
            var referencesExist = await runtime.ReferencesExistAsync(
                keys.Select(key => key.CostCenterId).Distinct().ToArray(),
                keys.Select(key => key.CashFlowItemId).Distinct().ToArray(),
                cancellationToken);
            if (!referencesExist)
            {
                throw new BudgetLimitImportWriteUnavailableException();
            }

            var current = await runtime.ReadPositionsForUpdateAsync(keys, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);
            var plan = BudgetLimitImportPlanBuilder.Build(batch, current);

            var affectedEntries = 0;
            foreach (var assignment in plan.Assignments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var affected = assignment.Operation switch
                {
                    BudgetLimitImportOperation.Insert =>
                        await runtime.InsertAsync(assignment.Item, cancellationToken),
                    BudgetLimitImportOperation.Update or BudgetLimitImportOperation.Clear =>
                        await runtime.SetLimitAsync(
                            assignment.Item,
                            RequireCurrentConcurrencyStamp(assignment),
                            cancellationToken),
                    BudgetLimitImportOperation.Unchanged => 0,
                    _ => throw new BudgetLimitImportWriteUnavailableException()
                };

                if (affected != (assignment.Operation == BudgetLimitImportOperation.Unchanged
                        ? 0
                        : 1)
                    || runtime.HasPendingChanges
                    || !runtime.HasSerializableTransaction)
                {
                    throw new BudgetLimitImportWriteUnavailableException();
                }

                affectedEntries += affected;
            }

            if (affectedEntries != plan.ChangeCount)
            {
                throw new BudgetLimitImportWriteUnavailableException();
            }

            var finalPositions = await runtime.ReadPositionsForUpdateAsync(keys, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAttemptReady(runtime);
            VerifyFinalState(plan, finalPositions);
            return new BudgetLimitImportWriteResult(
                plan,
                affectedEntries,
                Array.AsReadOnly(finalPositions.ToArray()));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BudgetLimitImportWriteUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
            when (!ProcessFatalExceptionClassifier.IsFatal(exception))
        {
            throw new BudgetLimitImportWriteUnavailableException(exception);
        }
    }

    private static void EnsureAttemptReady(IBudgetLimitImportWriteRuntime runtime)
    {
        if (!runtime.HasSerializableTransaction || runtime.HasPendingChanges)
        {
            throw new BudgetLimitImportWriteUnavailableException();
        }
    }

    private static Guid RequireCurrentConcurrencyStamp(
        BudgetLimitImportAssignment assignment) =>
        assignment.Current?.ConcurrencyStamp
        ?? throw new BudgetLimitImportWriteUnavailableException();

    private static void VerifyFinalState(
        BudgetLimitImportPlan plan,
        IReadOnlyCollection<BudgetLimitPositionSnapshot> finalPositions)
    {
        var finalByKey = finalPositions.ToDictionary(position => position.Key);
        foreach (var assignment in plan.Assignments)
        {
            if (!finalByKey.TryGetValue(assignment.Item.Key, out var final))
            {
                if (assignment.Operation == BudgetLimitImportOperation.Unchanged
                    && assignment.Current is null
                    && !assignment.Item.LimitAmountKzt.HasValue)
                {
                    continue;
                }

                throw new BudgetLimitImportWriteUnavailableException();
            }

            if (final.LimitAmountKzt != assignment.Item.LimitAmountKzt)
            {
                throw new BudgetLimitImportWriteUnavailableException();
            }

            if (assignment.Current is null)
            {
                if (final.ReservedAmountKzt != 0m
                    || final.PaidAmountKzt != 0m
                    || final.ConcurrencyStamp == Guid.Empty)
                {
                    throw new BudgetLimitImportWriteUnavailableException();
                }
                continue;
            }

            if (final.ReservedAmountKzt != assignment.Current.ReservedAmountKzt
                || final.PaidAmountKzt != assignment.Current.PaidAmountKzt)
            {
                throw new BudgetLimitImportWriteUnavailableException();
            }

            var shouldChangeStamp = assignment.Operation is
                BudgetLimitImportOperation.Update or BudgetLimitImportOperation.Clear;
            if ((final.ConcurrencyStamp != assignment.Current.ConcurrencyStamp)
                != shouldChangeStamp)
            {
                throw new BudgetLimitImportWriteUnavailableException();
            }
        }

        var expectedExistingKeys = plan.Assignments
            .Where(assignment => assignment.Current is not null
                || assignment.Item.LimitAmountKzt.HasValue)
            .Select(assignment => assignment.Item.Key)
            .ToHashSet();
        if (!finalByKey.Keys.ToHashSet().SetEquals(expectedExistingKeys))
        {
            throw new BudgetLimitImportWriteUnavailableException();
        }
    }
}
