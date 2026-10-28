using System.Data;
using Bms.Application.Authorization;
using Bms.Application.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class ReturnedRequestNoticeReader377(BmsDbContext db,TaskMailSource source):IReturnedRequestNoticeReader377
{
    public async Task<ReturnedRequestNoticePage377> ReadAsync(string actor,BusinessDataReadScope scope,int skip,int take,CancellationToken token)
    {
        if(db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel!=IsolationLevel.Serializable)
            throw new InvalidOperationException("Notification reads require a Serializable transaction.");
        var rows=await source.ReadReturned377Async(null,null,actor,token);
        var ids=rows.Select(t=>t.RequestId).Distinct().ToArray();
        var visible=await BusinessDataReadScopeQuery.ApplyCapturedRequests(db.ExpenseRequests.AsNoTracking(),scope)
            .Where(r=>ids.Contains(r.Id)).Select(r=>r.Id).ToArrayAsync(token);
        var notices=rows.Where(t=>visible.Contains(t.RequestId)).OrderByDescending(t=>t.ReturnedAtUtc).ThenBy(t=>t.MailId).ToArray();
        return new(notices.Skip(skip).Take(take).Select(t=>new ReturnedRequestNotice377(t.MailId,t.RequestId,t.Number,t.Organization,t.CostCenter,
            t.ReturnedAtUtc??throw new InvalidOperationException("Return date missing."),t.ReturnReason)).ToArray(),skip,take,notices.Length);
    }
}
