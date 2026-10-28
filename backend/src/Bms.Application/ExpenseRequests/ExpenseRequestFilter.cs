using Bms.Application.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

public sealed record ExpenseRequestFilter(
    bool? IsUrgent = null,
    DateOnly? PlannedPaymentDate = null,
    Guid? OrganizationId = null,
    ExpenseRequestStatus? Status = null,
    Guid? CostCenterId = null,
    Guid? CounterpartyId = null,
    string? InitiatorExternalId = null,
    DateTimeOffset? CreatedFromUtc = null,
    DateTimeOffset? CreatedBeforeUtc = null,
    bool OnlyMyTasks = false)
{
    public string? SortBy { get; init; }
    public string? SortDirection { get; init; }
    public string? Search { get; init; }
    public Guid? InitiatorEmployeeId { get; init; }
    public bool HideDrafts { get; init; }

    internal void Validate()
    {
        if (InitiatorEmployeeId == Guid.Empty)
            throw new ArgumentException("An empty employee filter is invalid.", "initiatorEmployeeId");
        if (Search is not null && (Search.Length > 200 || Search != Search.Trim() || Search.Any(char.IsControl)))
            throw new ArgumentException("Invalid expense-request search.", "search");
        if (SortBy is not null && SortBy is not ("requestNumber" or "requestDate" or "plannedPaymentDate" or "organization"
            or "costCenter" or "counterparty" or "initiator" or "status" or "urgency"
            or "amount" or "amountKzt" or "budget")
            || SortDirection is not null && (SortBy is null || SortDirection is not ("asc" or "desc")))
            throw new ArgumentException("Invalid expense-request sort.");

        if (Status.HasValue && !Enum.IsDefined(Status.Value))
        {
            throw new ArgumentOutOfRangeException(
                "status",
                "Unknown expense-request status filter.");
        }

        if (InitiatorExternalId is not null && !ActorIdentity.IsCanonical(InitiatorExternalId))
        {
            throw new ArgumentException(
                "Expense-request initiator filter must be canonical.",
                "initiatorExternalId");
        }

        ExpenseRequestCreatedPeriodFilter.Validate(CreatedFromUtc, CreatedBeforeUtc);
    }
}
