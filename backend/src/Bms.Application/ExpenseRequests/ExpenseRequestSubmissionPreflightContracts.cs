namespace Bms.Application.ExpenseRequests;

public static class SubmissionPreflightProblemCodes
{
    public const string Unavailable = "submission-preflight-unavailable";
}

public static class SubmissionPreflightBlockerCodes
{
    public const string AdministratorReadOnly = "Command.AdministratorReadOnly";
    public const string BudgetOwnerRequired = "Command.BudgetOwnerRequired";
    public const string AttachmentFactsUnavailable = "AttachmentFacts.Unavailable";
    public const string AccrualSnapshotSourceUnavailable = "AccrualSnapshotSource.Unavailable";
    public const string CashFlowItemMissing = "AccrualSnapshotSource.CashFlowItemMissing";
    public const string CashFlowItemInactive = "AccrualSnapshotSource.CashFlowItemInactive";
    public const string CashFlowItemGroup = "AccrualSnapshotSource.CashFlowItemGroup";
    public const string AccrualPeriodMissing = "ExpenseRequestLine.AccrualPeriodMissing";
    public const string AccrualPeriodNotMonthStart = "ExpenseRequestLine.AccrualPeriodNotMonthStart";
    public const string WorkflowDefinitionUnavailable = "WorkflowDefinition.Unavailable";
    public const string WorkflowSubmitCommandUnavailable = "WorkflowSubmitCommand.Unavailable";
    public const string ApprovalRoleAssignmentsUnavailable = "ApprovalRoleAssignments.Unavailable";
}

public static class SubmissionPreflightBlockerCategories
{
    public const string Command = "command";
    public const string Budget = "budget";
    public const string Attachment = "attachment";
    public const string Accrual = "accrual";
    public const string Data = "data";
    public const string Workflow = "workflow";
    public const string RoleAssignments = "roleAssignments";
}

public sealed record SubmissionBudgetReadinessDto(
    bool IsReady,
    decimal? PositiveDeficitKzt,
    IReadOnlyList<string> Blockers);

public sealed record SubmissionPreflightCapabilitiesDto(
    bool AttachmentFactsReady,
    bool AccrualSnapshotSourceReady,
    bool WorkflowSeedReady,
    bool RoleAssignmentsReady);

public sealed record SubmissionRoutePreviewDto(
    bool PreviewOnly,
    decimal PositiveDeficitKzt,
    IReadOnlyList<string> RequiredRoleCodes,
    bool AssigneesResolved);

public sealed record SubmissionPreflightBlockerDto(
    string Category,
    string Code,
    int? LineNumber = null);

public sealed record ExpenseRequestSubmissionPreflightDto(
    Guid RequestId,
    Guid RequestVersion,
    DateTimeOffset EvaluatedAtUtc,
    SubmissionReadinessDto SubmissionReadiness,
    SubmissionBudgetReadinessDto BudgetReadiness,
    SubmissionPreflightCapabilitiesDto Capabilities,
    SubmissionRoutePreviewDto? RoutePreview,
    bool CanSubmit,
    IReadOnlyList<SubmissionPreflightBlockerDto> Blockers,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? OperationKind = null);
