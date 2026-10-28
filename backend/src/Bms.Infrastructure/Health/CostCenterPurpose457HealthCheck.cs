using Bms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
namespace Bms.Infrastructure.Health;
public sealed class CostCenterPurpose457HealthCheck(BmsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken token=default)
    {
        try
        {
            var checks=await db.Database.SqlQueryRaw<int>("""
                SELECT CONVERT(int,CASE WHEN
                  COL_LENGTH(N'bms_web.CostCenterPurposes457',N'CostCenterId')=16
                  AND COL_LENGTH(N'bms_web.CostCenterPurposes457',N'OnlyCostDepartment')=1
                  AND COL_LENGTH(N'bms_web.CostCenterPurposes457',N'ConcurrencyStamp')=16
                  AND HAS_PERMS_BY_NAME(N'bms_web.CostCenterPurposes457',N'OBJECT',N'SELECT')=1
                  AND HAS_PERMS_BY_NAME(N'bms_web.CostCenterPurposes457',N'OBJECT',N'INSERT')=1
                  AND HAS_PERMS_BY_NAME(N'bms_web.CostCenterPurposes457',N'OBJECT',N'UPDATE')=1
                  AND COALESCE(HAS_PERMS_BY_NAME(N'bms_web.CostCenterPurposes457',N'OBJECT',N'DELETE'),0)=0
                  AND COALESCE(HAS_PERMS_BY_NAME(N'bms_web.CostCenterPurposes457',N'OBJECT',N'ALTER'),0)=0
                THEN 1 ELSE 0 END) AS Value
                """).ToArrayAsync(token);
            return checks.Length==1&&checks[0]==1?HealthCheckResult.Healthy():HealthCheckResult.Unhealthy("Cost department safeguards unavailable.");
        }
        catch(Exception) { return HealthCheckResult.Unhealthy("Cost department metadata unavailable."); }
    }
}
