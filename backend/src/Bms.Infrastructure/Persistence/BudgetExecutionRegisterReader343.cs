using System.Data;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Application.Budgeting;

namespace Bms.Infrastructure.Persistence;

/// <summary>Administrator-only append-only register reader. HTTP cannot post,
/// rebuild or mutate the register. Window balances are computed over the whole
/// key history before a request filter or page is applied.</summary>
public sealed class BudgetExecutionRegisterReader343(BmsDbContext db):IBudgetExecutionRegisterReader343
{
    public async Task<BudgetRegisterPage343> ReadAsync(DateOnly month,Guid? costCenterId,Guid? articleId,Guid? requestId,
        int skip,int take,CancellationToken ct)
    {
        if(month.Day!=1||month.Year is <2000 or >2100||skip<0||take is <1 or >1000
            ||costCenterId==Guid.Empty||articleId==Guid.Empty||requestId==Guid.Empty)throw new ArgumentException("Invalid register filter.");
            if(!db.BudgetRegisterEnabled343)throw new InvalidOperationException("Budget register not enabled.");
            var storage=new BudgetExecutionStorage343(db);
            await using var q=storage.Command("""
                SELECT m.*,SUM(m.AmountKzt) OVER(PARTITION BY m.CostCenterId,m.CashFlowItemId,m.BudgetMonth ORDER BY m.Sequence ROWS UNBOUNDED PRECEDING) BalanceAfterKzt
                INTO #capture343 FROM bms_execution343.Movements m
                WHERE m.BudgetMonth=@month AND (@cfo IS NULL OR m.CostCenterId=@cfo) AND (@article IS NULL OR m.CashFlowItemId=@article);
                SELECT m.Sequence,m.PostedAtUtc,m.BudgetMonth,COALESCE(c.Code COLLATE DATABASE_DEFAULT+N' ',N'')+COALESCE(c.Name COLLATE DATABASE_DEFAULT,N'—'),
                    COALESCE(a.Code COLLATE DATABASE_DEFAULT+N' ',N'')+COALESCE(a.Name COLLATE DATABASE_DEFAULT,N'—'),
                    CASE WHEN r.Status NOT IN('DRAFT','CANCELLED','REJECTED') AND NULLIF(LTRIM(RTRIM(r.RequestNumber)),N'') IS NOT NULL
                        AND NOT EXISTS(SELECT 1 FROM bms_web.BusinessAudit d WHERE d.ObjectType='ExpenseRequest' AND d.Action='ExpenseRequestDraftDeleted'
                            AND d.ObjectId=CONVERT(nvarchar(36),r.Id)) THEN r.Id END,
                    COALESCE(NULLIF(LTRIM(RTRIM(r.RequestNumber)),N'') COLLATE DATABASE_DEFAULT,
                        JSON_VALUE(CASE WHEN ISJSON(m.Evidence)=1 THEN m.Evidence ELSE N'{}' END,'$.number') COLLATE DATABASE_DEFAULT,
                        JSON_VALUE(CASE WHEN ISJSON(m.Evidence)=1 THEN m.Evidence ELSE N'{}' END,'$.requestNumber') COLLATE DATABASE_DEFAULT),m.MovementName,
                    m.LimitDeltaKzt,m.ReservedDeltaKzt,m.PaidDeltaKzt,m.AmountKzt,m.BalanceAfterKzt,m.SourceVersion,m.Evidence
                FROM #capture343 m LEFT JOIN bms_web.CostCenters c ON c.Id=m.CostCenterId
                    LEFT JOIN bms_web.CashFlowItems a ON a.Id=m.CashFlowItemId LEFT JOIN bms_web.ExpenseRequests r ON r.Id=m.RequestId
                WHERE (@request IS NULL OR m.RequestId=@request) ORDER BY m.Sequence DESC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
                SELECT COUNT(*) FROM #capture343 WHERE (@request IS NULL OR RequestId=@request);
                SELECT COALESCE(SUM(LimitDeltaKzt),0),COALESCE(SUM(ReservedDeltaKzt),0),COALESCE(SUM(PaidDeltaKzt),0),COALESCE(SUM(AmountKzt),0)
                FROM #capture343;
                SELECT Revision,CashCapturedAtUtc FROM bms_execution343.Component WHERE Id=1;
                """);
            BudgetExecutionStorage343.Add(q,"@month",DbType.Date,month.ToDateTime(TimeOnly.MinValue));
            BudgetExecutionStorage343.Add(q,"@cfo",DbType.Guid,costCenterId);BudgetExecutionStorage343.Add(q,"@article",DbType.Guid,articleId);
            BudgetExecutionStorage343.Add(q,"@request",DbType.Guid,requestId);BudgetExecutionStorage343.Add(q,"@skip",DbType.Int32,skip);
            BudgetExecutionStorage343.Add(q,"@take",DbType.Int32,take);
            await using var r=await q.ExecuteReaderAsync(ct);var items=new List<BudgetRegisterMovement343>();
            while(await r.ReadAsync(ct))items.Add(new(r.GetInt64(0),r.GetFieldValue<DateTimeOffset>(1),DateOnly.FromDateTime(r.GetDateTime(2)),
                r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:r.GetGuid(5),r.IsDBNull(6)?null:r.GetString(6),r.GetString(7),
                r.GetDecimal(8),r.GetDecimal(9),r.GetDecimal(10),r.GetDecimal(11),r.GetDecimal(12),r.GetString(13),r.GetString(14)));
            if(!await r.NextResultAsync(ct)||!await r.ReadAsync(ct))throw new InvalidOperationException("Register count missing.");
            var total=r.GetInt32(0);
            if(!await r.NextResultAsync(ct)||!await r.ReadAsync(ct))throw new InvalidOperationException("Register totals missing.");
            var totals=new BudgetRegisterTotals343(r.GetDecimal(0),r.GetDecimal(1),r.GetDecimal(2),r.GetDecimal(3));
            if(totals.BalanceKzt!=totals.LimitKzt-totals.ReserveKzt-totals.PaidKzt)throw new InvalidOperationException("Register totals drift.");
            if(!await r.NextResultAsync(ct)||!await r.ReadAsync(ct))throw new InvalidOperationException("Register version missing.");
            return new BudgetRegisterPage343(items,skip,take,total,r.GetInt64(0),r.IsDBNull(1)?null:r.GetFieldValue<DateTimeOffset>(1),totals);
    }
}
