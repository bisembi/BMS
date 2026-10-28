using Bms.Domain.Budgeting;

namespace Bms.Application.Budgeting;

public sealed record SaveBudgetAdjustmentCommand(Guid BaseBudgetId, Guid? ExpectedStamp, string Reason,
    IReadOnlyList<BudgetAdjustmentTransfer> Transfers);
public sealed record BudgetAdjustmentActionCommand(Guid ExpectedStamp, string? Comment = null);
public sealed record BudgetAdjustmentStage(int Number, string RoleCode, IReadOnlyList<Guid> EmployeeIds);
public sealed record BudgetAdjustmentDecision(int Submission, int Stage, string RoleCode, Guid EmployeeId,
    string Name, string Decision, DateTimeOffset AtUtc, string? Comment, string? ActingRoleCode = null);
public sealed record BudgetAdjustmentItem(Guid Id, string Number, Guid BaseBudgetId, string BaseBudgetNumber,
    Guid CostCenterId, string CostCenterName, string State, Guid Stamp, decimal TransferAmountKzt,
    string AuthorName, DateTimeOffset CreatedAtUtc, DateTimeOffset? ApprovedAtUtc);
public sealed record BudgetAdjustmentDto(BudgetAdjustmentItem Document, string Reason, DateOnly ValidFrom, DateOnly ValidTo,
    IReadOnlyList<BudgetAdjustmentTransfer> Transfers, IReadOnlyList<BudgetAdjustmentCell> EffectiveCells,
    IReadOnlyList<BudgetAdjustmentUsage> Consumption, IReadOnlyList<BudgetAdjustmentDecision> Decisions,
    IReadOnlyList<string> IncludedVersions, bool CanEdit, bool CanSubmit, bool CanApprove, bool CanStop,
    IReadOnlyList<string> ApproverNames);
public sealed record BudgetAdjustmentPage(IReadOnlyList<BudgetAdjustmentItem> Items, int Skip, int Take, int Total);
public sealed record BudgetAdjustmentBasis(Guid BaseBudgetId, string Number, Guid CostCenterId, DateOnly ValidFrom, DateOnly ValidTo,
    IReadOnlyList<BudgetAdjustmentCell> Cells, IReadOnlyList<BudgetAdjustmentUsage> Consumption, bool CanCreate,
    IReadOnlyList<BudgetAdjustmentArticle> Articles);
public sealed record BudgetAdjustmentArticle(Guid Id, string Code, string Name, string? FlowDirection, bool Selectable);
