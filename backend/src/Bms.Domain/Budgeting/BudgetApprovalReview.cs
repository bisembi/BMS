using Bms.Domain.Common;
using Bms.Domain.Workflow;

namespace Bms.Domain.Budgeting;

/// <summary>
/// One frozen submission and its ordered decisions. A transactional writer
/// must bind this state to the unchanged draft, re-prove live CFO authority,
/// and publish BudgetInstance.Approve only with the terminal decision. This
/// domain object is not yet a persisted workflow or a command capability.
/// </summary>
public sealed class BudgetApprovalReview
{
    private readonly IReadOnlyList<BudgetApprovalReviewStage> stages;
    private readonly List<BudgetApprovalReviewDecision> decisions = [];

    private BudgetApprovalReview(BudgetApprovalAssessment assessment, int submissionNumber,
        Guid submittedByEmployeeId, string submittedByExternalId, DateTimeOffset submittedAtUtc,
        IReadOnlyList<BudgetApprovalReviewStage> stages)
    {
        Id = Guid.NewGuid();
        Assessment = assessment;
        SubmissionNumber = submissionNumber;
        SubmittedByEmployeeId = submittedByEmployeeId;
        SubmittedByExternalId = submittedByExternalId;
        SubmittedAtUtc = submittedAtUtc;
        this.stages = stages;
        ConcurrencyStamp = Guid.NewGuid();
        State = BudgetApprovalReviewState.Pending;
        CurrentStageNumber = 1;
    }

    public Guid Id { get; private set; }
    public BudgetApprovalAssessment Assessment { get; }
    public int SubmissionNumber { get; }
    public Guid SubmittedByEmployeeId { get; }
    public string SubmittedByExternalId { get; }
    public DateTimeOffset SubmittedAtUtc { get; }
    public Guid ConcurrencyStamp { get; private set; }
    public BudgetApprovalReviewState State { get; private set; }
    public int? CurrentStageNumber { get; private set; }
    public IReadOnlyList<BudgetApprovalReviewStage> Stages => stages;
    public IReadOnlyList<BudgetApprovalReviewDecision> Decisions => decisions.AsReadOnly();

    public static BudgetApprovalReview Restore(Guid id, Guid stamp, BudgetApprovalAssessment assessment,
        int submissionNumber, Guid submittedByEmployeeId, string submittedByExternalId, DateTimeOffset submittedAtUtc,
        bool requiresOwnerReview, IReadOnlyCollection<BudgetApprovalReviewStageInput> participants,
        IReadOnlyCollection<BudgetApprovalReviewDecision> savedDecisions, BudgetApprovalReviewState expectedState, int? expectedStage)
    {
        if (id == Guid.Empty || stamp == Guid.Empty || savedDecisions is null)
            throw new DomainRuleViolationException("Stored review identity is missing.");
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(participants);
        // Frozen SQL JSON is immutable. Restore only the exact known routes,
        // independently of the current submission policy (also for recovery).
        var direct = !requiresOwnerReview && participants.Any(stage =>
            stage is not null && stage.StageNumber == 1
                && stage.RoleCode == ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode);
        var financial = ApprovalRoutePlanner.Plan(assessment.PositiveExcessKzt);
        var legacy = requiresOwnerReview
            ? Array.AsReadOnly(new[] { new ApprovalRouteStage(1, ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode) }
                .Concat(financial.Select(stage => new ApprovalRouteStage(stage.StageNumber + 1, stage.RoleCode))).ToArray())
            : financial;
        // Do not derive historical routes from the mutable submission planner.
        // Every new budget requires both directors. Main1 captured FD-only
        // submissions remain valid only at their original <=100000 threshold.
        IReadOnlyList<ApprovalRouteStage> planned = legacy;
        if (direct)
        {
            var capturedFdOnly = participants.Count == 1
                && assessment.PositiveExcessKzt <= ApprovalRoutePlanner.GeneralDirectorThresholdKzt;
            planned = capturedFdOnly
                ? Array.AsReadOnly(new[] { new ApprovalRouteStage(1, ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode) })
                : Array.AsReadOnly(new[] {
                    new ApprovalRouteStage(1, ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode),
                    new ApprovalRouteStage(2, ExpenseRequestWorkflowCodes.GeneralDirectorRoleCode),
                });
        }
        var review = Create(assessment, submissionNumber, submittedByEmployeeId, submittedByExternalId,
            submittedAtUtc, requiresOwnerReview, participants, planned);
        foreach (var saved in savedDecisions)
        {
            var current = review.Stages.SingleOrDefault(s => s.StageNumber == review.CurrentStageNumber);
            if (saved is null || current is null || saved.StageNumber != current.StageNumber || saved.RoleCode != current.RoleCode)
                throw new DomainRuleViolationException("Stored review decision order is inconsistent.");
            if (saved.ActingRoleCode is null)
                review.Decide(review.ConcurrencyStamp, saved.EmployeeId, saved.ActorExternalId,
                    saved.Decision, saved.DecidedAtUtc, saved.Comment);
            else if (saved.ActingRoleCode == ExpenseRequestWorkflowCodes.FinancialControllerRoleCode)
                review.DecideAsFinancialController(review.ConcurrencyStamp, saved.EmployeeId, saved.ActorExternalId,
                    saved.Decision, saved.DecidedAtUtc, saved.Comment);
            else throw new DomainRuleViolationException("Stored budget decision actor role is invalid.");
        }
        if (review.State != expectedState || review.CurrentStageNumber != expectedStage)
            throw new DomainRuleViolationException("Stored review state is inconsistent.");
        review.Id = id;
        review.ConcurrencyStamp = stamp;
        return review;
    }

    public static BudgetApprovalReview Submit(BudgetApprovalAssessment assessment, int submissionNumber,
        Guid submittedByEmployeeId, string submittedByExternalId, DateTimeOffset submittedAtUtc,
        bool requiresOwnerReview, IReadOnlyCollection<BudgetApprovalReviewStageInput> participants)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        return Create(assessment, submissionNumber, submittedByEmployeeId, submittedByExternalId,
            submittedAtUtc, requiresOwnerReview, participants, assessment.Plan(requiresOwnerReview));
    }

    private static BudgetApprovalReview Create(BudgetApprovalAssessment assessment, int submissionNumber,
        Guid submittedByEmployeeId, string submittedByExternalId, DateTimeOffset submittedAtUtc,
        bool requiresOwnerReview, IReadOnlyCollection<BudgetApprovalReviewStageInput> participants,
        IReadOnlyList<ApprovalRouteStage> planned)
    {
        ArgumentNullException.ThrowIfNull(participants);
        if (submissionNumber <= 0 || submittedByEmployeeId == Guid.Empty)
            throw new DomainRuleViolationException("A valid budget submission and employee are required.");
        RequireActor(submittedByExternalId);
        RequireTime(submittedAtUtc);
        var inputs = participants.ToArray();
        if (inputs.Length != planned.Count || inputs.Any(input => input is null)
            || inputs.Select(input => input.StageNumber).Distinct().Count() != inputs.Length)
            throw new DomainRuleViolationException("Budget review participants do not match the planned route.");
        var captured = new List<BudgetApprovalReviewStage>();
        foreach (var expected in planned)
        {
            var input = inputs.SingleOrDefault(row => row.StageNumber == expected.StageNumber);
            if (input is null || input.RoleCode != expected.RoleCode || input.EmployeeIds is null)
                throw new DomainRuleViolationException("Budget review role or stage is inconsistent.");
            var employees = input.EmployeeIds.ToArray();
            if (employees.Length == 0 || employees.Any(id => id == Guid.Empty)
                || employees.Distinct().Count() != employees.Length
                || (requiresOwnerReview && expected.StageNumber == 1 && employees.Contains(submittedByEmployeeId)))
                throw new DomainRuleViolationException("Budget review needs distinct eligible employees and an independent owner.");
            captured.Add(new BudgetApprovalReviewStage(expected.StageNumber, expected.RoleCode,
                Array.AsReadOnly(employees)));
        }
        return new BudgetApprovalReview(assessment, submissionNumber, submittedByEmployeeId,
            submittedByExternalId, submittedAtUtc, Array.AsReadOnly(captured.ToArray()));
    }

    public void Decide(Guid expectedConcurrencyStamp, Guid employeeId, string actorExternalId,
        BudgetApprovalDecisionKind decision, DateTimeOffset decidedAtUtc, string? comment = null)
        => DecideCore(expectedConcurrencyStamp, employeeId, actorExternalId, decision, decidedAtUtc, comment, null);

    /// <summary>Caller must prove the current FIN_CONTROLLER assignment inside the transaction.</summary>
    public void DecideAsFinancialController(Guid expectedConcurrencyStamp, Guid employeeId, string actorExternalId,
        BudgetApprovalDecisionKind decision, DateTimeOffset decidedAtUtc, string? comment = null)
        => DecideCore(expectedConcurrencyStamp, employeeId, actorExternalId, decision, decidedAtUtc, comment,
            ExpenseRequestWorkflowCodes.FinancialControllerRoleCode);

    private void DecideCore(Guid expectedConcurrencyStamp, Guid employeeId, string actorExternalId,
        BudgetApprovalDecisionKind decision, DateTimeOffset decidedAtUtc, string? comment, string? actingRoleCode)
    {
        if (expectedConcurrencyStamp == Guid.Empty || expectedConcurrencyStamp != ConcurrencyStamp)
            throw new BudgetInstanceConcurrencyConflictException();
        if (State != BudgetApprovalReviewState.Pending || CurrentStageNumber is not int number)
            throw new DomainRuleViolationException("Only the current pending budget stage may be decided.");
        var current = stages.Single(stage => stage.StageNumber == number);
        var controllerStop = actingRoleCode == ExpenseRequestWorkflowCodes.FinancialControllerRoleCode
            && current.RoleCode == ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode
            && decision is BudgetApprovalDecisionKind.Return or BudgetApprovalDecisionKind.Reject;
        if (employeeId == Guid.Empty || !Enum.IsDefined(decision)
            || (actingRoleCode is not null && !controllerStop)
            || (!controllerStop && !current.EmployeeIds.Contains(employeeId)))
            throw new DomainRuleViolationException("Employee is not eligible to decide this budget stage.");
        RequireActor(actorExternalId);
        RequireTime(decidedAtUtc);
        if (decidedAtUtc < SubmittedAtUtc || (decisions.Count > 0 && decidedAtUtc < decisions[^1].DecidedAtUtc))
            throw new DomainRuleViolationException("Budget decision time precedes the captured submission or decision.");
        if (comment is { Length: > 2000 } || (comment?.Any(character =>
            char.IsControl(character) && character is not ('\r' or '\n' or '\t')) ?? false))
            throw new DomainRuleViolationException("Budget review comment is invalid.");
        var normalized = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (decision != BudgetApprovalDecisionKind.Approve && normalized is null)
            throw new DomainRuleViolationException("A return or rejection requires a comment.");

        // No changes before all validation succeeds. The next stage cannot be
        // chosen by the caller, and a final decision publishes no limit here.
        decisions.Add(new BudgetApprovalReviewDecision(number, current.RoleCode, employeeId,
            actorExternalId, decision, decidedAtUtc, normalized, actingRoleCode));
        State = decision switch
        {
            BudgetApprovalDecisionKind.Return => BudgetApprovalReviewState.Returned,
            BudgetApprovalDecisionKind.Reject => BudgetApprovalReviewState.Rejected,
            _ when number == stages.Count => BudgetApprovalReviewState.Approved,
            _ => BudgetApprovalReviewState.Pending,
        };
        CurrentStageNumber = State == BudgetApprovalReviewState.Pending ? number + 1 : null;
        ConcurrencyStamp = Guid.NewGuid();
    }

    private static void RequireActor(string actorExternalId)
    {
        if (string.IsNullOrWhiteSpace(actorExternalId) || actorExternalId.Length > 256
            || actorExternalId != actorExternalId.Trim() || actorExternalId.Any(char.IsControl))
            throw new DomainRuleViolationException("A canonical decision actor is required.");
    }

    private static void RequireTime(DateTimeOffset atUtc)
    {
        if (atUtc == default || atUtc.Offset != TimeSpan.Zero)
            throw new DomainRuleViolationException("A nonempty UTC decision time is required.");
    }
}

public enum BudgetApprovalReviewState { Pending, Returned, Rejected, Approved }
public enum BudgetApprovalDecisionKind { Approve, Return, Reject }
public sealed record BudgetApprovalReviewStageInput(int StageNumber, string RoleCode,
    IReadOnlyCollection<Guid> EmployeeIds);
public sealed record BudgetApprovalReviewStage(int StageNumber, string RoleCode, IReadOnlyList<Guid> EmployeeIds);
public sealed record BudgetApprovalReviewDecision(int StageNumber, string RoleCode, Guid EmployeeId,
    string ActorExternalId, BudgetApprovalDecisionKind Decision, DateTimeOffset DecidedAtUtc, string? Comment,
    string? ActingRoleCode = null);
