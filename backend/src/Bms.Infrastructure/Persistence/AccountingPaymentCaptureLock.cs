using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

internal static class AccountingPaymentCaptureLock
{
    // Same database-scoped resource as the existing DDS publisher. Hold until
    // the business transaction commits, so admission cannot mix generations.
    internal static async Task AcquireAsync(BmsDbContext db, bool payment, CancellationToken token, AccountingReplicaLocation? location=null)
    {
        if (db.Database.CurrentTransaction is null) return;
        var resource = payment ? "BMS-AccountingPaymentCapture258" : "BMS-DirectDDS-Publish-v1";
#if BMS_OWNED419_ACCEPTANCE
        var catalog419 = (location ?? new AccountingReplicaLocation()).Catalog;
        var lockSql419 = catalog419 switch
        {
            "BmsAccrualEdit311_Test_415" => """
            DECLARE @result int;
            EXEC @result=[BmsAccrualEdit311_Test_415].sys.sp_getapplock @Resource=@resource,
                @LockMode=N'Shared',@LockOwner=N'Transaction',@LockTimeout=15000;
            IF @result<0 THROW 65840,N'Owned accounting source publication is busy.',1;
            """,
            "BmsReadiness407_Test_415" => """
            DECLARE @result int;
            EXEC @result=[BmsReadiness407_Test_415].sys.sp_getapplock @Resource=@resource,
                @LockMode=N'Shared',@LockOwner=N'Transaction',@LockTimeout=15000;
            IF @result<0 THROW 65840,N'Owned accounting source publication is busy.',1;
            """,
            _ => throw new InvalidOperationException("Outside isolated419 capture-lock allowlist.")
        };
        await db.Database.ExecuteSqlRawAsync(lockSql419, [new Microsoft.Data.SqlClient.SqlParameter("@resource", resource)], token);
#else
        if(location?.Catalog=="BmsPayroll455_Test")
        {
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                DECLARE @result int;
                EXEC @result=[BmsPayroll455_Test].sys.sp_getapplock @Resource={{resource}},
                    @LockMode=N'Shared',@LockOwner=N'Transaction',@LockTimeout=15000;
                IF @result<0 THROW 65840,N'Owned payroll source publication is busy.',1;
                """,token);
            return;
        }
        if(location?.Catalog=="BmsPaymentAllocation400_Test")
        {
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                DECLARE @result int;
                EXEC @result=[BmsPaymentAllocation400_Test].sys.sp_getapplock @Resource={{resource}},
                    @LockMode=N'Shared',@LockOwner=N'Transaction',@LockTimeout=15000;
                IF @result<0 THROW 65840,N'Owned accounting source publication is busy.',1;
                """,token);
            return;
        }
        if(location?.Catalog=="BmsAccrualEdit311_Test")
        {
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                DECLARE @result int;
                EXEC @result=[BmsAccrualEdit311_Test].sys.sp_getapplock @Resource={{resource}},
                    @LockMode=N'Shared',@LockOwner=N'Transaction',@LockTimeout=15000;
                IF @result<0 THROW 65840,N'Owned accounting source publication is busy.',1;
                """,token);
            return;
        }
        if(location?.Catalog=="BmsGoLive258r2_Replica")
        {
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                DECLARE @result int;
                EXEC @result=[BmsGoLive258r2_Replica].sys.sp_getapplock @Resource={{resource}},
                    @LockMode=N'Shared',@LockOwner=N'Transaction',@LockTimeout=15000;
                IF @result<0 THROW 65840,N'Accounting source publication is busy.',1;
                """,token);
            return;
        }
        // Catalog comes only from the canonical process-level installation
        // setting. The lock resource remains a SQL parameter, never SQL text.
        var replica = new Microsoft.Data.SqlClient.SqlCommandBuilder()
            .QuoteIdentifier((location ?? new AccountingReplicaLocation()).Catalog);
        var lockSql = $$"""
            DECLARE @result int;
            EXEC @result={{replica}}.sys.sp_getapplock @Resource=@resource,
                @LockMode=N'Shared',@LockOwner=N'Transaction',@LockTimeout=15000;
            IF @result<0 THROW 65840,N'Accounting source publication is busy.',1;
            """;
        await db.Database.ExecuteSqlRawAsync(lockSql,
            [new Microsoft.Data.SqlClient.SqlParameter("@resource", resource)], token);
#endif
    }
}
