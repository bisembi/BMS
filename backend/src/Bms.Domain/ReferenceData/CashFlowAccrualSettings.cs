using Bms.Domain.Common;

namespace Bms.Domain.ReferenceData;

/// <summary>One article owns one pair; disabling the feature clears the mapping.</summary>
public sealed record CashFlowAccrualSettings
{
    public bool FormsManagerialAccrual { get; }
    public Guid? ExpenseItemId { get; }
    public Guid? CostCenterId { get; }

    public CashFlowAccrualSettings(bool formsManagerialAccrual, Guid? expenseItemId, Guid? costCenterId = null)
    {
        if (formsManagerialAccrual
                ? expenseItemId is null || expenseItemId == Guid.Empty
                : expenseItemId is not null)
            throw new DomainRuleViolationException("The accrual settings pair is invalid.");
        FormsManagerialAccrual = formsManagerialAccrual;
        ExpenseItemId = expenseItemId;
        if (costCenterId == Guid.Empty)
            throw new DomainRuleViolationException("The article cost center is invalid.");
        CostCenterId = costCenterId;
    }
}
