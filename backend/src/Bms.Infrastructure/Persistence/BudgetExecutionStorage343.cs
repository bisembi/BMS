using System.Data;
using System.Data.Common;
using System.Text.Json;
using Bms.Domain.Budgeting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

internal sealed record RegisterSourceValue343(string Kind,Guid SourceId,Guid CostCenterId,Guid CashFlowItemId,
    DateOnly BudgetMonth,decimal AmountKzt,string MovementName,string SourceVersion,string Evidence)
{
    public BudgetKey Key => BudgetKey.Create(CostCenterId,CashFlowItemId,BudgetMonth);
}
internal sealed record RegisterFrozenValue343(Guid RequestId,string FinancialFingerprint,DateTimeOffset ApprovedAtUtc,
    decimal PositiveDeficitKzt,int? ApprovalSnapshotNumber,string Evidence,string PositionsJson);
internal sealed record RegisterFrozenPosition343(Guid CostCenterId,Guid CashFlowItemId,DateOnly BudgetMonth,decimal PositiveDeficitKzt);
internal sealed record RegisterCashValue343(Guid CostCenterId,Guid CashFlowItemId,DateOnly BudgetMonth,
    decimal SourceCashKzt,decimal RepresentedBmsPaidKzt,Guid SnapshotId)
{
    public BudgetKey Key => BudgetKey.Create(CostCenterId,CashFlowItemId,BudgetMonth);
}
internal sealed record RegisterCashTransfer343(Guid LinkId,Guid RequestId,Guid CostCenterId,Guid CashFlowItemId,
    DateOnly BudgetMonth,decimal AmountKzt,decimal OriginalReservePaidKzt,Guid SnapshotId,string Evidence)
{
    public BudgetKey Key=>BudgetKey.Create(CostCenterId,CashFlowItemId,BudgetMonth);
}
internal sealed record RegisterSnapshot343(bool Enabled,long Revision,Guid? CashSnapshotId,string? CashGrossSha256,
    DateTimeOffset? CashCapturedAtUtc,DateOnly? CashCutoffExclusiveLocal,IReadOnlyList<RegisterSourceValue343> Values,
    IReadOnlyList<RegisterFrozenValue343> Frozen,IReadOnlyList<RegisterCashValue343> Cash)
{
    public IReadOnlyList<RegisterCashTransfer343> Transfers {get;init;}=[];
}

/// <summary>No connection outside the existing local BMS SQL context, no source
/// credentials, no arbitrary SQL payload from HTTP. All calls retain the caller's
/// serializable transaction and cancellation token.</summary>
internal sealed class BudgetExecutionStorage343(BmsDbContext db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<bool> ExistsAsync(CancellationToken token)
    {
        await using var q=Command("SELECT CASE WHEN OBJECT_ID(N'bms_execution343.Component',N'U') IS NULL THEN 0 ELSE 1 END");
        return Convert.ToInt32(await q.ExecuteScalarAsync(token))==1;
    }

    public async Task<bool> AcquireGateAsync(bool readOnly,CancellationToken token)
    {
        if(!await ExistsAsync(token))return false;
        await using(var gate=Command("DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource=N'BMS:BudgetExecution343',@LockMode=@mode,@LockOwner=N'Transaction',@LockTimeout=15000;SELECT @r;"))
        {
            Add(gate,"@mode",DbType.String,readOnly?"Shared":"Exclusive");
            if(Convert.ToInt32(await gate.ExecuteScalarAsync(token))<0)throw new InvalidOperationException("Budget register transaction gate unavailable.");
        }
        await using(var state=Command("SELECT Enabled FROM bms_execution343.Component WHERE Id=1 AND OwnerMarker='2ee40663-2e51-48eb-b138-aa295e9ee343' AND ComponentVersion=1"))
        {
            var value=await state.ExecuteScalarAsync(token);
            if(value is not bool enabled)throw new InvalidOperationException("Budget register identity unavailable.");
            if(!enabled)return false;
        }
        return true;
    }

    public async Task<RegisterSnapshot343> ReadAsync(CancellationToken token)
    {
        await using var q=Command("""
            SELECT Enabled,Revision,CashSnapshotId,CashGrossSHA256,CashCapturedAtUtc,CashCutoffExclusiveLocal
            FROM bms_execution343.Component WHERE Id=1 AND OwnerMarker='2ee40663-2e51-48eb-b138-aa295e9ee343' AND ComponentVersion=1;
            SELECT Kind,SourceId,CostCenterId,CashFlowItemId,BudgetMonth,AmountKzt,SourceVersion,Evidence FROM bms_execution343.SourceValues;
            SELECT RequestId,FinancialFingerprint,ApprovedAtUtc,PositiveDeficitKzt,ApprovalSnapshotNumber,Evidence,PositionsJson FROM bms_execution343.FrozenDecisions;
            SELECT CostCenterId,CashFlowItemId,BudgetMonth,SourceCashKzt,RepresentedBmsPaidKzt,SnapshotId FROM bms_execution343.CashCells;
            SELECT LinkId,RequestId,CostCenterId,CashFlowItemId,BudgetMonth,AmountKzt,OriginalReservePaidKzt,SnapshotId,Evidence FROM bms_execution343.CashTransfers;
            """);
        await using var r=await q.ExecuteReaderAsync(token);
        if(!await r.ReadAsync(token))throw new InvalidOperationException("Budget register header missing.");
        var enabled=r.GetBoolean(0);var revision=r.GetInt64(1);Guid? snapshot=r.IsDBNull(2)?null:r.GetGuid(2);
        var hash=r.IsDBNull(3)?null:r.GetString(3);DateTimeOffset? captured=r.IsDBNull(4)?null:r.GetFieldValue<DateTimeOffset>(4);
        DateOnly? cutoff=r.IsDBNull(5)?null:DateOnly.FromDateTime(r.GetDateTime(5));
        if(await r.ReadAsync(token)||!await r.NextResultAsync(token))throw new InvalidOperationException("Invalid register source results.");
        var values=new List<RegisterSourceValue343>();
        while(await r.ReadAsync(token))
        {
            if(values.Count>=100000)throw new InvalidOperationException("Register source bound exceeded.");
            values.Add(new(r.GetString(0),r.GetGuid(1),r.GetGuid(2),r.GetGuid(3),DateOnly.FromDateTime(r.GetDateTime(4)),r.GetDecimal(5),"",r.GetString(6),r.GetString(7)));
        }
        if(!await r.NextResultAsync(token))throw new InvalidOperationException("Register approval results missing.");
        var frozen=new List<RegisterFrozenValue343>();
        while(await r.ReadAsync(token))
        {
            if(frozen.Count>=100000)throw new InvalidOperationException("Register decision bound exceeded.");
            frozen.Add(new(r.GetGuid(0),r.GetString(1),r.GetFieldValue<DateTimeOffset>(2),r.GetDecimal(3),r.IsDBNull(4)?null:r.GetInt32(4),r.GetString(5),r.GetString(6)));
        }
        if(!await r.NextResultAsync(token))throw new InvalidOperationException("Register cash results missing.");
        var cash=new List<RegisterCashValue343>();
        while(await r.ReadAsync(token))
        {
            if(cash.Count>=100000)throw new InvalidOperationException("Register cash bound exceeded.");
            cash.Add(new(r.GetGuid(0),r.GetGuid(1),DateOnly.FromDateTime(r.GetDateTime(2)),r.GetDecimal(3),r.GetDecimal(4),r.GetGuid(5)));
        }
        if(!await r.NextResultAsync(token))throw new InvalidOperationException("Register cash transfers missing.");
        var transfers=new List<RegisterCashTransfer343>();
        while(await r.ReadAsync(token))
        {
            if(transfers.Count>=100000)throw new InvalidOperationException("Register transfer bound exceeded.");
            transfers.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetGuid(3),DateOnly.FromDateTime(r.GetDateTime(4)),r.GetDecimal(5),r.GetDecimal(6),r.GetGuid(7),r.GetString(8)));
        }
        if(await r.NextResultAsync(token))throw new InvalidOperationException("Unexpected register result.");
        return new RegisterSnapshot343(enabled,revision,snapshot,hash,captured,cutoff,values,frozen,cash){Transfers=transfers};
    }

    public async Task<(int Movements,int Frozen)> PostAsync(IReadOnlyCollection<RegisterSourceValue343> values,
        IReadOnlyCollection<RegisterFrozenValue343> frozen,Guid correlation,CancellationToken token)
    {
        foreach(var v in values)
        {
            if(v.Kind is not("Limit" or "Reserve" or "Paid")||v.AmountKzt<0m||decimal.Round(v.AmountKzt,2)!=v.AmountKzt
                ||v.BudgetMonth.Day!=1||v.SourceVersion.Length>128||v.MovementName.Length>120||v.Evidence.Length>4000)
                throw new InvalidOperationException("Invalid exact register posting value.");
        }
        await using var q=Command("bms_execution343.Post");q.CommandType=CommandType.StoredProcedure;
        Add(q,"@values",DbType.String,JsonSerializer.Serialize(values,Json));
        Add(q,"@frozen",DbType.String,JsonSerializer.Serialize(frozen,Json));Add(q,"@correlation",DbType.Guid,correlation);
        await using var r=await q.ExecuteReaderAsync(token);if(!await r.ReadAsync(token))throw new InvalidOperationException("Posting receipt missing.");
        return(r.GetInt32(0),r.GetInt32(1));
    }

    internal DbCommand Command(string text)
    {
        var transaction=db.Database.CurrentTransaction;
        if(transaction?.GetDbTransaction().IsolationLevel!=IsolationLevel.Serializable)
            throw new InvalidOperationException("Budget register requires the existing serializable business transaction.");
        var q=db.Database.GetDbConnection().CreateCommand();q.Transaction=transaction.GetDbTransaction();q.CommandTimeout=90;q.CommandText=text;return q;
    }

    public async Task AcceptCashAsync(DirectDdsSnapshot snapshot,IReadOnlyCollection<RegisterCashValue343> cells,CancellationToken token,
        IReadOnlyCollection<RegisterCashTransfer343>? transfers=null,IReadOnlyCollection<Guid>? controlled400=null)
    {
        snapshot.Validate();
        await using var q=Command(controlled400 is{Count:>0}?"bms_execution343.AcceptApprovedCash400":"bms_execution343.AcceptCash");q.CommandType=CommandType.StoredProcedure;
        Add(q,"@snapshotId",DbType.Guid,snapshot.Id);Add(q,"@grossSha256",DbType.String,snapshot.GrossHash);
        Add(q,"@capturedAtUtc",DbType.DateTimeOffset,snapshot.CapturedAtUtc);
        Add(q,"@cutoffExclusiveLocal",DbType.Date,snapshot.CutoffExclusiveLocal.ToDateTime(TimeOnly.MinValue));
        Add(q,"@cells",DbType.String,JsonSerializer.Serialize(cells,Json));
        Add(q,"@transfers",DbType.String,JsonSerializer.Serialize(transfers??[],Json));
        if(controlled400 is{Count:>0})Add(q,"@controlledTransferIds",DbType.String,JsonSerializer.Serialize(controlled400));
        await q.ExecuteNonQueryAsync(token);
    }
    internal static void Add(DbCommand q,string name,DbType type,object? value)
    {
        var p=q.CreateParameter();p.ParameterName=name;p.DbType=type;p.Value=value??DBNull.Value;q.Parameters.Add(p);
    }
}
