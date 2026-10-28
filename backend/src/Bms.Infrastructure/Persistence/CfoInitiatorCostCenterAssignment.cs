namespace Bms.Infrastructure.Persistence;

public sealed class CfoInitiatorCostCenterAssignment
{
    public Guid EmployeeId { get; set; }
    public Guid CostCenterId { get; set; }
    public bool IsActive { get; set; }
}
