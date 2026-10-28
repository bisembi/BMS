using Bms.Domain.Workflow;

namespace Bms.Application.ExpenseRequests;

public sealed record ExpenseRequestClosingLineAllocationCommand(
    Guid LineId,
    decimal Amount);

public sealed record CloseExpenseRequestPartiallyCommand(
    Guid ExpectedVersion,
    DateOnly DocumentsReceivedDate,
    decimal ControlAmount,
    string? Reason,
    IReadOnlyList<ExpenseRequestClosingLineAllocationCommand>? LineAllocations = null);

public sealed record CloseExpenseRequestFullyCommand(
    Guid ExpectedVersion,
    DateOnly DocumentsReceivedDate,
    string? Reason);

public sealed record ReopenExpenseRequestCommand(
    Guid ExpectedVersion,
    string? Reason);

public sealed record ExpenseRequestClosingLineDto(
    Guid LineId,
    int SortOrder,
    decimal OriginalAmount,
    decimal OriginalAmountKzt,
    decimal ConfirmedClosedAmount,
    decimal ConfirmedClosedAmountKzt,
    decimal RemainingAmount,
    decimal RemainingAmountKzt);

/// <summary>
/// Safe public closing projection. The accrual-cycle UUID and expense-item
/// snapshot stay server-side and are never returned to the ordinary UI.
/// </summary>
public sealed record ExpenseRequestClosingStateDto(
    Guid ExpenseRequestId,
    string Status,
    string CurrencyCode,
    DateTimeOffset? FullyPaidAtUtc,
    DateOnly? LatestDocumentsReceivedDate,
    decimal ConfirmedClosingControlAmount,
    bool ClosedPartially,
    bool CanExecuteClosingCommands,
    Guid ConcurrencyStamp,
    IReadOnlyList<ExpenseRequestClosingLineDto> Lines);

public sealed record ExpenseRequestClosingCommandResult(
    ExpenseRequestClosingStateDto Closing,
    bool IsReplay,
    int ResponseStatusCode);

public interface IExpenseRequestClosingService
{
    Task<ExpenseRequestClosingStateDto?> GetStateAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<ExpenseRequestClosingCommandResult> ClosePartiallyAsync(
        Guid id,
        CloseExpenseRequestPartiallyCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<ExpenseRequestClosingCommandResult> CloseFullyAsync(
        Guid id,
        CloseExpenseRequestFullyCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<ExpenseRequestClosingCommandResult> ReopenAsync(
        Guid id,
        ReopenExpenseRequestCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<ExpenseRequestClosingStateDto?> ResolveIdempotencyAsync(
        Guid id,
        string commandCode,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed class ExpenseRequestClosingBlockedException()
    : Exception("The expense request cannot be closed in its current state.");

public sealed class ExpenseRequestClosingUnavailableException : Exception
{
    public ExpenseRequestClosingUnavailableException()
        : base("Expense-request closing is temporarily unavailable.")
    {
    }

    public ExpenseRequestClosingUnavailableException(Exception innerException)
        : base("Expense-request closing is temporarily unavailable.", innerException)
    {
    }
}

internal static class ExpenseRequestClosingCommandCodes
{
    internal static readonly IReadOnlySet<string> All = new HashSet<string>(
        [
            ExpenseRequestWorkflowCodes.ClosePartialCommandCode,
            ExpenseRequestWorkflowCodes.CloseFullCommandCode,
            ExpenseRequestWorkflowCodes.ReopenCommandCode,
        ],
        StringComparer.Ordinal);
}
