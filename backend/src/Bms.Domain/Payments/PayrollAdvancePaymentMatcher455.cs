using Bms.Domain.Common;

namespace Bms.Domain.Payments;

public sealed record PayrollAdvanceRequest455(Guid RequestId,Guid OrganizationId,DateOnly TreasuryDay,decimal Amount);
public sealed record PayrollAdvanceDocument455(string DocumentKey,Guid OrganizationId,DateOnly PaymentDay,decimal Amount);

/// Exact rule authorized for advances only. Evidence of posted 3350 -> cash
/// entries and matching KZT legs is verified before this pure boundary.
public static class PayrollAdvancePaymentMatcher455
{
    public static AccountingPaymentMatchPlan Plan(IReadOnlyList<PayrollAdvanceRequest455> requests,IReadOnlyList<PayrollAdvanceDocument455> documents)
    {
        if(requests.Count>10000||documents.Count>100000||requests.Select(r=>r.RequestId).Distinct().Count()!=requests.Count
            ||documents.Select(d=>d.DocumentKey).Distinct(StringComparer.Ordinal).Count()!=documents.Count)
            throw new DomainRuleViolationException("Unique bounded payroll capture required.");
        foreach(var r in requests){if(r.RequestId==Guid.Empty||r.OrganizationId==Guid.Empty||r.TreasuryDay==default)
            throw new DomainRuleViolationException("Payroll request identity/date required.");MoneyAmountPolicy.EnsurePositive(r.Amount,"Payroll request amount");}
        foreach(var d in documents){if(d.OrganizationId==Guid.Empty||d.PaymentDay==default||string.IsNullOrWhiteSpace(d.DocumentKey))
            throw new DomainRuleViolationException("Payroll document identity/date required.");MoneyAmountPolicy.EnsurePositive(d.Amount,"Payroll payment amount");}
        var sources=documents.GroupBy(d=>(d.OrganizationId,d.PaymentDay,d.Amount)).ToDictionary(g=>g.Key,g=>g.ToArray());
        var matches=new List<AccountingPaymentMatch>();var ambiguous=new List<Guid>();var used=new HashSet<string>(StringComparer.Ordinal);
        foreach(var group in requests.GroupBy(r=>(r.OrganizationId,r.TreasuryDay,r.Amount)))
        {
            if(!sources.TryGetValue(group.Key,out var found))continue;
            if(group.Count()==1&&found.Length==1){matches.Add(new(group.Single().RequestId,[found[0].DocumentKey]));used.Add(found[0].DocumentKey);}
            else ambiguous.AddRange(group.Select(r=>r.RequestId));
        }
        return new(matches,ambiguous,documents.Where(d=>!used.Contains(d.DocumentKey)).Select(d=>d.DocumentKey).ToArray());
    }
}
