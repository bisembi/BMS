using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bms.Application.Budgeting;

/// Validates a reference budget candidate document.
///
/// The envelope and the top-level orchestration live here; the months, the
/// cost centres, the cost and cash-flow items, the group totals, the source
/// mappings, the expected lines, the budgets, the strict primitive readers and
/// the result models live in the sibling partial files. Every rule is exact by
/// design: an unexpected property, a non-canonical string, a wrong UUID
/// version or a total that does not reconcile is a rejection, never a repair.

internal sealed record ReferenceBudgetCandidateSummary(
    string Source,
    int Year,
    int MonthCount,
    int CostCenterCount,
    int UnitCount,
    int CostItemCount,
    int CashFlowItemCount,
    int GroupMonthlyTotalCount,
    int ExpenseGroupCount,
    int BudgetInstanceCount,
    int BudgetLineCount,
    int DistinctLimitAmountCount,
    decimal RoundedSourceTotal,
    decimal RawSourceTotal,
    decimal BudgetLineTotal);

internal sealed class ReferenceBudgetCandidateValidationException : Exception
{
    public ReferenceBudgetCandidateValidationException(string message)
        : base(message)
    {
    }

    public ReferenceBudgetCandidateValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static partial class ReferenceBudgetCandidateValidator
{
    private const string ExcludedOwner = "не нужны в бюджете";
    private const int MaximumJsonCharacters = 5_000_000;
    private const decimal MaximumMoney = 9_999_999_999_999_999.99m;

    private static readonly string[] RootProperties =
    [
        "source",
        "year",
        "months",
        "cfo",
        "units",
        "costItems",
        "cashflowItems",
        "groupMonthlyTotals",
        "expenseGroups",
        "budgetInstances",
        "allocationRule"
    ];

    internal static ReferenceBudgetCandidateSummary Validate(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        if (json.Length == 0)
        {
            throw Invalid("root JSON must not be empty");
        }

        if (json.Length > MaximumJsonCharacters)
        {
            throw Invalid("root JSON exceeds the offline validation limit");
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32
            });

            return Validate(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw Invalid("root JSON is malformed", exception);
        }
    }

    private static ReferenceBudgetCandidateSummary Validate(JsonElement root)
    {
        var rootObject = ReadExactObject(root, "$", RootProperties);
        var source = ReadCanonicalString(rootObject["source"], "$.source", 260);
        if (!source.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("$.source must name an XLSX workbook");
        }

        var year = ReadInt32(rootObject["year"], "$.year");
        if (year is < 2000 or > 2100)
        {
            throw Invalid("$.year is outside the supported range");
        }

        var months = ReadMonths(rootObject["months"], year);
        var costCenters = ReadCostCenters(rootObject["cfo"]);
        var units = ReadUnits(rootObject["units"]);
        var costItems = ReadCostItems(rootObject["costItems"]);
        var cashFlowItems = ReadCashFlowItems(rootObject["cashflowItems"], costItems);
        var groupMonthlyTotals = ReadGroupMonthlyTotals(
            rootObject["groupMonthlyTotals"],
            costItems,
            months.Count);
        var expenseGroups = ReadExpenseGroups(
            rootObject["expenseGroups"],
            groupMonthlyTotals,
            cashFlowItems);
        var allocationRule = ReadCanonicalString(
            rootObject["allocationRule"],
            "$.allocationRule",
            2_000);
        if (allocationRule.Length < 100)
        {
            throw Invalid("$.allocationRule must preserve the reviewed model disclosure");
        }

        ValidateSourceMappings(costCenters, cashFlowItems);
        var expectedLines = BuildExpectedLines(
            months,
            cashFlowItems,
            groupMonthlyTotals,
            expenseGroups);
        var budget = ReadAndValidateBudgets(
            rootObject["budgetInstances"],
            year,
            months,
            costCenters,
            cashFlowItems,
            expenseGroups,
            expectedLines);

        var rawSourceTotal = expenseGroups.Sum(group =>
            groupMonthlyTotals[group].Sum(decimal.Abs));
        var roundedSourceTotal = expenseGroups.Sum(group =>
            groupMonthlyTotals[group].Sum(value => RoundMoney(decimal.Abs(value))));
        if (roundedSourceTotal != budget.Total)
        {
            throw Invalid("budget line total does not reconcile to rounded source cells");
        }

        return new ReferenceBudgetCandidateSummary(
            source,
            year,
            months.Count,
            costCenters.Count,
            units.Count,
            costItems.Count,
            cashFlowItems.Count,
            groupMonthlyTotals.Count,
            expenseGroups.Count,
            budget.InstanceCount,
            budget.LineCount,
            budget.DistinctAmounts,
            roundedSourceTotal,
            rawSourceTotal,
            budget.Total);
    }
}
