namespace Bms.Application.Abstractions;

public interface IBusinessTransactionRunner
{
    Task<TResult> ExecuteOwnerAssignmentMutationAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action, CancellationToken cancellationToken) =>
        ExecuteAsync(action, cancellationToken);

    Task<TResult> ExecuteCompanyBudgetMutationAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action, CancellationToken cancellationToken) =>
        ExecuteAsync(action, cancellationToken);

    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken);
}
