namespace Bms.Domain.ExpenseRequests;

/// <summary>Every supported approval split must also be payable in full.</summary>
public static class ExpenseRequestAllocationPolicy
{
    public const int MaximumLines = 10_000;
}
