using Bms.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
namespace Bms.Infrastructure.Health;
public sealed class PayrollEvidence455HealthCheck(BmsDbContext db):IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,CancellationToken token=default)
    {
        try
        {
            var b=new SqlConnectionStringBuilder(db.Database.GetConnectionString()){InitialCatalog=BmsInstallationSettings.ReplicaDatabase,Pooling=false,ConnectTimeout=5};
            if(!b.IntegratedSecurity||b.UserID.Length!=0||b.Password.Length!=0)return HealthCheckResult.Unhealthy("Payroll reader identity unavailable.");
            await using var c=new SqlConnection(b.ConnectionString);await c.OpenAsync(token);
            await using var q=c.CreateCommand();q.CommandTimeout=5;
            q.CommandText="""
                SELECT CAST(CASE WHEN HASHBYTES('SHA2_256',OBJECT_DEFINITION(OBJECT_ID(N'dbo.ReadPayrollCashEvidence455')))=0x809A916F3691316A717BFAE457822AA4AECC773371FE99D0FA5CE400E7DAD8FC
                AND HAS_PERMS_BY_NAME(N'dbo.ReadPayrollCashEvidence455',N'OBJECT',N'EXECUTE')=1
                AND COALESCE(HAS_PERMS_BY_NAME(N'dbo.RawCashLegs',N'OBJECT',N'SELECT'),0)=0
                AND COALESCE(HAS_PERMS_BY_NAME(N'dbo.RawCashLegs',N'OBJECT',N'UPDATE'),0)=0
                AND COALESCE(HAS_PERMS_BY_NAME(N'dbo.ReadPayrollCashEvidence455',N'OBJECT',N'ALTER'),0)=0
                THEN 1 ELSE 0 END AS bit)
                """;
            return await q.ExecuteScalarAsync(token) is true?HealthCheckResult.Healthy():HealthCheckResult.Unhealthy("Payroll evidence safeguards unavailable.");
        }catch(Exception){return HealthCheckResult.Unhealthy("Payroll evidence gateway unavailable.");}
    }
}
