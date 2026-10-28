namespace Bms.Infrastructure.Persistence;

internal sealed record TreasuryOverpaymentReleaseCommand(
    Guid CommandId,
    Guid TargetExpenseRequestId,
    Guid UserId);

internal sealed record TreasuryOverpaymentReleaseResult(
    decimal ReleasedAmount,
    decimal ReleasedAmountKzt,
    IReadOnlyList<StoredOverpaymentAllocation> ReleasedAllocations,
    int AffectedEntries);

internal sealed class TreasuryOverpaymentReleaseUnavailableException : Exception
{
    private const string SafeMessage =
        "The overpayment reservation could not be released consistently.";

    internal TreasuryOverpaymentReleaseUnavailableException()
        : base(SafeMessage)
    {
    }

    internal TreasuryOverpaymentReleaseUnavailableException(Exception innerException)
        : base(
            SafeMessage,
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}

internal interface ITreasuryOverpaymentReleaseRuntime
{
    bool HasSerializableTransaction { get; }

    bool HasPendingChanges { get; }

    Task AcquireExclusiveTransactionLockAsync(
        string resource,
        CancellationToken cancellationToken);

    Task<StoredOverpaymentScope?> FindTargetScopeAsync(
        Guid targetExpenseRequestId,
        CancellationToken cancellationToken);

    Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredOverpaymentAllocation>> FindAllocationsByCommandAsync(
        Guid commandId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredOverpaymentAllocation>> FindReservedAllocationsAsync(
        Guid targetExpenseRequestId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredOverpaymentSource>> FindSourcesAsync(
        IReadOnlyCollection<Guid> sourceIds,
        CancellationToken cancellationToken);

    Task<int> ReleaseSourceAsync(
        StoredOverpaymentSource expectedSource,
        decimal amount,
        decimal amountKzt,
        CancellationToken cancellationToken);

    Task<int> MarkAllocationReleasedAsync(
        StoredOverpaymentAllocation expectedAllocation,
        Guid commandId,
        Guid userId,
        DateTimeOffset releasedAtUtc,
        CancellationToken cancellationToken);

    Task<StoredOverpaymentAllocation?> FindAllocationByIdAsync(
        Guid allocationId,
        CancellationToken cancellationToken);

    Task<StoredOverpaymentSource?> FindSourceByIdAsync(
        Guid sourceId,
        CancellationToken cancellationToken);
}
