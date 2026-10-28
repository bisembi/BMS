namespace Bms.Domain.Budgeting;

/// <summary>Persisted frozen submission; changes only decisions/state under a CFO lock.</summary>
public sealed class BudgetApprovalSubmission
{
    public Guid Id { get; set; }
    public Guid BudgetInstanceId { get; set; }
    public Guid CostCenterId { get; set; }
    public int SubmissionNumber { get; set; }
    public Guid ProposalConcurrencyStamp { get; set; }
    public Guid ConcurrencyStamp { get; set; }
    public Guid SubmittedByEmployeeId { get; set; }
    public string SubmittedByExternalId { get; set; } = "";
    public DateTimeOffset SubmittedAtUtc { get; set; }
    public bool RequiresOwnerReview { get; set; }
    public decimal PositiveExcessKzt { get; set; }
    public BudgetApprovalReviewState State { get; set; }
    public int? CurrentStageNumber { get; set; }
    public string CellsJson { get; set; } = "[]";
    public string StagesJson { get; set; } = "[]";
    public string DecisionsJson { get; set; } = "[]";
}
