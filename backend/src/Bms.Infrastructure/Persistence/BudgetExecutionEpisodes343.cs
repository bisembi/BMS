using System.Security.Cryptography;
using System.Text;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

internal sealed record BudgetExecutionEpisode343(Guid? ReturnAuditId,DateTimeOffset? ReturnedAtUtc,DateTimeOffset QueueAtUtc)
{
    internal string? Fingerprint(string? financial) => financial is null ? null : ReturnAuditId is null ? financial
        : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"BMS-BudgetEpisode343\n{financial}\n{ReturnAuditId:D}")));
}

/// <summary>SQL audit is the durable submission-cycle source. Return releases
/// the slot; resubmission uses its new time, not the first-submission display
/// date. Existing audit and approval history are never rewritten.</summary>
internal static class BudgetExecutionEpisodes343
{
    internal static async Task<IReadOnlyDictionary<Guid,BudgetExecutionEpisode343>> ReadAsync(BmsDbContext db,
        IReadOnlyCollection<ExpenseRequest> requests,CancellationToken token)
    {
        var ids=requests.Select(r=>r.Id.ToString("D")).ToArray();
        var rows=await db.BusinessAuditEntries.AsNoTracking().Where(a=>a.ObjectType=="ExpenseRequest"&&ids.Contains(a.ObjectId)
            &&(a.Action=="WorkflowReturned"||a.Action=="SubmittedForApproval"||a.Action=="SubmittedApproved"
                ||a.Action=="SubmittedToPayment"||a.Action=="SubmittedToNsi"))
            .OrderBy(a=>a.OccurredAtUtc).ThenBy(a=>a.Id).Take(100001).ToArrayAsync(token);
        if(rows.Length>100000)throw new InvalidOperationException("Submission episode evidence bound exceeded.");
        var grouped=rows.GroupBy(a=>Guid.Parse(a.ObjectId)).ToDictionary(g=>g.Key,g=>g.ToArray());
        var result=new Dictionary<Guid,BudgetExecutionEpisode343>();var now=DateTimeOffset.UtcNow;
        foreach(var request in requests)
        {
            var audit=grouped.GetValueOrDefault(request.Id)??[];
            var returned=audit.LastOrDefault(a=>a.Action=="WorkflowReturned");
            var submitted=returned is null?null:audit.LastOrDefault(a=>a.Action!="WorkflowReturned"&&a.OccurredAtUtc>=returned.OccurredAtUtc);
            result.Add(request.Id,new(returned?.Id,returned?.OccurredAtUtc,
                returned is null?request.CreatedAtUtc:submitted?.OccurredAtUtc??now));
        }
        return result;
    }
}
