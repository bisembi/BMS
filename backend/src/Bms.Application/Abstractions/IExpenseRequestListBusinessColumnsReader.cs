using Bms.Application.ExpenseRequests;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.Abstractions;

/// <summary>Reads one already authorized page inside its Serializable capture.
/// Only full-card-visible requests may be supplied; no new visibility is granted.</summary>
public interface IExpenseRequestListBusinessColumnsReader
{
    Task<IReadOnlyDictionary<Guid, ExpenseRequestListBusinessColumnsDto>> ReadAsync(
        IReadOnlyList<ExpenseRequest> requests, CancellationToken cancellationToken);
}
