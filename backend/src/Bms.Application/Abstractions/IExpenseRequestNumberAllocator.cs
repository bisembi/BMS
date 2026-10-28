namespace Bms.Application.Abstractions;

public interface IExpenseRequestNumberAllocator
{
    Task<string> AllocateAsync(Guid costCenterId, CancellationToken cancellationToken);
    Task AcquireAsync(CancellationToken token) => Task.CompletedTask;

    Task<ExpenseRequestNumberLease> ReserveAsync(Guid costCenterId, Guid leaseId, string actor, DateTimeOffset now,
        CancellationToken token) => throw new NotSupportedException();
    Task ReleaseAsync(Guid costCenterId, Guid leaseId, string actor, DateTimeOffset now,
        CancellationToken token) => throw new NotSupportedException();
    Task<string> ConsumeAsync(Guid costCenterId, Guid leaseId, string actor, DateTimeOffset now,
        CancellationToken token) => throw new NotSupportedException();
}

public sealed record ExpenseRequestNumberLease(Guid LeaseId, Guid CostCenterId, string Number, DateTimeOffset ExpiresAtUtc);
