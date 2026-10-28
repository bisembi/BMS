using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// <summary>
/// The first executable workflow command. Its optimistic version is part of
/// the command body and must match the persisted draft before a route is
/// captured or an accrual snapshot is written.
/// </summary>
public sealed record SubmitExpenseRequestCommand(Guid ExpectedVersion);

/// <summary>
/// Body shared by the first executable workflow commands. A reason is
/// required by the verified matrix for RETURN, REJECT and CANCEL; the
/// service validates that rule rather than trusting the caller or UI.
/// </summary>
public sealed record ExpenseRequestWorkflowCommand(
    Guid ExpectedVersion,
    string? Reason = null,
    Guid? CounterpartyId = null,
    Guid? ContractId = null);

public sealed record ExpenseRequestWorkflowCommandResult(
    ExpenseRequestDto ExpenseRequest,
    bool IsReplay,
    int ResponseStatusCode);

/// <summary>
/// Safe read-only projection of one command from the approved workflow
/// matrix. It contains no route participants, employee identifiers or
/// provider diagnostics.
/// </summary>
public sealed record ExpenseRequestWorkflowCommandAvailabilityDto(
    string Code,
    string Label,
    bool RequiresReason,
    bool IsEnabled,
    bool RequiresNsiLinks);

public sealed record ExpenseRequestWorkflowCommandAvailabilityResult(
    string RequestVersion,
    IReadOnlyList<ExpenseRequestWorkflowCommandAvailabilityDto> Commands);

public sealed record SubmitExpenseRequestResult(
    ExpenseRequestDto ExpenseRequest,
    bool IsReplay,
    int ResponseStatusCode);

/// <summary>
/// Expected fail-closed outcome when authoritative data says a draft cannot
/// enter approval yet. The public HTTP boundary maps it to a fixed message.
/// </summary>
public sealed class ExpenseRequestSubmissionBlockedException()
    : Exception("The expense request cannot be submitted in its current state.");

public sealed class ExpenseRequestBudgetChangedException()
    : Exception("The active budget has changed and its limit is insufficient; return and resubmit the request.");

/// <summary>
/// Safe wrapper for unavailable workflow infrastructure or an inconsistent
/// persisted workflow definition. It never exposes a provider exception.
/// </summary>
public sealed class ExpenseRequestWorkflowUnavailableException : Exception
{
    public ExpenseRequestWorkflowUnavailableException()
        : base("The expense-request workflow is temporarily unavailable.")
    {
    }

    public ExpenseRequestWorkflowUnavailableException(Exception innerException)
        : base("The expense-request workflow is temporarily unavailable.", innerException)
    {
    }
}
