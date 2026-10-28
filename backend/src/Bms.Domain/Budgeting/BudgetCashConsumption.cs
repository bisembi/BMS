using Bms.Domain.Common;

namespace Bms.Domain.Budgeting;

/// <summary>Gross eligible 1C cash plus BMS settlement not yet represented in
/// that exact source capture. Represented is measured in the ORIGINAL BMS
/// reserve, not the bank's exchange-rate equivalent; foreign-rate differences
/// therefore stay in the source fact rather than being deducted twice.</summary>
public static class BudgetCashConsumption
{
    public static decimal EffectivePaid(decimal sourceCashKzt, decimal bmsPaidKzt, decimal representedBmsPaidKzt)
    {
        MoneyAmountPolicy.EnsureNonNegative(sourceCashKzt, nameof(sourceCashKzt));
        MoneyAmountPolicy.EnsureNonNegative(bmsPaidKzt, nameof(bmsPaidKzt));
        MoneyAmountPolicy.EnsureNonNegative(representedBmsPaidKzt, nameof(representedBmsPaidKzt));
        if (representedBmsPaidKzt > bmsPaidKzt)
            throw new DomainRuleViolationException("Represented BMS payments exceed the recorded settlement.");
        var result = checked(sourceCashKzt + bmsPaidKzt - representedBmsPaidKzt);
        MoneyAmountPolicy.EnsureNonNegative(result, "Effective cash consumption");
        return result;
    }
}
