using Bms.Domain.Common;

namespace Bms.Domain.Budgeting;

public readonly record struct BudgetKey
{
    private BudgetKey(Guid costCenterId, Guid cashFlowItemId, DateOnly budgetMonth)
    {
        CostCenterId = costCenterId;
        CashFlowItemId = cashFlowItemId;
        BudgetMonth = budgetMonth;
    }

    public Guid CostCenterId { get; }

    public Guid CashFlowItemId { get; }

    public DateOnly BudgetMonth { get; }

    public static BudgetKey Create(
        Guid costCenterId,
        Guid cashFlowItemId,
        DateOnly budgetMonth)
    {
        if (costCenterId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Cost center is required for a budget key.");
        }

        if (cashFlowItemId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Cash-flow item is required for a budget key.");
        }

        return new BudgetKey(
            costCenterId,
            cashFlowItemId,
            new DateOnly(budgetMonth.Year, budgetMonth.Month, 1));
    }
}
