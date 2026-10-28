using Bms.Domain.Budgeting;
using Bms.Domain.Common;
namespace Bms.Application.Budgeting;
public static class BddsPlanRouteDirections
{
    // Classification comes from original gross plan lines, never their net.
    // Empty opposite-direction placeholders cannot make a funded item Mixed.
    public static BudgetFlowDirection[] FromOriginalLines(IEnumerable<(BudgetFlowDirection Direction,decimal Amount)> source)
    {
        var lines=source.ToArray();
        if(lines.Length==0)throw new CompanyBudgetPlanUnavailableException();
        foreach(var line in lines)MoneyAmountPolicy.EnsureNonNegative(line.Amount,"Approved plan amount");
        var funded=lines.Where(line=>line.Amount!=0m).Select(line=>line.Direction).Distinct().ToArray();
        return funded.Length>0?funded:lines.Select(line=>line.Direction).Distinct().ToArray();
    }
}
