using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Application.Authorization;
using Bms.Application.Treasury;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// Reads the same sealed cash and detail captures as ODDS, not just request
/// reconciliation. Article/CFO authority is resolved inside each transaction.
public sealed class GoodsCalendarOddsReader469(BmsDbContext db,ICurrentActor actor,
    IDirectDdsReplicaReader source,AccountingReplicaLocation location):IGoodsCalendarActuals469
{
    public async Task<GoodsCalendarActuals469> ReadAsync(PaymentCalendarGridQuery query,
        IReadOnlyDictionary<Guid,string> articles,IReadOnlyList<DatedPaymentCalendarRequestCapture> captures,CancellationToken token)
    {
        ActorIdentity.RequireBusinessDataAccess(actor);query.Validate();
        if(articles.Count!=3)throw new PaymentCalendarUnavailableException();
        var scope=await actor.ResolveBusinessDataReadScopeAsync(token);
        var articleIds=articles.Keys.ToArray();
        var directory=await db.CashFlowItems.AsNoTracking().Where(i=>articleIds.Contains(i.Id))
            .Select(i=>new{i.Id,i.CostCenterId}).Take(4).ToArrayAsync(token);
        if(directory.Length!=3)throw new PaymentCalendarUnavailableException();
        var ids=directory.Where(i=>CanReadArticle469(scope,i.CostCenterId)).Select(i=>i.Id).ToArray();
        if(ids.Length==0)return new(null,null,[]);
        var snapshot=await CurrentDdsActualsSource.ReadRequiredAsync(source,token);
        var connectionString=new SqlConnectionStringBuilder(db.Database.GetConnectionString())
        {InitialCatalog=location.Catalog,Pooling=false,ConnectTimeout=15,ApplicationName="BMS-GoodsCalendar469-ReadOnly"};
        if(!connectionString.IntegratedSecurity||connectionString.UserID.Length!=0||connectionString.Password.Length!=0)
            throw new PaymentCalendarUnavailableException();
        await using var connection=new SqlConnection(connectionString.ConnectionString);await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();command.CommandType=CommandType.StoredProcedure;
        command.CommandText="dbo.ReadGoodsCalendarActuals469";command.CommandTimeout=30;
        command.Parameters.Add("@SnapshotId",SqlDbType.UniqueIdentifier).Value=snapshot.Id;
        command.Parameters.Add("@ArticleIds",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(ids);
        command.Parameters.Add("@FromDay",SqlDbType.Date).Value=query.From.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@ThroughDay",SqlDbType.Date).Value=query.Through.ToDateTime(TimeOnly.MinValue);
        var raw=new List<(Guid Article,string Key,DateOnly Day,string Document,string Party,decimal Amount)>();
        await using(var r=await command.ExecuteReaderAsync(token))
        {
            if(!await r.ReadAsync(token)||r.GetGuid(0)!=snapshot.Id||r.GetInt32(1)>PaymentCalendarExportPolicy.MaximumRows)
                throw new PaymentCalendarUnavailableException();
            var count=r.GetInt32(1);var total=r.GetDecimal(2);
            if(count<0||!await r.NextResultAsync(token))throw new PaymentCalendarUnavailableException();
            var unique=new HashSet<string>(StringComparer.Ordinal);decimal sum=0;
            while(await r.ReadAsync(token))
            {
                if(raw.Count>=count)throw new PaymentCalendarUnavailableException();
                var key=r.GetString(1);var line=r.GetInt32(2);var direction=r.GetString(3);
                var identity=key+"|"+line+"|"+direction;
                if(!unique.Add(identity)||line<1||direction is not("Ct" or "Dt"))throw new PaymentCalendarUnavailableException();
                var article=r.GetGuid(0);var day=DateOnly.FromDateTime(r.GetDateTime(4));var amount=r.GetDecimal(7);
                if(!ids.Contains(article)||day<query.From||day>query.Through||amount==0
                    ||(direction=="Ct"?amount<0:amount>0))throw new PaymentCalendarUnavailableException();
                sum=checked(sum+amount);
                raw.Add((article,identity,day,r.GetString(5),r.GetString(6),amount));
            }
            if(raw.Count!=count||sum!=total)throw new PaymentCalendarUnavailableException();
        }
        // Exact persisted links only; neither amount nor counterparty names
        // are used to invent a request. Shared allocations remain independent.
        var docKeys=raw.Select(r=>r.Key[..r.Key.LastIndexOf('|')]).Select(k=>k[..k.LastIndexOf('|')])
            .Distinct(StringComparer.Ordinal).ToArray();
        var links=await db.AccountingPaymentLinks.AsNoTracking().Where(l=>l.ReversedAtUtc==null&&docKeys.Contains(l.DocumentKey))
            .Select(l=>new{l.DocumentKey,l.ExpenseRequestId}).Take(PaymentCalendarExportPolicy.MaximumRows+1).ToArrayAsync(token);
        if(links.Length>PaymentCalendarExportPolicy.MaximumRows||links.GroupBy(l=>l.DocumentKey,StringComparer.Ordinal).Any(g=>g.Count()!=1))
            throw new PaymentCalendarUnavailableException();
        var byKey=links.ToDictionary(l=>l.DocumentKey,l=>l.ExpenseRequestId,StringComparer.Ordinal);
        var visible=captures.ToDictionary(c=>c.Request.Id);
        var entries=raw.Select(r=>
        {
            var last=r.Key.LastIndexOf('|');var docKey=r.Key[..r.Key.LastIndexOf('|',last-1)];
            var requestId=byKey.TryGetValue(docKey,out var id)&&visible.ContainsKey(id)?(Guid?)id:null;
            return new GoodsCalendarActual469(r.Article,new(r.Day,r.Amount<0?"BANK_REVERSAL":"BANK_PAYMENT",requestId,
                "KZT",r.Amount,r.Amount,string.IsNullOrWhiteSpace(r.Party)?null:r.Party,requestId.HasValue&&visible[requestId.Value].CanOpen)
            {Source="ODDS",SourceEntryId=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(r.Key))),DocumentNumber=r.Document});
        }).ToArray();
        return new(snapshot.Id,snapshot.CapturedAtUtc,entries);
    }
    internal static bool CanReadArticle469(BusinessDataReadScope scope,Guid? costCenterId)=>
        scope.CanReadAll||costCenterId is{} id&&scope.CostCenterIds.Contains(id);
}
