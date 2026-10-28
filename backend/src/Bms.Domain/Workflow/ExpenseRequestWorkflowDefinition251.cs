namespace Bms.Domain.Workflow;

/// <summary>Closed new-submission routes. Persisted V1-V5 participants stay immutable.</summary>
public static class ExpenseRequestWorkflowDefinition251
{
    public static bool IsVersion(int version) => version is >= 6 and <= 13;
    public static IReadOnlyList<ApprovalRouteStage> Plan(decimal deficit, int version)
    {
        _ = ApprovalRoutePlanner.Plan(deficit); // canonical monetary validation
        if (!IsVersion(version)) throw new Bms.Domain.Common.DomainRuleViolationException("Unknown route version.");
        // The author's own owner/FD stage is already satisfied by submission;
        // independent financial control is never satisfied by that exception.
        var roles = new List<string>();
        if (version is 7 or 9 or 11 or 13) roles.Add("BUDGET_OWNER");
        // V12/V13 are new submissions authored by an assigned controller.
        // Submission fulfils only their own FC step; an independent owner,
        // FD on any deficit and GD above the existing threshold remain required.
        if (version is not (12 or 13)) roles.Add("FIN_CONTROLLER");
        if (deficit > 0 && version is not (8 or 9)) roles.Add("FIN_DIRECTOR");
        if (deficit > ApprovalRoutePlanner.GeneralDirectorThresholdKzt && version is not (10 or 11)) roles.Add("GENERAL_DIRECTOR");
        return roles.Select((role, i) => new ApprovalRouteStage(i + 1, role)).ToArray();
    }

    public static string SubmissionCondition(decimal deficit, int version, bool nsi) =>
        Plan(deficit, version).Count > 0 ? "SUBMISSION_READY"
            : nsi ? ExpenseRequestWorkflowDefinitionV3.RequiresNsi : ExpenseRequestWorkflowDefinitionV3.ReadyForPayment;

    public static bool TryCreate(int version, IEnumerable<DocumentStatus>? statuses, IEnumerable<WorkflowCommand>? commands,
        IEnumerable<WorkflowPermission>? permissions, out ExpenseRequestWorkflowDefinition? definition)
    {
        definition = null;
        if (!IsVersion(version) || !ExpenseRequestWorkflowDefinitionV2.TryCreate(statuses, commands, permissions, out var seed)
            || seed is null) return false;
        var rows = seed.Permissions.Where(p => p.StatusCode != "IN_APPROVAL" && p.CommandCode != "SUBMIT")
            .Select(p => WorkflowPermission.Create(version, p.StatusCode, p.StageNumber, p.RoleCode, p.CommandCode,
                p.TargetStatusCode, p.TargetStageNumber, p.ConditionCode, p.IsVisible, p.IsEnabled, p.IsProvisional)).ToList();
        foreach (var status in new[] { "DRAFT", "RETURNED" })
        {
            rows.Add(Rule(status, 0, WorkflowPermission.InitiatorRole, "SUBMIT", "IN_APPROVAL", 1, "SUBMISSION_READY"));
            if (version is 8 or 12)
            {
                rows.Add(Rule(status, 0, WorkflowPermission.InitiatorRole, "SUBMIT", "APPROVED", 0, ExpenseRequestWorkflowDefinitionV3.ReadyForPayment));
                rows.Add(Rule(status, 0, WorkflowPermission.InitiatorRole, "SUBMIT", "AWAITING_NSI", 0, ExpenseRequestWorkflowDefinitionV3.RequiresNsi));
            }
        }
        var maximum = Plan(100000.01m, version);
        foreach (var stage in maximum)
        {
            if (stage.StageNumber < maximum.Count)
                rows.Add(Rule("IN_APPROVAL", stage.StageNumber, stage.RoleCode, "APPROVE", "IN_APPROVAL", stage.StageNumber + 1, "ROUTE_HAS_NEXT_STAGE"));
            rows.Add(Rule("IN_APPROVAL", stage.StageNumber, stage.RoleCode, "APPROVE", "APPROVED", 0, "ROUTE_LAST_NO_NSI"));
            rows.Add(Rule("IN_APPROVAL", stage.StageNumber, stage.RoleCode, "APPROVE", "AWAITING_NSI", 0, "ROUTE_LAST_REQUIRES_NSI"));
            foreach (var (command, target) in new[] { ("RETURN", "RETURNED"), ("REJECT", "REJECTED") })
                rows.Add(Rule("IN_APPROVAL", stage.StageNumber, stage.RoleCode, command, target, 0));
            rows.Add(Rule("IN_APPROVAL", stage.StageNumber, WorkflowPermission.InitiatorRole, "CANCEL", "CANCELLED", 0));
        }
        definition = WithTreasuryRules(new(seed.Statuses, seed.Commands, rows.AsReadOnly(), version));
        return true;

        WorkflowPermission Rule(string status, int stage, string role, string command, string target, int next, string condition = "") =>
            WorkflowPermission.Create(version, status, stage, role, command, target, next, condition, isProvisional: false);
    }

    /// <summary>Unexecuted exits now stop at APPROVED; only treasury controls payment handoff.
    /// This changes no recorded status or captured participant, including historical routes.</summary>
    public static ExpenseRequestWorkflowDefinition WithTreasuryRules(ExpenseRequestWorkflowDefinition source)
    {
        var rows = source.Permissions.Where(p => p.StatusCode is not ("APPROVED" or "TO_PAY"))
            .Select(p => WorkflowPermission.Create(source.Version, p.StatusCode, p.StageNumber, p.RoleCode, p.CommandCode,
                p.TargetStatusCode == "TO_PAY" && p.CommandCode is "APPROVE" or "CONFIRM_NSI" ? "APPROVED" : p.TargetStatusCode,
                p.TargetStageNumber, p.ConditionCode, p.IsVisible, p.IsEnabled, p.IsProvisional)).ToList();
        rows.Add(Rule("APPROVED", "SEND_TO_PAYMENT", "TO_PAY"));
        foreach (var status in new[] { "APPROVED", "TO_PAY" })
        {
            rows.Add(Rule(status, "CANCEL", "CANCELLED"));
            rows.Add(Rule(status, "RETURN", "RETURNED"));
        }
        return new(source.Statuses, source.Commands, rows.AsReadOnly(), source.Version);
        WorkflowPermission Rule(string status, string command, string target) =>
            WorkflowPermission.Create(source.Version, status, 0, "TREASURER", command, target, 0, isProvisional: false);
    }
}
