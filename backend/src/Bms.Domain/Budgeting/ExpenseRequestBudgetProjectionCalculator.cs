using Bms.Domain.ExpenseRequests;
using Bms.Domain.Common;

namespace Bms.Domain.Budgeting;

public sealed record BudgetProjectionCalculation(
    bool IsComplete,
    IReadOnlyList<string> MissingRequirements,
    IReadOnlyList<BudgetRequestProjection> Projections)
{
    public decimal ReservedAmountKzt => Projections.Sum(projection => projection.ReservedAmountKzt);
}

public static class ExpenseRequestBudgetProjectionCalculator
{
    public static BudgetProjectionCalculation Calculate(ExpenseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Lines returns the aggregate's canonical sort and a defensive array.
        // Capture that view once: the structural pass and the conversion pass
        // must inspect the same snapshot and should not repeatedly allocate it.
        var lines = request.Lines.ToArray();
        var missing = CollectStructuralMissingRequirements(request, lines);
        var isKzt = string.Equals(request.CurrencyCode, "KZT", StringComparison.Ordinal);
        if (!isKzt && !request.HasValidPreliminaryCurrencyCalculation())
        {
            missing.Add("CurrencyRate");
        }

        var conversionLineNumber = 0;
        foreach (var line in lines)
        {
            conversionLineNumber++;
            if (!line.AmountKzt.HasValue || !line.HasValidKztCalculatedAmounts())
            {
                missing.Add($"Lines[{conversionLineNumber}].CurrencyConversion");
            }
        }

        if (missing.Count != 0)
        {
            return new BudgetProjectionCalculation(false, missing.AsReadOnly(), []);
        }

        var budgetMonth = request.EffectiveBudgetMonth
            ?? throw new DomainRuleViolationException("A complete request has no budget month.");
        var projections = lines.Select(line => BudgetRequestProjection.Create(
            request.Id,
            line.Id,
            BudgetKey.Create(
                ExpenseBudgetDimensions372.CostCenter(request, line),
                line.CashFlowItemId!.Value,
                budgetMonth),
            line.AmountKzt!.Value)).ToArray();

        return new BudgetProjectionCalculation(true, [], projections);
    }

    public static bool IsStructurallyCompleteForCurrencyCalculation(ExpenseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var lines = request.Lines.ToArray();
        return CollectStructuralMissingRequirements(request, lines).Count == 0;
    }

    private static List<string> CollectStructuralMissingRequirements(
        ExpenseRequest request,
        IReadOnlyList<ExpenseRequestLine> lines)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(request.InitiatorExternalId))
        {
            missing.Add("InitiatorExternalId");
        }

        if (!request.OrganizationId.HasValue || request.OrganizationId == Guid.Empty)
        {
            missing.Add("OrganizationId");
        }

        if (!request.DefaultCostCenterId.HasValue || request.DefaultCostCenterId == Guid.Empty)
        {
            missing.Add("DefaultCostCenterId");
        }

        if (!request.PlannedPaymentDate.HasValue)
        {
            missing.Add("PlannedPaymentDate");
        }

        if (request.CurrencyCode is not { Length: 3 }
            || request.CurrencyCode.Any(character => character is < 'A' or > 'Z'))
        {
            missing.Add("CurrencyCode");
        }

        if (string.IsNullOrWhiteSpace(request.BusinessJustification))
        {
            missing.Add("BusinessJustification");
        }

        if (request.IsUrgent && string.IsNullOrWhiteSpace(request.UrgencyReason))
        {
            missing.Add("UrgencyReason");
        }

        if (lines.Count == 0)
        {
            missing.Add("Lines");
        }

        var lineNumber = 0;
        foreach (var line in lines)
        {
            lineNumber++;
            if (!line.CashFlowItemId.HasValue || line.CashFlowItemId == Guid.Empty)
            {
                missing.Add($"Lines[{lineNumber}].CashFlowItemId");
            }

            if (!line.CostCenterId.HasValue || line.CostCenterId == Guid.Empty)
            {
                missing.Add($"Lines[{lineNumber}].CostCenterId");
            }

            if (line.Amount <= 0m)
            {
                missing.Add($"Lines[{lineNumber}].Amount");
            }

            if (!line.VatRateCode.HasValue || !Enum.IsDefined(line.VatRateCode.Value))
            {
                missing.Add($"Lines[{lineNumber}].VatRateCode");
            }

            if (!line.HasValidSourceCalculatedAmounts())
            {
                missing.Add($"Lines[{lineNumber}].CalculatedAmounts");
            }
        }

        return missing;
    }
}
