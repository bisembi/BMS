using Bms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
namespace Bms.Infrastructure.Health;
public sealed class CompanyVersionDeletionHealthCheck(BmsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            await using var q = db.Database.GetDbConnection().CreateCommand(); q.CommandTimeout = 10;
            q.CommandText = """
                SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=60 AND script_name=N'060_company_budget_version_deletions.sql')
                AND (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.CompanyBudgetVersionDeletions'))=4
                AND (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'bms_web.CompanyBudgetVersionDeletions') AND is_disabled=0 AND is_not_trusted=0 AND delete_referential_action=0)=2
                AND (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'bms_web.CompanyBudgetVersionDeletions') AND is_disabled=0 AND is_not_trusted=0)=2
                AND (SELECT COUNT(*) FROM sys.triggers WHERE name IN(N'TR_CompanyBudgetVersionDeletions_Immutable',N'TR_CompanyBudgetActiveVersion_NotDeleted') AND is_disabled=0)=2
                AND HASHBYTES('SHA2_256',OBJECT_DEFINITION(OBJECT_ID(N'bms_web.TR_CompanyBudgetVersionDeletions_Immutable')))=0xE476B1497307D270A7A43046F4D6AF2F9C10149CFE7439E4CE6334607F93AE40
                AND HASHBYTES('SHA2_256',OBJECT_DEFINITION(OBJECT_ID(N'bms_web.TR_CompanyBudgetActiveVersion_NotDeleted')))=0x0B859EF31A5D69607FC7187F398DCC2805542CA647C18643398F414B3E3085A5
                THEN 1 ELSE 0 END AS bit)
                """;
            return await q.ExecuteScalarAsync(cancellationToken) is true ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Company version deletion schema is not ready.");
        }
        catch (Exception) { return HealthCheckResult.Unhealthy("Company version deletion database check failed."); }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
