using Bms.Domain.Common;
using Bms.Domain.Workflow;

namespace Bms.Domain.Budgeting;

/// <summary>Budget-only route. Expense-request policy remains independent.</summary>
public static class BudgetApprovalRoutePlanner
{
    public static IReadOnlyList<ApprovalRouteStage> Plan(decimal positiveExcessKzt)
    {
        MoneyAmountPolicy.EnsureNonNegative(positiveExcessKzt, "Budget approval excess");
        var stages = new List<ApprovalRouteStage>
        {
            new(1, ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode),
            new(2, ExpenseRequestWorkflowCodes.GeneralDirectorRoleCode),
        };
        return stages.AsReadOnly();
    }
}
