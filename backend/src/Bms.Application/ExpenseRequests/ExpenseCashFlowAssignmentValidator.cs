using Bms.Application.Abstractions;
using Bms.Domain.Common;
namespace Bms.Application.ExpenseRequests;

public interface IExpenseCashFlowAssignmentValidator
{
    Task ValidateAsync(Guid? headerCostCenterId, IEnumerable<Guid?> articleIds, CancellationToken token);
}
public sealed class ExpenseCashFlowAssignmentValidator(IReferenceDataRepository references) : IExpenseCashFlowAssignmentValidator
{
    public async Task ValidateAsync(Guid? headerCostCenterId, IEnumerable<Guid?> articleIds, CancellationToken token)
    {
        var selected = articleIds.Where(id => id.HasValue && id != Guid.Empty).Select(id => id!.Value).Distinct().ToArray();
        // Incomplete drafts remain editable, but cannot pass submission readiness.
        if (headerCostCenterId is null || headerCostCenterId == Guid.Empty || selected.Length == 0) return;
        var articles = await references.ResolveCashFlowItemsAsync(selected, token);
        if (selected.Any(id => !articles.Any(article => article.Id == id && article.CostCenterId == headerCostCenterId)))
            throw new DomainRuleViolationException("Cash-flow articles must belong to the header CFO.",
                DomainRuleViolationReason.ExpenseDraftArticleCfoMismatch);
    }
}
