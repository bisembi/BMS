using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Domain.Budgeting;

/// <summary>The owning CFO remains a document/access dimension. Only an advance
/// consumes the separately server-resolved HR salary budget.</summary>
public static class ExpenseBudgetDimensions372
{
    public static Guid CostCenter(ExpenseRequest request, ExpenseRequestLine line) =>
        request.PaymentOperation is { IsPayrollAdvance: true } operation
            ? operation.BudgetCostCenterId ?? throw new DomainRuleViolationException("Advance HR budget is not resolved.")
            : line.CostCenterId ?? throw new DomainRuleViolationException("Budget CFO is missing.");
}
