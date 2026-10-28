namespace Bms.Infrastructure.Persistence;

internal sealed record TreasuryOverpaymentReservationCommand(
    Guid CommandId,
    Guid TargetExpenseRequestId,
    decimal RequestedAmount,
    Guid UserId);

internal sealed record StoredOverpaymentScope(
    Guid OrganizationId,
    Guid CounterpartyId,
    Guid CurrencyId,
    string CurrencyCode);

internal sealed record StoredOverpaymentSource(
    Guid SourceId,
    Guid OrganizationId,
    Guid CounterpartyId,
    Guid CurrencyId,
    string CurrencyCode,
    DateTimeOffset OccurredAtUtc,
    decimal CurrentRemainingAmount,
    decimal CurrentRemainingAmountKzt,
    decimal ReservedAmount,
    decimal ReservedAmountKzt);

internal sealed record StoredOverpaymentAllocation(
    Guid TargetExpenseRequestId,
    Guid SourceId,
    Guid AllocationId,
    decimal Amount,
    decimal AmountKzt,
    string Status,
    Guid CommandId,
    DateTimeOffset ReservedAtUtc,
    DateTimeOffset? AppliedOrReleasedAtUtc,
    Guid UserId);

internal sealed record TreasuryOverpaymentReservationResult(
    decimal RequestedAmount,
    decimal SelectedAmount,
    decimal SelectedAmountKzt,
    decimal ShortfallAmount,
    IReadOnlyList<StoredOverpaymentAllocation> Allocations,
    int AffectedEntries);

internal sealed class TreasuryOverpaymentReservationUnavailableException : Exception
{
    private const string SafeMessage =
        "The overpayment reservation could not be stored consistently.";

    internal TreasuryOverpaymentReservationUnavailableException()
        : base(SafeMessage)
    {
    }

    internal TreasuryOverpaymentReservationUnavailableException(Exception innerException)
        : base(
            SafeMessage,
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}

internal interface ITreasuryOverpaymentReservationRuntime
{
    bool HasSerializableTransaction { get; }

    bool HasPendingChanges { get; }

    Guid CreateAllocationId();

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

    Task<bool> HasActiveDeficitAsync(
        StoredOverpaymentScope scope,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredOverpaymentSource>> FindSourcesAsync(
        StoredOverpaymentScope scope,
        CancellationToken cancellationToken);

    Task<int> ReserveSourceAsync(
        StoredOverpaymentSource expectedSource,
        decimal amount,
        decimal amountKzt,
        CancellationToken cancellationToken);

    Task<int> InsertAllocationAsync(
        StoredOverpaymentAllocation allocation,
        CancellationToken cancellationToken);

    Task<StoredOverpaymentAllocation?> FindAllocationByIdAsync(
        Guid allocationId,
        CancellationToken cancellationToken);

    Task<StoredOverpaymentSource?> FindSourceByIdAsync(
        Guid sourceId,
        CancellationToken cancellationToken);
}
