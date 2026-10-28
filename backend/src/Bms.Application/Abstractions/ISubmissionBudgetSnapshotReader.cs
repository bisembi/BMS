using Bms.Domain.Budgeting;

namespace Bms.Application.Abstractions;

public interface ISubmissionBudgetSnapshotReader
{
    Task<SubmissionBudgetSnapshot> ReadAsync(
        Guid expenseRequestId,
        IReadOnlyCollection<BudgetRequestProjection> desiredProjections,
        CancellationToken cancellationToken);
}
