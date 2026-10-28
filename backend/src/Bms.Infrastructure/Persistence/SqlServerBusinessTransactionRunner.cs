using System.Data;
using System.Runtime.ExceptionServices;
using Bms.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class SqlServerBusinessTransactionRunner : IBusinessTransactionRunner
{
    private readonly IBusinessTransactionRuntime runtime;

    public SqlServerBusinessTransactionRunner(BmsDbContext dbContext, IBudgetExecutionTransactionIntent343? intent = null)
        : this(new EfCoreBusinessTransactionRuntime(dbContext, intent))
    {
    }

    internal SqlServerBusinessTransactionRunner(IBusinessTransactionRuntime runtime)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken) => ExecuteCoreAsync(action, false, false, cancellationToken);

    public Task<TResult> ExecuteOwnerAssignmentMutationAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action, CancellationToken cancellationToken) =>
        ExecuteCoreAsync(action, false, true, cancellationToken);

    public Task<TResult> ExecuteCompanyBudgetMutationAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action, CancellationToken cancellationToken) =>
        ExecuteCoreAsync(action, true, false, cancellationToken);

    private async Task<TResult> ExecuteCoreAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action, bool companyBudgetMutation, bool ownerAssignmentMutation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (runtime.HasCurrentTransaction)
        {
            throw new InvalidOperationException(
                "A BMS business transaction cannot be nested inside another database transaction.");
        }

        try
        {
            return await runtime.ExecuteWithRetryAsync(
                strategyCancellationToken => ExecuteAttemptAsync(action, companyBudgetMutation, ownerAssignmentMutation, strategyCancellationToken),
                cancellationToken);
        }
        catch (CommittedBusinessTransactionCleanupException exception)
            when (!ProcessFatalExceptionClassifier.IsFatal(exception.Failure))
        {
            // The action is already committed. The internal attempt marker kept a
            // provider-like cleanup failure away from the execution strategy; now
            // preserve the committed outcome for application orchestration.
            throw new BusinessTransactionCommittedException(exception.Failure);
        }
        catch (CommittedBusinessTransactionCleanupException exception)
        {
            // Process-fatal failures preserve their original type and stack.
            ExceptionDispatchInfo.Capture(exception.Failure).Throw();
            throw new InvalidOperationException("Unreachable post-commit cleanup failure path.");
        }
    }

    private async Task<TResult> ExecuteAttemptAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        bool companyBudgetMutation,
        bool ownerAssignmentMutation,
        CancellationToken cancellationToken)
    {
        var transaction = await runtime.BeginSerializableTransactionAsync(
            cancellationToken);
        var committed = false;
        try
        {
            // Always first: an activation cannot invert the request/CFO/budget
            // lock order. Exclusive mutations never upgrade a retained Shared lock.
            await runtime.AcquireCompanyBudgetLockAsync(companyBudgetMutation, cancellationToken);
            await runtime.ValidateStaffSubstitutionAsync(cancellationToken);
            // Owner writers serialize before any employee/CFO range locks, never
            // upgrade a retained lock and do not serialize unrelated business reads.
            if (ownerAssignmentMutation)
                await runtime.AcquireOwnerAssignmentLockAsync(cancellationToken);
            var result = await action(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            await transaction.DisposeAsync();
            return result;
        }
        catch (Exception failure)
        {
            Exception? rollbackFailure = null;
            Exception? cleanupFailure = null;
            Exception? disposeFailure = null;
            if (!committed)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch (Exception exception)
                {
                    rollbackFailure = exception;
                }
            }

            try
            {
                // A retry must not reuse tracked state produced by the failed attempt.
                runtime.ClearTrackedState();
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }

            if (!committed)
            {
                try
                {
                    await transaction.DisposeAsync();
                }
                catch (Exception exception)
                {
                    disposeFailure = exception;
                }
            }

            Exception failureToRethrow = failure;
            if (rollbackFailure is not null ||
                cleanupFailure is not null ||
                disposeFailure is not null)
            {
                var failures = new List<Exception> { failure };
                if (rollbackFailure is not null)
                {
                    failures.Add(rollbackFailure);
                }

                if (cleanupFailure is not null)
                {
                    failures.Add(cleanupFailure);
                }

                if (disposeFailure is not null)
                {
                    failures.Add(disposeFailure);
                }

                failureToRethrow = new AggregateException(
                    "The BMS business transaction failed and cleanup did not complete.",
                    failures);
            }

            if (committed)
            {
                throw new CommittedBusinessTransactionCleanupException(failureToRethrow);
            }

            ExceptionDispatchInfo.Capture(failureToRethrow).Throw();
            throw new InvalidOperationException("Unreachable transaction failure path.");
        }
    }
}

internal sealed class CommittedBusinessTransactionCleanupException(Exception failure) : Exception
{
    internal Exception Failure { get; } =
        failure ?? throw new ArgumentNullException(nameof(failure));
}

internal interface IBusinessTransactionRuntime
{
    Task ValidateStaffSubstitutionAsync(CancellationToken token) => Task.CompletedTask;
    Task AcquireOwnerAssignmentLockAsync(CancellationToken token) => Task.CompletedTask;
    Task AcquireCompanyBudgetLockAsync(bool exclusive, CancellationToken token) => Task.CompletedTask;
    bool HasCurrentTransaction { get; }

    Task<TResult> ExecuteWithRetryAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken);

    Task<IBusinessTransactionHandle> BeginSerializableTransactionAsync(
        CancellationToken cancellationToken);

    void ClearTrackedState();
}

internal interface IBusinessTransactionHandle : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}

internal sealed class EfCoreBusinessTransactionRuntime(BmsDbContext dbContext, IBudgetExecutionTransactionIntent343? intent = null) :
    IBusinessTransactionRuntime
{
    public Task ValidateStaffSubstitutionAsync(CancellationToken token) => StaffSubstitutionGuard471.ValidateAsync(dbContext, token);
    public Task AcquireOwnerAssignmentLockAsync(CancellationToken token) =>
        BudgetOwnerRoleSynchronizer.AcquireLockAsync(dbContext, token);
    public Task AcquireCompanyBudgetLockAsync(bool exclusive, CancellationToken token) =>
        CompanyBudgetPlanReader.AcquireLockAsync(dbContext, exclusive, token);
    private readonly BmsDbContext dbContext =
        dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    public bool HasCurrentTransaction => dbContext.Database.CurrentTransaction is not null;

    public Task<TResult> ExecuteWithRetryAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return dbContext.Database.CreateExecutionStrategy().ExecuteAsync(
            operation,
            cancellationToken);
    }

    public async Task<IBusinessTransactionHandle> BeginSerializableTransactionAsync(
        CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            dbContext.BudgetRegisterReadOnly343=intent?.IsReadOnly??false;
            dbContext.BudgetRegisterFinancialWrites343=false;
            dbContext.BudgetRegisterLimitsOnly454=false;
            dbContext.BudgetRegisterTouchedRequests343.Clear();
            dbContext.BudgetRegisterEnabled343=await new BudgetExecutionStorage343(dbContext)
                .AcquireGateAsync(dbContext.BudgetRegisterReadOnly343,cancellationToken);
            return new EfCoreBusinessTransactionHandle(transaction,dbContext);
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            finally { await transaction.DisposeAsync(); }
            throw;
        }
    }

    public void ClearTrackedState()
    {
        dbContext.ChangeTracker.Clear();
        dbContext.BudgetRegisterFinancialWrites343=false;
        dbContext.BudgetRegisterLimitsOnly454=false;
        dbContext.BudgetRegisterTouchedRequests343.Clear();
    }
}

internal sealed class EfCoreBusinessTransactionHandle(IDbContextTransaction transaction, BmsDbContext? dbContext = null) :
    IBusinessTransactionHandle
{
    private readonly IDbContextTransaction transaction =
        transaction ?? throw new ArgumentNullException(nameof(transaction));

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if(dbContext is {BudgetRegisterEnabled343:true,BudgetRegisterFinancialWrites343:true})
        {
            if(dbContext.BudgetRegisterReadOnly343)throw new InvalidOperationException("Read-only register transaction cannot commit financial changes.");
            if(dbContext.BudgetRegisterLimitsOnly454)
                await new BudgetExecutionPosting343(dbContext).RecalculateActiveLimits454Async(cancellationToken);
            else await new BudgetExecutionPosting343(dbContext).SynchronizeAsync(false,cancellationToken);
        }
        if (dbContext is not null) await StaffSubstitutionGuard471.ValidateAsync(dbContext, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if(dbContext is not null){dbContext.BudgetRegisterFinancialWrites343=false;dbContext.BudgetRegisterLimitsOnly454=false;}
    }

    public Task RollbackAsync(CancellationToken cancellationToken) =>
        transaction.RollbackAsync(cancellationToken);

    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}
