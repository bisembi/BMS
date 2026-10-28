using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bms.Application.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

internal sealed record RegisterBankLeg343(AccountingSourceCashLeg Leg,Guid CashFlowItemId);

/// <summary>One fixed, read-only, same-server EXEC; no source-system writes,
/// arbitrary SQL or catalog selection from HTTP/configuration.</summary>
internal sealed class BudgetExecutionBankSource343(BmsDbContext db,AccountingReplicaLocation? location=null)
{
    private static readonly Regex Key=new("^(ExampleAccountingA|ExampleAccountingB|ExampleAccountingC)\\|[0-9A-F]{8}\\|[0-9A-F]{32}$",RegexOptions.CultureInvariant);
    public async Task<IReadOnlyList<RegisterBankLeg343>> ReadAsync(Guid snapshotId,IReadOnlyCollection<string> documentKeys,CancellationToken ct)
    {
        if(snapshotId==Guid.Empty||documentKeys.Count>100000||documentKeys.Any(k=>!Key.IsMatch(k)))
            throw new InvalidOperationException("Exact bounded bank coverage request required.");
        if(documentKeys.Count==0)return [];
        var b=new SqlConnectionStringBuilder(db.Database.GetConnectionString())
            {InitialCatalog=(location??new()).Catalog,ApplicationName="BMS-BudgetExecution343-ReadOnly",Pooling=false,ConnectTimeout=15};
#if BMS_OWNED419_ACCEPTANCE
        AccountingReplicaLocation.RequireReader419(b);
#else
        if(!b.IntegratedSecurity||b.UserID.Length!=0||b.Password.Length!=0)throw new InvalidOperationException("Integrated bank coverage connection required.");
#endif
        await using var connection=new SqlConnection(b.ConnectionString);await connection.OpenAsync(ct);
        await using var command=connection.CreateCommand();command.CommandType=CommandType.StoredProcedure;
        command.CommandText="dbo.ReadAccountingBudgetCoverage455";command.CommandTimeout=60;
        command.Parameters.Add("@SnapshotId",SqlDbType.UniqueIdentifier).Value=snapshotId;
        command.Parameters.Add("@DocumentKeysJson",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(documentKeys.Distinct(StringComparer.Ordinal));
        await using var reader=await command.ExecuteReaderAsync(ct);
        var keys=documentKeys.ToHashSet(StringComparer.Ordinal);var rows=new List<RegisterBankLeg343>();
        while(await reader.ReadAsync(ct))
        {
            var leg=new AccountingSourceCashLeg(reader.GetString(0),reader.GetInt32(1),AccountingPaymentSourceReader.NormalizeTime(reader.GetString(2)),reader.GetDecimal(3));
            if(!keys.Contains(leg.DocumentKey)||leg.LineNo<1||leg.AmountKzt<=0m||decimal.Round(leg.AmountKzt,2)!=leg.AmountKzt
                ||reader.IsDBNull(4)||reader.GetGuid(4)==Guid.Empty||rows.Count>=200000)
                throw new InvalidOperationException("Verified source bank article and exact tiyn amount required.");
            rows.Add(new(leg,reader.GetGuid(4)));
        }
        if(await reader.NextResultAsync(ct)||rows.GroupBy(r=>(r.Leg.DocumentKey,r.Leg.LineNo)).Any(g=>g.Count()!=1))
            throw new InvalidOperationException("Duplicate or unexpected bank budget evidence.");
        return rows;
    }
}
