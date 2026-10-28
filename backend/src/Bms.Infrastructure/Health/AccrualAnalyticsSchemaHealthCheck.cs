using Bms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
namespace Bms.Infrastructure.Health;
public sealed class AccrualAnalyticsSchemaHealthCheck(BmsDbContext db):IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,CancellationToken cancellationToken=default)
    {
        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            await using var q=db.Database.GetDbConnection().CreateCommand();q.CommandTimeout=10;
            q.CommandText="""
                SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=58 AND script_name=N'058_accrual_department.sql')
                AND EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.ExpenseRequestLines') AND name=N'AccrualCostCenterId' AND system_type_id=36 AND is_nullable=1)
                AND EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'bms_web.ExpenseRequestLines') AND referenced_object_id=OBJECT_ID(N'bms_web.CostCenters') AND name=N'FK_ExpenseRequestLines_AccrualCostCenter' AND is_disabled=0 AND is_not_trusted=0)
                AND EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'bms_web.ExpenseRequestLines') AND name=N'CK_ExpenseRequestLines_AccrualCostCenter' AND is_disabled=0 AND is_not_trusted=0)
                THEN 1 ELSE 0 END AS bit)
                """;
            return await q.ExecuteScalarAsync(cancellationToken) is true?HealthCheckResult.Healthy():HealthCheckResult.Unhealthy("Accrual analytics schema is not ready.");
        }
        catch(Exception){return HealthCheckResult.Unhealthy("Accrual analytics database check failed.");}
        finally{await db.Database.CloseConnectionAsync();}
    }
}
