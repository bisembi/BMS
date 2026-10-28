using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Domain.Budgeting;
using Bms.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed record ApprovedAccountingTarget400(Guid ExpenseRequestId, string RequestNumber,
    Guid RequestVersion, decimal CashAmount, decimal RoundingAmount);
public sealed record ApprovedAccountingPlan400(Guid ApprovalId, string DocumentKey, string FinancialHash,
    string ApprovedBy, string Reason, IReadOnlyList<ApprovedAccountingTarget400> Targets);
public sealed record ApprovedAccountingLine400(Guid LineId, int SortOrder, Guid CostCenterId,
    Guid CashFlowItemId, DateOnly BudgetMonth, decimal ReleasedOriginalKzt);
public sealed record ApprovedAccountingAllocation400(Guid ExpenseRequestId, string RequestNumber,
    Guid RequestVersion, Guid OrganizationId, Guid CounterpartyId, Guid CurrencyId,
    Guid EventId, decimal ObligationAmount, decimal CashAmount, decimal RoundingAmount,
    IReadOnlyList<ApprovedAccountingLine400> Lines);
public sealed record ApprovedAccountingGroup400(Guid ApprovalId, string DocumentKey, string FinancialHash,
    Guid SourceCaptureId, Guid DdsCaptureId, decimal SourceCashAmount, DateOnly PaymentDay,
    DateTimeOffset PaidAtUtc, DateTimeOffset ApprovedAtUtc, string ApprovedBy, string Reason,
    IReadOnlyList<AccountingSourceCashLeg> Legs, IReadOnlyList<ApprovedAccountingAllocation400> Allocations);

/// <summary>Small, explicitly approved exceptions are stored in the existing
/// append-only audit, not in primary 1C or fabricated bank facts. The unique
/// document pool and deterministic event ids are checked under the same capture
/// lock/Serializable transaction as normal intake. No new mutation endpoint.</summary>
internal static class ApprovedAccountingEvidence400
{
    internal const string ObjectType = "AccountingAllocation400";
    internal const string Approved = "AccountingAllocationApproved";
    internal const string Reversed = "AccountingAllocationReversed";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task<IReadOnlyList<ApprovedAccountingGroup400>> ReadAsync(BmsDbContext db,
        CancellationToken token, bool includeReversed = false)
    {
        var query=db.BusinessAuditEntries.AsNoTracking().Where(a=>a.ObjectType==ObjectType);
        var sizes=await query.Select(a=>new{a.Id,Bytes=(long?)EF.Functions.DataLength(a.AfterJson)})
            .Take(2001).ToArrayAsync(token);
        if(sizes.Length>2000 || sizes.Any(s=>s.Bytes is null or >2000000) || sizes.Sum(s=>s.Bytes??0)>8000000)
            throw new InvalidOperationException("Bounded approved allocation evidence required.");
        if(sizes.Length==0)return [];
        var rows = await query.Take(2001).ToArrayAsync(token);
        if (rows.Length > 2000) throw new InvalidOperationException("Approved allocation archive review required.");
        if (rows.Any(r => r.Action != Approved && r.Action != Reversed))
            throw new InvalidOperationException("Unknown approved allocation evidence.");
        var approvals = rows.Where(r => r.Action == Approved).ToArray();
        if (approvals.Select(r => r.ObjectId).Distinct(StringComparer.Ordinal).Count() != approvals.Length)
            throw new InvalidOperationException("Duplicate allocation approval.");
        var groups = approvals.Select(r =>
        {
            if (r.AfterJson.Length > 1000000) throw new InvalidOperationException("Approved allocation evidence too large.");
            var group = JsonSerializer.Deserialize<ApprovedAccountingGroup400>(r.AfterJson, Json)
                ?? throw new InvalidOperationException("Approved allocation evidence missing.");
            Validate(group);
            if (r.ObjectId != group.ApprovalId.ToString("D") || r.ActorExternalId != group.ApprovedBy)
                throw new InvalidOperationException("Allocation approval identity differs.");
            return group;
        }).ToArray();
        if (groups.Select(g => g.DocumentKey).Distinct(StringComparer.Ordinal).Count() != groups.Length)
            throw new InvalidOperationException("One primary document cannot be independently approved twice.");
        if(groups.Sum(g=>g.Allocations.Count)>2000)
            throw new InvalidOperationException("Approved allocation archive review required.");
        var reversed = rows.Where(r => r.Action == Reversed).ToArray();
        if (reversed.Select(r => r.ObjectId).Distinct().Count() != reversed.Length
            || reversed.Any(r => !approvals.Any(a => a.ObjectId == r.ObjectId)))
            throw new InvalidOperationException("Allocation reversal without a unique approval.");
        var inactive = reversed.Select(r => r.ObjectId).ToHashSet(StringComparer.Ordinal);
        return includeReversed ? groups : groups.Where(g => !inactive.Contains(g.ApprovalId.ToString("D"))).ToArray();
    }

    internal static void Validate(ApprovedAccountingGroup400 group)
    {
        if (group.ApprovalId == Guid.Empty || group.SourceCaptureId == Guid.Empty || group.DdsCaptureId == Guid.Empty
            || string.IsNullOrWhiteSpace(group.DocumentKey) || group.FinancialHash.Length != 64
            || !group.FinancialHash.All(Uri.IsHexDigit) || !group.ApprovedBy.StartsWith("S-1-5-", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(group.Reason) || group.Reason.Length > 2000
            || group.Legs.Count is < 1 or > 2000 || group.Legs.Any(l => l.DocumentKey != group.DocumentKey || l.AmountKzt <= 0)
            || group.Legs.Select(l => l.LineNo).Distinct().Count() != group.Legs.Count
            || group.Legs.Sum(l => l.AmountKzt) != group.SourceCashAmount
            || group.PaymentDay != DateOnly.FromDateTime(group.PaidAtUtc.ToOffset(TimeSpan.FromHours(5)).DateTime))
            throw new InvalidOperationException("Invalid approved primary document pool.");
        ConfirmedPaymentEventContract.ValidateProcessingTime(group.PaidAtUtc);
        ConfirmedPaymentEventContract.ValidateProcessingTime(group.ApprovedAtUtc);
        if (group.PaidAtUtc > group.ApprovedAtUtc) throw new InvalidOperationException("Future cash approval refused.");
        ApprovedCashAllocationPolicy400.Validate(group.SourceCashAmount, group.Allocations.Select(a =>
            new ApprovedCashSlice400(a.ExpenseRequestId, a.ObligationAmount, a.CashAmount, a.RoundingAmount)).ToArray());
        var lineIds = new HashSet<Guid>();
        var eventIds = new HashSet<Guid>();
        foreach (var a in group.Allocations)
        {
            if (a.OrganizationId == Guid.Empty || a.CounterpartyId == Guid.Empty || a.CurrencyId == Guid.Empty
                || a.RequestVersion == Guid.Empty || string.IsNullOrWhiteSpace(a.RequestNumber)
                || a.EventId == Guid.Empty || !eventIds.Add(a.EventId) || a.Lines.Count is < 1 or > 1000
                || a.Lines.Any(l => l.LineId == Guid.Empty || !lineIds.Add(l.LineId)
                    || l.ReleasedOriginalKzt <= 0 || l.BudgetMonth.Day != 1
                    || l.CostCenterId == Guid.Empty || l.CashFlowItemId == Guid.Empty)
                || a.Lines.Sum(l => l.ReleasedOriginalKzt) != a.ObligationAmount)
                throw new InvalidOperationException("Invalid approved request allocation.");
        }
    }

    internal static IReadOnlyList<ApprovedPaymentRounding400> Rounding(
        IReadOnlyList<ApprovedAccountingGroup400> groups, Guid requestId) => groups
        .SelectMany(g => g.Allocations.Where(a => a.ExpenseRequestId == requestId && a.RoundingAmount > 0)
            .Select(a => new ApprovedPaymentRounding400(g.ApprovalId, requestId, a.CurrencyId,
                a.EventId, a.RoundingAmount, g.PaidAtUtc, g.ApprovedAtUtc))).ToArray();

    internal static async Task<IReadOnlyList<ApprovedPaymentRounding400>> ReadRoundingAsync(
        BmsDbContext db, Guid requestId, CancellationToken token) => Rounding(await ReadAsync(db, token), requestId);

    internal static BudgetPaymentShare343[] Shares(ApprovedAccountingAllocation400 allocation) => allocation.Lines
        .Select(l => new BudgetPaymentShare343(l.LineId, l.SortOrder,
            BudgetKey.Create(l.CostCenterId, l.CashFlowItemId, l.BudgetMonth), l.ReleasedOriginalKzt)).ToArray();

    internal static async Task<IReadOnlyList<ApprovedPaymentRounding400>> ReadValidatedRoundingAsync(
        BmsDbContext db, CancellationToken token)
    {
        var groups=await ReadAsync(db,token);
        var ids=groups.SelectMany(g=>g.Allocations).Where(a=>a.RoundingAmount>0)
            .Select(a=>a.ExpenseRequestId).Distinct().ToArray();
        if(ids.Length==0)return [];
        var requests=await db.ExpenseRequests.AsNoTracking().Include(r=>r.Lines).Where(r=>ids.Contains(r.Id)).ToArrayAsync(token);
        var facts=await db.ExpenseRequestPaymentFacts.AsNoTracking().Where(f=>ids.Contains(f.ExpenseRequestId)).ToArrayAsync(token);
        var currency=await db.Currencies.AsNoTracking().SingleAsync(c=>c.Code=="KZT",token);
        if(requests.Length!=ids.Length)throw new InvalidOperationException("Rounding target missing.");
        var result=new List<ApprovedPaymentRounding400>();
        foreach(var request in requests)
        {
            var evidence=Rounding(groups,request.Id);
            var events=facts.Where(f=>f.ExpenseRequestId==request.Id).Select(f=>new ConfirmedPaymentEvent(f.SourceSystem,
                f.ExternalId,f.EventId,f.ExpenseRequestId,f.CurrencyId,f.Amount,f.AmountKzt,f.PaidAt,f.PaymentNumber,
                f.IsReversal,f.ReversedEventId)).ToArray();
            if(request.CurrencyCode!="KZT")throw new InvalidOperationException("Rounding requires the approved KZT scope.");
            ApprovedPaymentRoundingPolicy400.Validate(request.Id,currency.Id,request.TotalAmount,
                PaymentFactLedgerReconciler.Reconcile(request.Id,currency.Id,events),evidence);
            result.AddRange(evidence);
        }
        return result;
    }
}
