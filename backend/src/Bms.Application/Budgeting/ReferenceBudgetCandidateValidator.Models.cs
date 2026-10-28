using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bms.Application.Budgeting;

/// The shapes the readers produce.
internal static partial class ReferenceBudgetCandidateValidator
{

    private sealed record CostCenter(Guid Id, string Code, string Name, bool FromItemOwner);

    private sealed record ReferenceUnit(Guid Id, string Code, string Name);

    private sealed record CostItem(Guid Id, string Code, string Name);

    private sealed record CashFlowItem(
        int SourceIndex,
        Guid Id,
        string Code,
        string Name,
        string Full,
        string Direction,
        string Group,
        string Owner,
        string Flow,
        Guid CostItemId);

    private readonly record struct BudgetLineKey(Guid CashFlowItemId, DateOnly BudgetPeriod);

    private sealed record BudgetValidationResult(
        int InstanceCount,
        int LineCount,
        int DistinctAmounts,
        decimal Total);
}
