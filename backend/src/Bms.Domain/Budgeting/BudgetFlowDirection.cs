namespace Bms.Domain.Budgeting;

/// <summary>
/// Direction of one planned cash-flow line. Stored amounts are always
/// non-negative; direction supplies the sign for cash-flow reporting.
/// </summary>
public enum BudgetFlowDirection
{
    Payment = 0,
    Receipt = 1
}
