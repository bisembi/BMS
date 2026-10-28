using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Domain.Budgeting;

public sealed record BudgetPaymentShare343(Guid LineId,int SortOrder,BudgetKey Key,decimal ReleasedOriginalKzt);

/// <summary>Confirmed bank KZT replaces the released original reserve, in the
/// request's original budget keys/months, not the date of the bank entry.
/// User-confirmed FX rule: proportional to settled original line amounts,
/// rounded to tiyn; the last line receives the exact unallocated remainder.</summary>
public static class BudgetPaymentAttribution343
{
    public static IReadOnlyDictionary<BudgetKey,decimal> Allocate(decimal actualKzt,IReadOnlyCollection<BudgetPaymentShare343> shares)
    {
        Money(actualKzt);
        if(actualKzt<=0m||shares.Count is <1 or >ExpenseRequestAllocationPolicy.MaximumLines||shares.Select(s=>s.LineId).Distinct().Count()!=shares.Count
            ||shares.Any(s=>s.LineId==Guid.Empty||s.ReleasedOriginalKzt<=0m))
            throw new InvalidOperationException("Exact settled payment line evidence required.");
        foreach(var share in shares)Money(share.ReleasedOriginalKzt);
        var ordered=shares.OrderBy(s=>s.SortOrder).ThenBy(s=>s.LineId.ToString("N"),StringComparer.Ordinal).ToArray();
        var total=ordered.Sum(s=>s.ReleasedOriginalKzt);Money(total);
        var result=new Dictionary<BudgetKey,decimal>();var remaining=actualKzt;
        for(var i=0;i<ordered.Length;i++)
        {
            var share=ordered[i];
            // Decimal ratio first avoids overflowing actual*original at the
            // existing monetary ceiling. Cap rounded shares at cash remaining
            // for very small payments split among many lines.
            var amount=i==ordered.Length-1?remaining:Math.Min(remaining,
                decimal.Round(actualKzt*(share.ReleasedOriginalKzt/total),2,MidpointRounding.AwayFromZero));
            Money(amount);remaining-=amount;result[share.Key]=checked(result.GetValueOrDefault(share.Key)+amount);
        }
        if(remaining!=0m||result.Values.Sum()!=actualKzt)throw new InvalidOperationException("Bank payment attribution does not conserve cash.");
        return result;
    }
    private static void Money(decimal value)
    {
        if(value<0m||value>MoneyAmountPolicy.Maximum||decimal.Round(value,2)!=value)
            throw new InvalidOperationException("Exact nonnegative KZT payment required.");
    }
}
