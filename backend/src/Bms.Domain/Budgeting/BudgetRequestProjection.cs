using Bms.Domain.Common;

namespace Bms.Domain.Budgeting;

public sealed class BudgetRequestProjection
{
    private BudgetRequestProjection()
    {
    }

    private BudgetRequestProjection(
        Guid expenseRequestId,
        Guid expenseRequestLineId,
        BudgetKey key,
        decimal reservedAmountKzt)
    {
        if (expenseRequestId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Expense request is required for a budget projection.");
        }

        if (expenseRequestLineId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Expense request line is required for a budget projection.");
        }

        MoneyAmountPolicy.EnsurePositive(reservedAmountKzt, "Projected reserved amount");

        Id = expenseRequestLineId;
        ExpenseRequestId = expenseRequestId;
        ExpenseRequestLineId = expenseRequestLineId;
        CostCenterId = key.CostCenterId;
        CashFlowItemId = key.CashFlowItemId;
        BudgetMonth = key.BudgetMonth;
        ReservedAmountKzt = reservedAmountKzt;
    }

    public Guid Id { get; private set; }

    public Guid ExpenseRequestId { get; private set; }

    public Guid ExpenseRequestLineId { get; private set; }

    public Guid CostCenterId { get; private set; }

    public Guid CashFlowItemId { get; private set; }

    public DateOnly BudgetMonth { get; private set; }

    public decimal ReservedAmountKzt { get; private set; }

    public BudgetKey Key => BudgetKey.Create(CostCenterId, CashFlowItemId, BudgetMonth);

    public static BudgetRequestProjection Create(
        Guid expenseRequestId,
        Guid expenseRequestLineId,
        BudgetKey key,
        decimal reservedAmountKzt) =>
        new(expenseRequestId, expenseRequestLineId, key, reservedAmountKzt);
}
