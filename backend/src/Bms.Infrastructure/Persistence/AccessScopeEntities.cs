using Bms.Domain.ReferenceData;

namespace Bms.Infrastructure.Persistence;

public sealed class OrganizationReference
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class CostCenterReference
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? GroupingName { get; set; }
    public bool OwnerManagedLocally { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsGroup { get; set; }
    public bool IsActive { get; set; }
}

public sealed class CashFlowItemReference
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public string ActivityType { get; set; } = CashFlowActivityType.Operating;
    public string? FlowDirection { get; set; }
    public bool IsActive { get; set; }
    public bool IsHistoricalForActuals { get; set; }
    public bool IsGroup { get; set; }
    public bool FormsManagerialAccrual { get; set; }
    public Guid? AccrualExpenseItemId { get; set; }
    public Guid AccrualSettingsVersion { get; set; } = Guid.NewGuid();
    public Guid? CostCenterId { get; set; }
}

public sealed class ExpenseItemReference
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class CounterpartyReference
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Bin { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ContractReference
{
    public Guid Id { get; set; }
    public Guid CounterpartyId { get; set; }
    public Guid OrganizationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateOnly ContractDate { get; set; }
    public Guid CurrencyId { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ActorOrganizationGrant
{
    public string ActorExternalId { get; set; } = string.Empty;
    public Guid OrganizationId { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ActorCostCenterAccess
{
    public string ActorExternalId { get; set; } = string.Empty;
    public Guid CostCenterId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public bool IsAllowed { get; set; }
}
