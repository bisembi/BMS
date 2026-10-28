using System.Text.Json;
using Bms.Infrastructure.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class TaskMailQueue(BmsDbContext db,TaskMailSource source,ITaskMailRecipientDirectory directory,ITaskMailTransport transport)
{
    public const string MessageType="bms.expense-task-mail.v1";
    public const string BudgetMessageType="bms.budget-task-mail.v1";
    public const string AdjustmentMessageType="bms.budget-adjustment-task-mail.v1";
    public const string StaffMessageType471="bms.staff-substitution-mail.v1";
    internal static string MessageTypeFor(string documentKind)=>documentKind switch
    {
        TaskMailDocumentKinds.ExpenseRequest=>MessageType,
        TaskMailDocumentKinds.BudgetInstance=>BudgetMessageType,
        TaskMailDocumentKinds.BudgetAdjustment=>AdjustmentMessageType,
        TaskMailDocumentKinds.StaffSubstitution=>StaffMessageType471,
        _=>throw new InvalidOperationException("Unknown mail document kind.")
    };
    // The internal Exchange endpoint rejects rapid submission bursts. Together
    // with the worker's one-minute pause, this bounded batch prevents a fresh
    // backlog from exhausting retries merely because it was sent at once.
    public const int MaximumMessagesPerCycle=5;
    internal const int PendingReadBatchSize=128;
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public Task<int> RunAsync(TaskMailDelivery settings,CancellationToken token)=>RunAsync(settings,null,token);
    public async Task<int> RunAsync(TaskMailDelivery settings,Func<TaskMailDelivery>? reloadSettings,CancellationToken token)
    {
        await db.Database.OpenConnectionAsync(token);var locked=false;
        try
        {
            var connection=db.Database.GetDbConnection();await using var acquire=connection.CreateCommand();
            acquire.Transaction=db.Database.CurrentTransaction?.GetDbTransaction();
            acquire.CommandText="DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'BMS:TaskMail:v1',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0; SELECT @r;";
            acquire.CommandTimeout=3;locked=Convert.ToInt32(await acquire.ExecuteScalarAsync(token))>=0;if(!locked)return 0;
            var tasks=await source.ReadAsync(token);
            foreach(var batch in tasks.Chunk(100))
            {
                var ids=batch.Select(t=>t.MailId).ToArray();var existing=await db.OutboxMessages.Where(x=>ids.Contains(x.Id)).Select(x=>x.Id).ToArrayAsync(token);
                var inserted415=new List<OutboxMessage>();
                foreach(var task in batch.Where(t=>!existing.Contains(t.MailId)))
                {
                    var row=new OutboxMessage {Id=task.MailId,OccurredAtUtc=DateTimeOffset.UtcNow,MessageType=MessageTypeFor(task.DocumentKind),
                        CorrelationId=task.MailId.ToString("N"),PayloadJson=JsonSerializer.Serialize(TaskMailEnvelope.From(task),Json)};
                    db.OutboxMessages.Add(row);inserted415.Add(row);
                }
                await db.SaveChangesAsync(token);
                foreach(var row in inserted415)db.Entry(row).State=EntityState.Detached;
            }
            // Standby captures durable messages but cannot touch SMTP or resolve
            // credentials. Existing unrelated outbox kinds are never dispatched.
            if(!settings.Enabled)return tasks.Count;
            var processed=0;
            DateTimeOffset? afterTime=null;var afterId=Guid.Empty;
            while(processed<MaximumMessagesPerCycle)
            {
            var pending=await ReadPendingBatch(afterTime,afterId).ToArrayAsync(token);
            if(pending.Length==0)break;
            // Advance using the database order, including rows that cannot be
            // retried. They must not starve eligible mail behind the first page.
            afterTime=pending[^1].OccurredAtUtc;afterId=pending[^1].Id;
            foreach(var row in pending)
            {
                token.ThrowIfCancellationRequested();
                settings=reloadSettings?.Invoke()??settings;if(!settings.Enabled)return tasks.Count;
                TaskMailEnvelope? envelope;
                try{envelope=JsonSerializer.Deserialize<TaskMailEnvelope>(row.PayloadJson,Json);}catch(JsonException){envelope=null;}
                if(envelope is null || envelope.DocumentKind is not(TaskMailDocumentKinds.ExpenseRequest or TaskMailDocumentKinds.BudgetInstance or TaskMailDocumentKinds.BudgetAdjustment or TaskMailDocumentKinds.StaffSubstitution)
                    ||MessageTypeFor(envelope.DocumentKind)!=row.MessageType)
                {row.LastError="INVALID_MAIL_ENVELOPE";await db.SaveChangesAsync(token);continue;}
                if(envelope.State=="Sending")
                {Set(row,envelope with {State="Uncertain"},"SMTP_OUTCOME_UNCERTAIN_NO_AUTOMATIC_RETRY");await db.SaveChangesAsync(token);continue;}
                if(envelope.State is "Uncertain" or "Failed"||envelope.NextAttemptUtc>DateTimeOffset.UtcNow)continue;
                if(envelope.State is not("Queued" or "Retry")||row.RetryCount>=6)
                {Set(row,envelope with {State="Failed"},"INVALID_MAIL_STATE_OR_RETRY_LIMIT");await db.SaveChangesAsync(token);continue;}
                if(processed>=MaximumMessagesPerCycle)break;processed++;
                // Re-read current roles, exact captured approval participant,
                // CFO authority, treasury stage, mapping and NSI grants before delivery.
                var current=(await source.ReadCurrentAsync(envelope,token)).SingleOrDefault(t=>t.MailId==row.Id&&envelope.Matches(t));
                if(current is null)
                {row.ProcessedAtUtc=DateTimeOffset.UtcNow;Set(row,envelope with {State="Stale"},"TASK_NO_LONGER_ASSIGNED");await db.SaveChangesAsync(token);continue;}
                string recipient;
                try{recipient=await directory.ResolveMailAsync(current.ActorExternalId,token);}
                catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
                catch(Exception error)when(error is not OutOfMemoryException and not StackOverflowException)
                {Retry(row,envelope,false,"RECIPIENT_MAIL_UNAVAILABLE");await db.SaveChangesAsync(token);continue;}
                // Directory IO is bounded but can take time. Check authority again.
                var verified=(await source.ReadCurrentAsync(envelope,token)).SingleOrDefault(t=>t.MailId==row.Id&&envelope.Matches(t));
                if(verified is null)
                {row.ProcessedAtUtc=DateTimeOffset.UtcNow;Set(row,envelope with {State="Stale"},"TASK_NO_LONGER_ASSIGNED");await db.SaveChangesAsync(token);continue;}
                if(verified.ActorExternalId!=current.ActorExternalId)
                {Retry(row,envelope,false,"RECIPIENT_MAPPING_CHANGED");await db.SaveChangesAsync(token);continue;}
                current=verified;settings=reloadSettings?.Invoke()??settings;if(!settings.Enabled)return tasks.Count;
                var originalPayload=row.PayloadJson;
                var sending=JsonSerializer.Serialize(envelope with {State="Sending",NextAttemptUtc=null},Json);
                // CAS also protects against an execution-strategy reconnect
                // losing the session lock while another worker claims this row.
                var claimed=await db.OutboxMessages.Where(x=>x.Id==row.Id&&x.MessageType==row.MessageType
                    &&x.ProcessedAtUtc==null&&x.PayloadJson==originalPayload)
                    .ExecuteUpdateAsync(set=>set.SetProperty(x=>x.PayloadJson,sending).SetProperty(x=>x.LastError,(string?)null),token);
                if(claimed!=1){db.Entry(row).State=EntityState.Detached;continue;}
                row.PayloadJson=sending;row.LastError=null;db.Entry(row).State=EntityState.Unchanged;
                try
                {
                    await transport.SendAsync(current,recipient,settings,token);
                    row.ProcessedAtUtc=DateTimeOffset.UtcNow;Set(row,envelope with {State="Sent",NextAttemptUtc=null},null);
                }
                catch(TaskMailDeliveryException error){Retry(row,envelope,error.Uncertain,error.Uncertain?"SMTP_OUTCOME_UNCERTAIN_NO_AUTOMATIC_RETRY":"SMTP_REJECTED_OR_CONNECT_FAILED");}
                catch(Exception error)when(error is not OutOfMemoryException and not StackOverflowException)
                {Retry(row,envelope,true,"SMTP_OUTCOME_UNCERTAIN_NO_AUTOMATIC_RETRY");}
                // If this commit fails, Sending stays durable. A restart holds
                // it as Uncertain instead of silently duplicating an accepted mail.
                await db.SaveChangesAsync(CancellationToken.None);
            }
            foreach(var row in pending)db.Entry(row).State=EntityState.Detached;
            }
            return tasks.Count;
        }
        finally
        {
            if(locked){await using var release=db.Database.GetDbConnection().CreateCommand();release.CommandTimeout=3;
                release.Transaction=db.Database.CurrentTransaction?.GetDbTransaction();
                release.CommandText="EXEC sys.sp_releaseapplock @Resource=N'BMS:TaskMail:v1',@LockOwner=N'Session';";await release.ExecuteNonQueryAsync(CancellationToken.None);}
            await db.Database.CloseConnectionAsync();
        }
    }
    internal IQueryable<OutboxMessage> ReadPendingBatch(DateTimeOffset? afterTime,Guid afterId)
    {
        var query=db.OutboxMessages.Where(x=>(x.MessageType==MessageType||x.MessageType==BudgetMessageType||x.MessageType==AdjustmentMessageType||x.MessageType==StaffMessageType471)&&x.ProcessedAtUtc==null);
        if(afterTime is { } time)query=query.Where(x=>x.OccurredAtUtc>time||(x.OccurredAtUtc==time&&x.Id.CompareTo(afterId)>0));
        return query.OrderBy(x=>x.OccurredAtUtc).ThenBy(x=>x.Id).Take(PendingReadBatchSize);
    }
    internal static void Retry(OutboxMessage row,TaskMailEnvelope envelope,bool uncertain,string code)
    {
        row.RetryCount++;
        Set(row,envelope with {State=uncertain?"Uncertain":row.RetryCount>=6?"Failed":"Retry",
            NextAttemptUtc=uncertain||row.RetryCount>=6?null:DateTimeOffset.UtcNow.AddMinutes(Math.Min(60,Math.Pow(2,row.RetryCount)))},code);
    }
    private static void Set(OutboxMessage row,TaskMailEnvelope value,string? error)
    {row.PayloadJson=JsonSerializer.Serialize(value,Json);row.LastError=error;}
}
