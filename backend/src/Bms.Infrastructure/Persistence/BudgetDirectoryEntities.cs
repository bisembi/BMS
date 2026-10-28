namespace Bms.Infrastructure.Persistence;

// Budget-document directory only. These rows neither import nor replace expense
// owner assignments, employee roles or dated organization/CFO access grants.
public sealed class BudgetCostCenterSettings
{
    public Guid CostCenterId { get; set; }
    public string? Abbreviation { get; set; }
    public Guid ConcurrencyStamp { get; set; }
}

public sealed class CostCenterPurpose457
{
    public Guid CostCenterId { get; set; }
    public bool OnlyCostDepartment { get; set; }
    public Guid ConcurrencyStamp { get; set; }
}

public sealed class BudgetPrefixOwner
{
    public string CanonicalPrefix { get; set; } = string.Empty;
    public Guid CostCenterId { get; set; }
}

// Absence means uninitialized, not zero; the transactional writer must establish
// an explicitly justified starting point without adopting legacy document numbers.
public sealed class BudgetCostCenterCounter
{
    public Guid CostCenterId { get; set; }
    public long LastIssuedOrdinal { get; set; }
}

public sealed class BudgetCostCenterMembership
{
    public Guid EmployeeId { get; set; }
    public Guid CostCenterId { get; set; }
    public string MembershipKind { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
