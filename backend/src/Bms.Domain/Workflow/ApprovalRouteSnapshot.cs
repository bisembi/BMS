using Bms.Domain.Common;

namespace Bms.Domain.Workflow;

/// <summary>
/// Immutable route calculated at the moment an expense request is submitted.
///
/// It deliberately carries the employees eligible at that instant instead of
/// resolving current role assignments later. A later role change must not
/// rewrite a submitted request. This type is dormant until the approved
/// workflow writer persists it atomically with the status transition.
/// </summary>
public sealed class ApprovalRouteSnapshot
{
    private readonly IReadOnlyList<ApprovalRouteSnapshotStage> _stages;

    private ApprovalRouteSnapshot(
        Guid expenseRequestId,
        int snapshotNumber,
        int workflowDefinitionVersion,
        Guid organizationId,
        Guid costCenterId,
        decimal positiveDeficitKzt,
        DateTimeOffset capturedAtUtc,
        IReadOnlyList<ApprovalRouteSnapshotStage> stages)
    {
        ExpenseRequestId = expenseRequestId;
        SnapshotNumber = snapshotNumber;
        WorkflowDefinitionVersion = workflowDefinitionVersion;
        OrganizationId = organizationId;
        CostCenterId = costCenterId;
        PositiveDeficitKzt = positiveDeficitKzt;
        CapturedAtUtc = capturedAtUtc;
        _stages = stages;
    }

    public Guid ExpenseRequestId { get; }

    /// <summary>Increments for each re-submission after a return.</summary>
    public int SnapshotNumber { get; }

    /// <summary>Version of the approved workflow definition used to build it.</summary>
    public int WorkflowDefinitionVersion { get; }

    public Guid OrganizationId { get; }

    public Guid CostCenterId { get; }

    public decimal PositiveDeficitKzt { get; }

    public DateTimeOffset CapturedAtUtc { get; }

    public IReadOnlyList<ApprovalRouteSnapshotStage> Stages => _stages;

    /// <summary>
    /// Resolves the one snapshot stage on which an employee may make an
    /// approval decision. Eligibility comes only from the immutable snapshot:
    /// a later functional-role assignment change cannot grant or revoke a
    /// decision for an already-submitted request.
    /// </summary>
    /// <remarks>
    /// This is a domain guard, not an active workflow command. A future writer
    /// must additionally check the persisted request version, status and
    /// current stage inside its transaction before using this result.
    /// </remarks>
    public ApprovalRouteSnapshotStage RequireEligibleParticipant(
        int stageNumber,
        Guid employeeId)
    {
        if (stageNumber <= 0)
        {
            throw new DomainRuleViolationException(
                "Approval route stage number must be positive.");
        }

        if (employeeId == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "Approval decision employee id is required.");
        }

        var stage = _stages.SingleOrDefault(candidate => candidate.StageNumber == stageNumber);
        if (stage is null)
        {
            throw new DomainRuleViolationException(
                "Approval route snapshot does not contain the requested stage.");
        }

        if (!stage.EligibleEmployeeIds.Contains(employeeId))
        {
            throw new DomainRuleViolationException(
                "Employee is not eligible to decide the requested approval route stage.");
        }

        return stage;
    }

    public static ApprovalRouteSnapshot Create(
        Guid expenseRequestId,
        int snapshotNumber,
        int workflowDefinitionVersion,
        Guid organizationId,
        Guid costCenterId,
        decimal positiveDeficitKzt,
        DateTimeOffset capturedAtUtc,
        IEnumerable<ApprovalRouteSnapshotStageParticipants> stageParticipants)
    {
        if (expenseRequestId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Expense request id is required.");
        }

        if (snapshotNumber <= 0)
        {
            throw new DomainRuleViolationException("Approval route snapshot number must be positive.");
        }

        if (workflowDefinitionVersion <= 0)
        {
            throw new DomainRuleViolationException("Workflow definition version must be positive.");
        }

        if (organizationId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Organization id is required.");
        }

        if (costCenterId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Cost center id is required.");
        }

        if (capturedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleViolationException("Approval route snapshot time must be UTC.");
        }

        ArgumentNullException.ThrowIfNull(stageParticipants);

        var expectedStages = ApprovalRoutePlanner.Plan(positiveDeficitKzt, workflowDefinitionVersion);
        var participantRows = stageParticipants.ToArray();
        if (participantRows.Length != expectedStages.Count)
        {
            throw new DomainRuleViolationException(
                "Approval route snapshot must contain every calculated route stage exactly once.");
        }

        var participantsByStage = new Dictionary<int, Guid[]>();
        foreach (var participantRow in participantRows)
        {
            if (participantRow is null
                || participantRow.StageNumber <= 0
                || !participantsByStage.TryAdd(
                    participantRow.StageNumber,
                    NormalizeParticipants(participantRow.EligibleEmployeeIds)))
            {
                throw new DomainRuleViolationException(
                    "Approval route snapshot contains a duplicate or invalid stage.");
            }
        }

        var stages = new ApprovalRouteSnapshotStage[expectedStages.Count];
        for (var index = 0; index < expectedStages.Count; index++)
        {
            var expectedStage = expectedStages[index];
            if (!participantsByStage.TryGetValue(expectedStage.StageNumber, out var employeeIds))
            {
                throw new DomainRuleViolationException(
                    "Approval route snapshot stages do not match the calculated route.");
            }

            stages[index] = new ApprovalRouteSnapshotStage(
                expectedStage.StageNumber,
                expectedStage.RoleCode,
                employeeIds);
        }

        return new ApprovalRouteSnapshot(
            expenseRequestId,
            snapshotNumber,
            workflowDefinitionVersion,
            organizationId,
            costCenterId,
            positiveDeficitKzt,
            capturedAtUtc,
            Array.AsReadOnly(stages));
    }

    private static Guid[] NormalizeParticipants(IEnumerable<Guid> employeeIds)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);

        var normalized = employeeIds.ToArray();
        if (normalized.Length == 0
            || normalized.Any(employeeId => employeeId == Guid.Empty)
            || normalized.Distinct().Count() != normalized.Length)
        {
            throw new DomainRuleViolationException(
                "Each approval route stage must snapshot one or more distinct employees.");
        }

        return normalized;
    }
}

/// <summary>Input recipients for one calculated route stage.</summary>
public sealed record ApprovalRouteSnapshotStageParticipants(
    int StageNumber,
    IEnumerable<Guid> EligibleEmployeeIds);

/// <summary>One immutable role and its eligible employees at snapshot time.</summary>
public sealed class ApprovalRouteSnapshotStage
{
    private readonly IReadOnlyList<Guid> _eligibleEmployeeIds;

    internal ApprovalRouteSnapshotStage(
        int stageNumber,
        string roleCode,
        IReadOnlyList<Guid> eligibleEmployeeIds)
    {
        StageNumber = stageNumber;
        RoleCode = roleCode;
        _eligibleEmployeeIds = Array.AsReadOnly(eligibleEmployeeIds.ToArray());
    }

    public int StageNumber { get; }

    public string RoleCode { get; }

    public IReadOnlyList<Guid> EligibleEmployeeIds => _eligibleEmployeeIds;
}
