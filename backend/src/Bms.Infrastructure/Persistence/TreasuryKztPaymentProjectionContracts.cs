using Bms.Domain.ExpenseRequests;

namespace Bms.Infrastructure.Persistence;

internal sealed record StoredPaymentProjectionRequest(
    Guid ExpenseRequestId,
    ExpenseRequestStatus Status,
    string CurrencyCode,
    Guid ConcurrencyStamp,
    int? ActiveApprovalRouteSnapshotNumber,
    int ActiveApprovalRouteStageNumber,
    DateOnly BudgetMonth,
    DateTimeOffset UpdatedAtUtc);

internal sealed record StoredPaymentProjectionLine(
    Guid LineId,
    int SortOrder,
    decimal Amount,
    decimal? AmountKzt,
    Guid CostCenterId,
    Guid CashFlowItemId);

internal sealed record StoredPaymentBudgetProjection(
    Guid ExpenseRequestLineId,
    Guid CostCenterId,
    Guid CashFlowItemId,
    DateOnly BudgetMonth,
    decimal ReservedAmountKzt);

internal sealed record StoredPaymentBudgetPosition(
    Guid CostCenterId,
    Guid CashFlowItemId,
    DateOnly BudgetMonth,
    decimal ReservedAmountKzt,
    decimal PaidAmountKzt,
    Guid ConcurrencyStamp);

internal sealed record TreasuryKztPaymentProjectionResult(
    ExpenseRequestStatus PreviousStatus,
    ExpenseRequestStatus AppliedStatus,
    decimal ReleasedReserveKzt,
    decimal RemainingReserveKzt,
    int AffectedEntries);

/// <summary>
/// Every outward failure of the payment projection carries this fixed text.
/// SQL state, server names, table names and stack traces stay inside.
/// </summary>
internal sealed class TreasuryKztPaymentProjectionUnavailableException : Exception
{
    private const string SafeMessage =
        "The confirmed payment projection could not be applied consistently.";

    internal TreasuryKztPaymentProjectionUnavailableException()
        : base(SafeMessage)
    {
    }

    internal TreasuryKztPaymentProjectionUnavailableException(Exception innerException)
        : base(
            SafeMessage,
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}

internal interface ITreasuryKztPaymentProjectionRuntime
{
    bool HasSerializableTransaction { get; }

    bool HasPendingChanges { get; }

    Task AcquireExclusiveTransactionLockAsync(
        string resource,
        CancellationToken cancellationToken);

    Task<StoredPaymentProjectionRequest?> FindRequestAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredPaymentProjectionLine>> FindLinesAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredPaymentBudgetProjection>> FindBudgetProjectionsAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken);

    Task<StoredPaymentBudgetPosition?> FindBudgetPositionAsync(
        Guid costCenterId,
        Guid cashFlowItemId,
        DateOnly budgetMonth,
        CancellationToken cancellationToken);

    Task<int> UpdateRequestStatusAsync(
        Guid expenseRequestId,
        Guid expectedConcurrencyStamp,
        ExpenseRequestStatus targetStatus,
        Guid nextConcurrencyStamp,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken);

    Task<int> ReplaceBudgetProjectionAsync(
        Guid expenseRequestId,
        Guid expenseRequestLineId,
        Guid costCenterId,
        Guid cashFlowItemId,
        DateOnly budgetMonth,
        decimal reservedAmountKzt,
        CancellationToken cancellationToken);

    Task<int> DeleteBudgetProjectionAsync(
        Guid expenseRequestId,
        Guid expenseRequestLineId,
        CancellationToken cancellationToken);

    Task<int> UpdateBudgetPositionAsync(
        Guid costCenterId,
        Guid cashFlowItemId,
        DateOnly budgetMonth,
        Guid expectedConcurrencyStamp,
        decimal reservedAmountKzt,
        decimal paidAmountKzt,
        Guid nextConcurrencyStamp,
        CancellationToken cancellationToken);
}
