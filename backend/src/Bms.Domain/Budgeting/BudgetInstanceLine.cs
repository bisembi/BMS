using Bms.Domain.Common;

namespace Bms.Domain.Budgeting;

/// <summary>
/// One planned limit line of a budget instance. The key is the global
/// group key without organization: cost centre (ЦФО) from the header,
/// cash-flow item, month start, and flow direction. Nothing here touches
/// BudgetPositions.
/// </summary>
public sealed class BudgetInstanceLine
{
    private BudgetInstanceLine()
    {
    }

    private BudgetInstanceLine(
        Guid id,
        Guid budgetInstanceId,
        Guid cashFlowItemId,
        DateOnly budgetMonth,
        decimal limitAmountKzt,
        BudgetFlowDirection flowDirection,
        int sortOrder,
        string? comment)
    {
        Id = id;
        BudgetInstanceId = budgetInstanceId;
        CashFlowItemId = cashFlowItemId;
        BudgetMonth = budgetMonth;
        LimitAmountKzt = limitAmountKzt;
        FlowDirection = flowDirection;
        SortOrder = sortOrder;
        Comment = comment;
    }

    public Guid Id { get; private set; }

    public Guid BudgetInstanceId { get; private set; }

    public Guid CashFlowItemId { get; private set; }

    public DateOnly BudgetMonth { get; private set; }

    public decimal LimitAmountKzt { get; private set; }

    public BudgetFlowDirection FlowDirection { get; private set; }

    public int SortOrder { get; private set; }

    public string? Comment { get; private set; }

    internal static BudgetInstanceLine Create(
        Guid budgetInstanceId,
        Guid cashFlowItemId,
        DateOnly budgetMonth,
        decimal limitAmountKzt,
        BudgetFlowDirection flowDirection,
        int sortOrder,
        string? comment = null)
    {
        if (budgetInstanceId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Budget instance id is required.");
        }

        if (cashFlowItemId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Cash-flow item is required on a budget line.");
        }

        if (budgetMonth.Day != 1)
        {
            throw new DomainRuleViolationException("Budget month must be the first day of the month.");
        }

        MoneyAmountPolicy.EnsureNonNegative(limitAmountKzt, "Budget line limit");
        MoneyAmountPolicy.EnsureRepresentable(limitAmountKzt, "Budget line limit");

        if (!Enum.IsDefined(flowDirection))
        {
            throw new DomainRuleViolationException("Budget flow direction is invalid.");
        }

        if (sortOrder < 0)
        {
            throw new DomainRuleViolationException("Budget line sort order cannot be negative.");
        }

        var normalizedComment = NormalizeComment(comment);

        return new BudgetInstanceLine(
            Guid.NewGuid(),
            budgetInstanceId,
            cashFlowItemId,
            budgetMonth,
            limitAmountKzt,
            flowDirection,
            sortOrder,
            normalizedComment);
    }

    private static string? NormalizeComment(string? comment)
    {
        if (comment is null || comment.Length == 0)
        {
            return null;
        }

        if (comment.Length > 2000 || comment.Any(character =>
                char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
        {
            throw new DomainRuleViolationException("Budget line comment is invalid.");
        }

        var trimmed = comment.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
