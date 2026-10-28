using System.Data;
using Bms.Application.ExpenseRequests;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class DebtorsReader470(BmsDbContext db):IDebtorsReader470
{
    public async Task<IReadOnlyList<DebtorRequest470>> ReadAsync(string actor,DebtorsQuery470 query,CancellationToken token)
    {
        if(db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel!=IsolationLevel.Serializable)
            throw new DebtorsReportUnavailable470();
        query.Validate();
        var scope=await BusinessDataReadScopeQuery.ResolveAsync(db,actor,token);
        // Same authoritative, role/CFO-aware list scope. A caller's full-read
        // hint, organization filter or employee label never grants authority.
        var visible=ExpenseRequestVisibilityQuery.ApplyCapturedListScope(db,db.ExpenseRequests.AsNoTracking(),actor,scope,false);
        var rows=await Filter(visible,query).OrderBy(r=>r.Id).Select(r=>new DebtorRequest470(r.Id,r.InitiatorExternalId,
            r.CurrencyCode,r.Lines.Sum(l=>l.Amount),
            r.Lines.All(l=>l.AmountKzt.HasValue)&&(r.Lines.Any()||r.CurrencyCode=="KZT")?r.Lines.Sum(l=>l.AmountKzt):null))
            .Take(20001).ToArrayAsync(token);
        if(rows.Length>20000)throw new DebtorsReportUnavailable470();
        return rows;
    }
    internal static IQueryable<ExpenseRequest> Filter(IQueryable<ExpenseRequest> q,DebtorsQuery470 f)
    {
        q=q.Where(r=>r.Status==ExpenseRequestStatus.Paid);
        if(f.OrganizationId is{} organization)q=q.Where(r=>r.OrganizationId==organization);
        if(f.CostCenterId is{} center)q=q.Where(r=>r.DefaultCostCenterId==center);
        if(f.CounterpartyId is{} party)q=q.Where(r=>r.CounterpartyId==party);
        if(f.PlannedPaymentDate is{} day)q=q.Where(r=>r.PlannedPaymentDate==day);
        if(f.IsUrgent is{} urgent)q=q.Where(r=>r.IsUrgent==urgent);
        return q;
    }
}
