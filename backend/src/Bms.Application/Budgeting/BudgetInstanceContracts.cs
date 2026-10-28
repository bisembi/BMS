using System.Text.Json.Serialization;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;

namespace Bms.Application.Budgeting;

public sealed record BudgetInstanceCreationOptionsDto(
    bool CanChooseAnyCostCenter,
    BudgetDirectoryOptionDto? DefaultCostCenter);

public sealed record BudgetInstanceLineDto(
    Guid Id,
    Guid CashFlowItemId,
    DateOnly BudgetMonth,
    decimal LimitAmountKzt,
    int SortOrder,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Comment = null,
    string FlowDirection = nameof(BudgetFlowDirection.Payment));

public sealed record BudgetInstanceDto(
    Guid Id,
    string Number,
    Guid CostCenterId,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    string Status,
    Guid OwnerEmployeeId,
    Guid ConcurrencyStamp,
    IReadOnlyList<BudgetInstanceLineDto> Lines,
    decimal TotalLimitAmountKzt,
    bool CanEdit,
    bool CanApprove,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CostCenterCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CostCenterName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ApprovalOrdinal = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ApprovedAtUtc = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ApprovedByEmployeeId = null,
    decimal TotalReceiptsAmountKzt = 0m,
    decimal TotalPaymentsAmountKzt = 0m,
    decimal NetCashFlowAmountKzt = 0m);

public sealed record BudgetInstanceVersionMembershipDto(Guid Id, string Name);

public sealed record BudgetInstanceListItemDto(
    Guid Id,
    string Number,
    Guid CostCenterId,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    string Status,
    int LineCount,
    decimal TotalLimitAmountKzt,
    Guid ConcurrencyStamp,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CostCenterCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CostCenterName = null,
    decimal TotalReceiptsAmountKzt = 0m,
    decimal TotalPaymentsAmountKzt = 0m,
    decimal NetCashFlowAmountKzt = 0m,
    bool IsEffective = false,
    bool CanDelete = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ApprovalState = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AuthorName = null,
    IReadOnlyList<BudgetInstanceVersionMembershipDto>? IncludedVersions = null);

public sealed record BudgetInstancePageDto(
    IReadOnlyList<BudgetInstanceListItemDto> Items,
    int Skip,
    int Take,
    int Total);

public sealed record BudgetInstanceLineCommand(
    Guid CashFlowItemId,
    DateOnly BudgetMonth,
    decimal LimitAmountKzt,
    string? Comment = null,
    string FlowDirection = nameof(BudgetFlowDirection.Payment));

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateBudgetInstanceCommand(
    [property: JsonRequired] Guid CostCenterId,
    [property: JsonRequired] DateOnly ValidFrom,
    [property: JsonRequired] DateOnly ValidTo,
    [property: JsonRequired] IReadOnlyList<BudgetInstanceLineCommand> Lines);

public sealed record UpdateBudgetInstanceCommand(
    Guid ExpectedConcurrencyStamp,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    IReadOnlyList<BudgetInstanceLineCommand> Lines);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApproveBudgetInstanceCommand(
    [property: JsonRequired] Guid ExpectedConcurrencyStamp);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeleteBudgetInstanceCommand(
    [property: JsonRequired] Guid CostCenterId,
    [property: JsonRequired] Guid ExpectedConcurrencyStamp);

public sealed record BudgetInstanceCommandResult(
    BudgetInstanceDto Instance,
    [property: JsonIgnore] bool Created);

internal static class BudgetInstanceMapper
{
    internal static BudgetInstanceDto ToDto(BudgetInstance instance, bool canEdit, bool canApprove,
        string? costCenterCode = null, string? costCenterName = null) =>
        new(
            instance.Id,
            instance.Number,
            instance.CostCenterId,
            instance.ValidFrom,
            instance.ValidTo,
            instance.Status.ToString(),
            instance.OwnerEmployeeId,
            instance.ConcurrencyStamp,
            instance.Lines
                .OrderBy(line => line.SortOrder)
                .ThenBy(line => line.Id)
                .Select(line => new BudgetInstanceLineDto(
                    line.Id,
                    line.CashFlowItemId,
                    line.BudgetMonth,
                    line.LimitAmountKzt,
                    line.SortOrder,
                    line.Comment,
                    line.FlowDirection.ToString()))
                .ToArray(),
            instance.Lines.Where(line => line.FlowDirection == BudgetFlowDirection.Payment)
                .Sum(line => line.LimitAmountKzt),
            canEdit,
            canApprove,
            costCenterCode,
            costCenterName,
            instance.ApprovalOrdinal,
            instance.ApprovedAtUtc,
            instance.ApprovedByEmployeeId,
            instance.Lines.Where(line => line.FlowDirection == BudgetFlowDirection.Receipt)
                .Sum(line => line.LimitAmountKzt),
            instance.Lines.Where(line => line.FlowDirection == BudgetFlowDirection.Payment)
                .Sum(line => line.LimitAmountKzt),
            instance.Lines.Where(line => line.FlowDirection == BudgetFlowDirection.Receipt)
                .Sum(line => line.LimitAmountKzt)
                - instance.Lines.Where(line => line.FlowDirection == BudgetFlowDirection.Payment)
                    .Sum(line => line.LimitAmountKzt));

    internal static BudgetInstanceListItemDto ToListItem(BudgetInstance instance) =>
        new(
            instance.Id,
            instance.Number,
            instance.CostCenterId,
            instance.ValidFrom,
            instance.ValidTo,
            instance.Status.ToString(),
            instance.Lines.Count,
            instance.Lines.Where(line => line.FlowDirection == BudgetFlowDirection.Payment)
                .Sum(line => line.LimitAmountKzt),
            instance.ConcurrencyStamp,
            TotalReceiptsAmountKzt: instance.Lines
                .Where(line => line.FlowDirection == BudgetFlowDirection.Receipt)
                .Sum(line => line.LimitAmountKzt),
            TotalPaymentsAmountKzt: instance.Lines
                .Where(line => line.FlowDirection == BudgetFlowDirection.Payment)
                .Sum(line => line.LimitAmountKzt),
            NetCashFlowAmountKzt: instance.Lines
                .Where(line => line.FlowDirection == BudgetFlowDirection.Receipt)
                .Sum(line => line.LimitAmountKzt)
                - instance.Lines.Where(line => line.FlowDirection == BudgetFlowDirection.Payment)
                    .Sum(line => line.LimitAmountKzt));

    internal static IReadOnlyList<BudgetInstanceLineDraft> ToDrafts(
        IReadOnlyList<BudgetInstanceLineCommand> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var drafts = new BudgetInstanceLineDraft[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index]
                ?? throw new DomainRuleViolationException("A budget line draft cannot be null.",
                    DomainRuleViolationReason.BudgetLinesInvalid, index);
            drafts[index] = new BudgetInstanceLineDraft(
                line.CashFlowItemId,
                line.BudgetMonth,
                line.LimitAmountKzt,
                line.Comment,
                ParseDirection(line.FlowDirection));
        }

        return drafts;
    }

    internal static BudgetFlowDirection ParseDirection(string? value) => value switch
    {
        null or "" or nameof(BudgetFlowDirection.Payment) => BudgetFlowDirection.Payment,
        nameof(BudgetFlowDirection.Receipt) => BudgetFlowDirection.Receipt,
        _ => throw new DomainRuleViolationException("Budget flow direction is invalid.",
            DomainRuleViolationReason.BudgetLinesInvalid)
    };
}
