using Bms.Domain.Common;
using System.Text.Json.Serialization;

namespace Bms.Domain.ExpenseRequests;

// A new row may only inherit the budget identity and frozen accrual article of
// an existing source line. Never accept these protected fields from the client.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExpenseRequestApprovalAllocationLine(Guid SourceLineId, Guid? LineId, decimal Amount,
    Guid AccrualCostCenterId, DateOnly AccrualPeriod, ExpenseVatRate VatRateCode, Guid SourceGroupId);

public sealed class ApprovalAllocationTotalMismatchException : Exception;
public sealed class ApprovalAllocationSourceMismatchException : Exception;

public sealed partial class ExpenseRequest
{
    private static (Guid?,Guid?,bool?,Guid?) AllocationKey(ExpenseRequestLine l) =>
        (l.CashFlowItemId,l.CostCenterId,l.AccrualSnapshotFormsManagerialAccrual,l.AccrualSnapshotExpenseItemId);
    public IReadOnlyList<ExpenseRequestApprovalAllocationLine> ApprovalAllocationLines()
    {
        var groups=Lines.GroupBy(AllocationKey).ToDictionary(g=>g.Key,g=>g.First().Id);
        return Lines.Where(l=>l.AccrualPeriod.HasValue&&l.VatRateCode.HasValue).Select(l=>
            new ExpenseRequestApprovalAllocationLine(l.Id,l.Id,l.Amount,
                (l.AccrualCostCenterId??l.CostCenterId).GetValueOrDefault(),l.AccrualPeriod!.Value,l.VatRateCode!.Value,groups[AllocationKey(l)])).ToArray();
    }

    private IReadOnlyList<(ExpenseRequestLine? Existing,ExpenseRequestLine Prepared)> PrepareApprovalAllocations(
        IReadOnlyList<ExpenseRequestApprovalAllocationLine> edits)
    {
        if(edits.Count is 0 or >ExpenseRequestAllocationPolicy.MaximumLines || edits.Any(e=>e is null)
            || FullyPaidAtUtc is not null || AccrualCycleId is not null)
            throw new DomainRuleViolationException("Invalid accrual allocation or payment already started.");
        var old=Lines.ToDictionary(l=>l.Id);
        var groups=Lines.GroupBy(AllocationKey).ToDictionary(g=>g.Key,g=>g.ToArray());
        var groupIds=groups.ToDictionary(g=>g.Key,g=>g.Value[0].Id);
        // Keep every persisted line identity: reservations and future bank/closing
        // links are not silently discarded. Only server-assigned child IDs are new.
        var existing=edits.Where(e=>e.LineId.HasValue).Select(e=>e.LineId!.Value).ToArray();
        if(existing.Length!=old.Count || existing.Distinct().Count()!=old.Count || existing.Any(id=>!old.ContainsKey(id))
            || edits.Any(e=>!old.ContainsKey(e.SourceLineId)||(e.LineId.HasValue&&e.LineId!=e.SourceLineId)))
            throw new DomainRuleViolationException("Foreign, duplicate or missing accrual source line.");
        foreach(var e in edits)
        {
            MoneyAmountPolicy.EnsurePositive(e.Amount,"Allocation amount");
            var l=old[e.SourceLineId];
            if(e.SourceGroupId!=groupIds[AllocationKey(l)] || e.AccrualCostCenterId==Guid.Empty || e.AccrualPeriod.Year is <1900 or >9998 || e.AccrualPeriod.Day!=1
                || !Enum.IsDefined(e.VatRateCode) || (PaymentOperation is not null&&e.VatRateCode!=ExpenseVatRate.WithoutVat)
                || l.AmountKzt is null || l.AccrualPeriod is null || l.VatRateCode is null
                || !l.HasValidOrAbsentAccrualSnapshot() || l.ConfirmedClosedAmount!=0 || l.ConfirmedClosedAmountKzt!=0)
                throw new DomainRuleViolationException("Invalid accrual allocation details.");
        }
        if(edits.Sum(e=>e.Amount)!=TotalAmount)throw new ApprovalAllocationTotalMismatchException();
        foreach(var group in edits.GroupBy(e=>AllocationKey(old[e.SourceLineId])))
            if(group.Sum(e=>e.Amount)!=groups[group.Key].Sum(l=>l.Amount))throw new ApprovalAllocationSourceMismatchException();

        var result=new List<(ExpenseRequestLine?,ExpenseRequestLine)>();
        var nextSort=old.Values.Max(l=>l.SortOrder);
        foreach(var group in edits.GroupBy(e=>AllocationKey(old[e.SourceLineId])))
        {
            var originals=groups[group.Key];var remaining=originals.Sum(l=>l.Amount);var remainingKzt=originals.Sum(l=>l.AmountKzt!.Value);
            var moneyChanged=group.Any(e=>e.LineId is null||e.Amount!=old[e.LineId.Value].Amount);
            foreach(var e in group)
            {
                var source=old[e.SourceLineId];
                var grossKzt=moneyChanged?PairedMoneyAllocationPolicy.Allocate(remainingKzt,e.Amount,remaining):source.AmountKzt!.Value;
                remaining-=e.Amount;remainingKzt-=grossKzt;
                var row=new ExpenseRequestLine(e.LineId??Guid.NewGuid(),Id,source.CashFlowItemId,source.CostCenterId,
                    e.AccrualPeriod,e.Amount,e.LineId.HasValue?source.SortOrder:checked(++nextSort),e.VatRateCode,e.AccrualCostCenterId);
                // Allocate the frozen KZT gross, not a newly queried FX rate. The
                // final part consumes the exact paired remainder, including cents.
                var netKzt=PairedMoneyAllocationPolicy.Allocate(grossKzt,row.AmountExcludingVat!.Value,row.Amount);
                row.ApplyKztCalculation(grossKzt,grossKzt-netKzt,netKzt);
                if(!source.HasAbsentAccrualSnapshot())
                    row.ApplyInitialAccrualSnapshot(source.AccrualSnapshotFormsManagerialAccrual!.Value,source.AccrualSnapshotExpenseItemId);
                result.Add((e.LineId.HasValue?source:null,row));
            }
        }
        return result;
    }
}
