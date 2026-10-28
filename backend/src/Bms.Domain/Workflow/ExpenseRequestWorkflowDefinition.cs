namespace Bms.Domain.Workflow;

/// <summary>
/// One database-loaded workflow definition that is safe to execute. The v1
/// matrix is deliberately closed: a changed, partial or extended row never
/// grants a command merely because it is present in SQL.
/// </summary>
public sealed class ExpenseRequestWorkflowDefinition
{
    internal ExpenseRequestWorkflowDefinition(
        IReadOnlyList<DocumentStatus> statuses,
        IReadOnlyList<WorkflowCommand> commands,
        IReadOnlyList<WorkflowPermission> permissions, int version = 1)
    {
        Statuses = statuses;
        Commands = commands;
        Permissions = permissions;
        Version = version;
    }

    public int Version { get; }

    public IReadOnlyList<DocumentStatus> Statuses { get; }

    public IReadOnlyList<WorkflowCommand> Commands { get; }

    public IReadOnlyList<WorkflowPermission> Permissions { get; }
}

/// <summary>
/// Exact approved v1 matrix. The migration seeds this data; this verifier
/// makes a later database drift fail closed at the command boundary.
/// </summary>
public static class ExpenseRequestWorkflowDefinitionV1
{
    public const int Version = 1;

    private static readonly ExpectedStatus[] ExpectedStatuses =
    [
        new(ExpenseRequestWorkflowCodes.DraftStatusCode, "Черновик", 10),
        new(ExpenseRequestWorkflowCodes.ReturnedStatusCode, "Возвращена", 20),
        new(ExpenseRequestWorkflowCodes.InApprovalStatusCode, "На согласовании", 30),
        new(ExpenseRequestWorkflowCodes.ApprovedStatusCode, "Утверждено", 40),
        new(ExpenseRequestWorkflowCodes.AwaitingNsiStatusCode, "Ожидает НСИ", 50),
        new(ExpenseRequestWorkflowCodes.ToPayStatusCode, "К оплате", 60),
        new(ExpenseRequestWorkflowCodes.PartiallyPaidStatusCode, "Оплачена частично", 70),
        new(ExpenseRequestWorkflowCodes.PaidStatusCode, "Оплачена", 80),
        new(ExpenseRequestWorkflowCodes.ClosedStatusCode, "Закрыта", 90),
        new(ExpenseRequestWorkflowCodes.RejectedStatusCode, "Отклонена", 100),
        new(ExpenseRequestWorkflowCodes.CancelledStatusCode, "Отменена", 110)
    ];

    private static readonly ExpectedCommand[] ExpectedCommands =
    [
        new(ExpenseRequestWorkflowCodes.SubmitCommandCode, "Подать", false, false, 10, false),
        new(ExpenseRequestWorkflowCodes.ApproveCommandCode, "Согласовать", false, true, 20, false),
        new(ExpenseRequestWorkflowCodes.ReturnCommandCode, "Вернуть", true, true, 30, false),
        new(ExpenseRequestWorkflowCodes.RejectCommandCode, "Отклонить", true, true, 40, false),
        new(ExpenseRequestWorkflowCodes.CancelCommandCode, "Отменить", true, false, 50, false),
        new(ExpenseRequestWorkflowCodes.ConfirmNsiCommandCode, "Подтвердить НСИ", false, false, 60, false),
        new(ExpenseRequestWorkflowCodes.SendToPaymentCommandCode, "В оплату", false, false, 70, false),
        new(ExpenseRequestWorkflowCodes.ClosePartialCommandCode, "Закрыть частично", true, false, 80, true),
        new(ExpenseRequestWorkflowCodes.CloseFullCommandCode, "Закрыть полностью", true, false, 90, true),
        new(ExpenseRequestWorkflowCodes.ReopenCommandCode, "Открыть повторно", true, false, 100, true),
        new(ExpenseRequestWorkflowCodes.AddAttachmentCommandCode, "Добавить вложение", true, false, 110, true)
    ];

    private static readonly ExpectedPermission[] ExpectedPermissions =
    [
        Rule("DRAFT", 0, WorkflowPermission.InitiatorRole, "SUBMIT", "IN_APPROVAL", 1, "SUBMISSION_READY"),
        Rule("DRAFT", 0, WorkflowPermission.InitiatorRole, "CANCEL", "CANCELLED", 0),
        Rule("RETURNED", 0, WorkflowPermission.InitiatorRole, "SUBMIT", "IN_APPROVAL", 1, "SUBMISSION_READY"),
        Rule("RETURNED", 0, WorkflowPermission.InitiatorRole, "CANCEL", "CANCELLED", 0),
        Rule("IN_APPROVAL", 1, "FIN_CONTROLLER", "APPROVE", "IN_APPROVAL", 2, "ROUTE_HAS_NEXT_STAGE"),
        Rule("IN_APPROVAL", 1, "FIN_CONTROLLER", "APPROVE", "APPROVED", 0, "ROUTE_LAST_NO_NSI"),
        Rule("IN_APPROVAL", 1, "FIN_CONTROLLER", "APPROVE", "AWAITING_NSI", 0, "ROUTE_LAST_REQUIRES_NSI"),
        Rule("IN_APPROVAL", 2, "FIN_DIRECTOR", "APPROVE", "IN_APPROVAL", 3, "ROUTE_HAS_NEXT_STAGE"),
        Rule("IN_APPROVAL", 2, "FIN_DIRECTOR", "APPROVE", "APPROVED", 0, "ROUTE_LAST_NO_NSI"),
        Rule("IN_APPROVAL", 2, "FIN_DIRECTOR", "APPROVE", "AWAITING_NSI", 0, "ROUTE_LAST_REQUIRES_NSI"),
        Rule("IN_APPROVAL", 3, "GENERAL_DIRECTOR", "APPROVE", "APPROVED", 0, "ROUTE_LAST_NO_NSI"),
        Rule("IN_APPROVAL", 3, "GENERAL_DIRECTOR", "APPROVE", "AWAITING_NSI", 0, "ROUTE_LAST_REQUIRES_NSI"),
        Rule("IN_APPROVAL", 1, "FIN_CONTROLLER", "RETURN", "RETURNED", 0),
        Rule("IN_APPROVAL", 2, "FIN_DIRECTOR", "RETURN", "RETURNED", 0),
        Rule("IN_APPROVAL", 3, "GENERAL_DIRECTOR", "RETURN", "RETURNED", 0),
        Rule("IN_APPROVAL", 1, "FIN_CONTROLLER", "REJECT", "REJECTED", 0),
        Rule("IN_APPROVAL", 2, "FIN_DIRECTOR", "REJECT", "REJECTED", 0),
        Rule("IN_APPROVAL", 3, "GENERAL_DIRECTOR", "REJECT", "REJECTED", 0),
        Rule("IN_APPROVAL", 1, WorkflowPermission.InitiatorRole, "CANCEL", "CANCELLED", 0),
        Rule("IN_APPROVAL", 2, WorkflowPermission.InitiatorRole, "CANCEL", "CANCELLED", 0),
        Rule("IN_APPROVAL", 3, WorkflowPermission.InitiatorRole, "CANCEL", "CANCELLED", 0),
        Rule("AWAITING_NSI", 0, "NSI_SPECIALIST", "CONFIRM_NSI", "APPROVED", 0, "NSI_CANDIDATE_LINKED"),
        Rule("AWAITING_NSI", 0, WorkflowPermission.InitiatorRole, "CANCEL", "CANCELLED", 0),
        Rule("APPROVED", 0, "TREASURER", "SEND_TO_PAYMENT", "TO_PAY", 0),
        Rule("APPROVED", 0, WorkflowPermission.InitiatorRole, "CANCEL", "CANCELLED", 0),
        Rule("TO_PAY", 0, WorkflowPermission.InitiatorRole, "CANCEL", "CANCELLED", 0)
    ];

    public static bool TryCreate(
        IEnumerable<DocumentStatus>? statuses,
        IEnumerable<WorkflowCommand>? commands,
        IEnumerable<WorkflowPermission>? permissions,
        out ExpenseRequestWorkflowDefinition? definition)
    {
        definition = null;
        if (statuses is null || commands is null || permissions is null)
        {
            return false;
        }

        try
        {
            var actualStatuses = statuses.ToArray();
            var actualCommands = commands.ToArray();
            var actualPermissions = permissions.ToArray();
            if (actualStatuses.Any(status => status is null)
                || actualCommands.Any(command => command is null)
                || actualPermissions.Any(permission => permission is null)
                || !MatchesStatuses(actualStatuses)
                || !MatchesCommands(actualCommands)
                || !MatchesPermissions(actualPermissions))
            {
                return false;
            }

            definition = new ExpenseRequestWorkflowDefinition(
                Array.AsReadOnly(actualStatuses.OrderBy(status => status.SortOrder).ToArray()),
                Array.AsReadOnly(actualCommands.OrderBy(command => command.SortOrder).ToArray()),
                Array.AsReadOnly(actualPermissions
                    .OrderBy(permission => permission.StatusCode, StringComparer.Ordinal)
                    .ThenBy(permission => permission.StageNumber)
                    .ThenBy(permission => permission.RoleCode, StringComparer.Ordinal)
                    .ThenBy(permission => permission.CommandCode, StringComparer.Ordinal)
                    .ThenBy(permission => permission.ConditionCode, StringComparer.Ordinal)
                    .ToArray()));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Produces independent v1 rows for regression tests and controlled local
    /// acceptance fixtures. Production always reads and verifies SQL rows.
    /// </summary>
    public static ExpenseRequestWorkflowDefinitionRows CreateExpectedRows() => new(
        Array.AsReadOnly(ExpectedStatuses
            .Select(status => DocumentStatus.Create(
                status.Code,
                status.Name,
                status.Code,
                "neutral",
                status.SortOrder,
                isProvisional: false))
            .ToArray()),
        Array.AsReadOnly(ExpectedCommands
            .Select(command => WorkflowCommand.Create(
                command.Code,
                command.Name,
                command.RequiresReason,
                command.IsApprovalAction,
                command.SortOrder,
                command.IsProvisional))
            .ToArray()),
        Array.AsReadOnly(ExpectedPermissions
            .Select(permission => WorkflowPermission.Create(
                Version,
                permission.StatusCode,
                permission.StageNumber,
                permission.RoleCode,
                permission.CommandCode,
                permission.TargetStatusCode,
                permission.TargetStageNumber,
                permission.ConditionCode,
                isVisible: true,
                isEnabled: true,
                isProvisional: false))
            .ToArray()));

    private static bool MatchesStatuses(IReadOnlyCollection<DocumentStatus> actual) =>
        actual.Count == ExpectedStatuses.Length
        && ExpectedStatuses.All(expected => actual.Count(status =>
            string.Equals(status.Code, expected.Code, StringComparison.Ordinal)
            && string.Equals(status.Name, expected.Name, StringComparison.Ordinal)
            && string.Equals(status.LogicalStateCode, expected.Code, StringComparison.Ordinal)
            && string.Equals(status.Color, "neutral", StringComparison.Ordinal)
            && status.SortOrder == expected.SortOrder
            && !status.IsProvisional) == 1);

    private static bool MatchesCommands(IReadOnlyCollection<WorkflowCommand> actual) =>
        actual.Count == ExpectedCommands.Length
        && ExpectedCommands.All(expected => actual.Count(command =>
            string.Equals(command.Code, expected.Code, StringComparison.Ordinal)
            && string.Equals(command.Name, expected.Name, StringComparison.Ordinal)
            && command.RequiresReason == expected.RequiresReason
            && command.IsApprovalAction == expected.IsApprovalAction
            && command.SortOrder == expected.SortOrder
            && command.IsProvisional == expected.IsProvisional) == 1);

    private static bool MatchesPermissions(IReadOnlyCollection<WorkflowPermission> actual) =>
        actual.Count == ExpectedPermissions.Length
        && ExpectedPermissions.All(expected => actual.Count(permission =>
            permission.DefinitionVersion == Version
            && string.Equals(permission.StatusCode, expected.StatusCode, StringComparison.Ordinal)
            && permission.StageNumber == expected.StageNumber
            && string.Equals(permission.RoleCode, expected.RoleCode, StringComparison.Ordinal)
            && string.Equals(permission.CommandCode, expected.CommandCode, StringComparison.Ordinal)
            && string.Equals(permission.TargetStatusCode, expected.TargetStatusCode, StringComparison.Ordinal)
            && permission.TargetStageNumber == expected.TargetStageNumber
            && string.Equals(permission.ConditionCode, expected.ConditionCode, StringComparison.Ordinal)
            && permission.IsVisible
            && permission.IsEnabled
            && !permission.IsProvisional) == 1);

    private static ExpectedPermission Rule(
        string statusCode,
        int stageNumber,
        string roleCode,
        string commandCode,
        string targetStatusCode,
        int targetStageNumber,
        string conditionCode = "") => new(
        statusCode,
        stageNumber,
        roleCode,
        commandCode,
        targetStatusCode,
        targetStageNumber,
        conditionCode);

    private sealed record ExpectedStatus(string Code, string Name, int SortOrder);

    private sealed record ExpectedCommand(
        string Code,
        string Name,
        bool RequiresReason,
        bool IsApprovalAction,
        int SortOrder,
        bool IsProvisional);

    private sealed record ExpectedPermission(
        string StatusCode,
        int StageNumber,
        string RoleCode,
        string CommandCode,
        string TargetStatusCode,
        int TargetStageNumber,
        string ConditionCode);
}

public sealed record ExpenseRequestWorkflowDefinitionRows(
    IReadOnlyList<DocumentStatus> Statuses,
    IReadOnlyList<WorkflowCommand> Commands,
    IReadOnlyList<WorkflowPermission> Permissions);
