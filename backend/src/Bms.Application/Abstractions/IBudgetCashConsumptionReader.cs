using Bms.Domain.Budgeting;

namespace Bms.Application.Abstractions;

public sealed record BudgetCashConsumptionAmount(decimal SourceCashKzt, decimal RepresentedBmsPaidKzt)
{
    public decimal EffectivePaid(decimal bmsPaidKzt) =>
        BudgetCashConsumption.EffectivePaid(SourceCashKzt, bmsPaidKzt, RepresentedBmsPaidKzt);
}

/// <summary>Gross source and source-linked BMS settlement for factual execution
/// and request admission. Only the remaining reserve is deducted in addition
/// to cash already represented by the exact immutable source capture.
/// Missing source is a failure,
/// not zero; a proven key without source movements has an explicit zero.</summary>
public interface IBudgetCashConsumptionReader
{
    Bms.Application.Budgeting.ExecutionFactSourceDto? CapturedSource { get; }
    Task<IReadOnlyDictionary<BudgetKey, BudgetCashConsumptionAmount>> ReadAsync(
        IReadOnlyCollection<BudgetKey> keys, CancellationToken token);
}
