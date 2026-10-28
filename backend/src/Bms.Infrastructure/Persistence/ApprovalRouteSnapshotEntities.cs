namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Dormant physical storage for one immutable approval-route snapshot. It is
/// intentionally not exposed through a reader or a writer until the workflow
/// command is approved and implemented as an atomic transaction.
/// </summary>
public sealed class PersistedApprovalRouteSnapshot
{
    public Guid ExpenseRequestId { get; set; }
    public int SnapshotNumber { get; set; }
    public int WorkflowDefinitionVersion { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CostCenterId { get; set; }
    public decimal PositiveDeficitKzt { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
}

/// <summary>Role stage preserved with the snapshot, not resolved dynamically.</summary>
public sealed class PersistedApprovalRouteStage
{
    public Guid ExpenseRequestId { get; set; }
    public int SnapshotNumber { get; set; }
    public int StageNumber { get; set; }
    public string RoleCode { get; set; } = string.Empty;
}

/// <summary>One employee eligible to decide a particular snapshot stage.</summary>
public sealed class PersistedApprovalRouteStageParticipant
{
    public Guid ExpenseRequestId { get; set; }
    public int SnapshotNumber { get; set; }
    public int StageNumber { get; set; }
    public Guid EmployeeId { get; set; }
}
