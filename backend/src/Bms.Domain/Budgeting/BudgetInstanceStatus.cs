namespace Bms.Domain.Budgeting;

/// <summary>
/// A draft can be edited; an approved document is an immutable historical
/// version. Selecting effective coverage belongs to the application/store,
/// and does not change an older approved document's status.
/// </summary>
public enum BudgetInstanceStatus
{
    Draft = 0,
    Approved = 1,
}
