using Bms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bms.Infrastructure.Health;

public sealed class ActiveComposition454HealthCheck(BmsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            await using var q = db.Database.GetDbConnection().CreateCommand(); q.CommandTimeout = 10;
            q.CommandText = """
                SELECT CAST(CASE WHEN SCHEMA_ID(N'bms_version454') IS NOT NULL
                AND (SELECT principal_id FROM sys.schemas WHERE name=N'bms_version454')=DATABASE_PRINCIPAL_ID(N'dbo')
                AND (SELECT COUNT(*) FROM bms_version454.Contract)=1
                AND EXISTS(SELECT 1 FROM bms_version454.Contract WHERE Id=1 AND ContractName=N'BmsActiveComposition454/v1')
                AND (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_version454.Contract'))=2
                AND (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_version454.MutationPermit'))=3
                AND (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_version454.CompositionHistory'))=11
                AND NOT EXISTS(SELECT 1 FROM bms_version454.MutationPermit)
                AND (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id IN(OBJECT_ID(N'bms_version454.Contract'),
                  OBJECT_ID(N'bms_version454.MutationPermit'),OBJECT_ID(N'bms_version454.CompositionHistory')) AND is_disabled=0 AND is_not_trusted=0)=4
                AND EXISTS(SELECT 1 FROM sys.sql_modules WHERE object_id=OBJECT_ID(N'bms_version454.UpdateActiveComposition') AND execute_as_principal_id=-2)
                AND (SELECT COUNT(*) FROM sys.triggers WHERE object_id IN(OBJECT_ID(N'bms_version454.TR_CompositionHistory_Immutable'),
                  OBJECT_ID(N'bms_web.TR_CompanyBudgetVersions_Immutable'),OBJECT_ID(N'bms_web.TR_CompanyBudgetMembers_Immutable')) AND is_disabled=0 AND is_instead_of_trigger=0)=3
                AND HASHBYTES('SHA2_256',OBJECT_DEFINITION(OBJECT_ID(N'bms_version454.TR_CompositionHistory_Immutable')))=0xDFAB3E6F9D98B1059BAE3D02A5644F4BE7C3B74C4964543DF4EB6AEDB4D302A5
                AND HASHBYTES('SHA2_256',OBJECT_DEFINITION(OBJECT_ID(N'bms_web.TR_CompanyBudgetVersions_Immutable')))=0x9E2287576DDFF1A7F71834E152AF45FA6A95EA70168A521A26ABD4C6A80C2D38
                AND HASHBYTES('SHA2_256',OBJECT_DEFINITION(OBJECT_ID(N'bms_web.TR_CompanyBudgetMembers_Immutable')))=0x17536C7F297C57090ADC55DAEC3499505536E4453FE781288E9E5669C6A60951
                AND HASHBYTES('SHA2_256',OBJECT_DEFINITION(OBJECT_ID(N'bms_version454.UpdateActiveComposition')))=0x69D6D912EDA5982F8EA8DFF9707E7CA19180D45FD2BF48B89A0E8F54A36A1969
                AND HAS_PERMS_BY_NAME(N'bms_version454.UpdateActiveComposition',N'OBJECT',N'EXECUTE')=1
                AND HAS_PERMS_BY_NAME(N'bms_version454.MutationPermit',N'OBJECT',N'INSERT')=0
                AND HAS_PERMS_BY_NAME(N'bms_version454.MutationPermit',N'OBJECT',N'UPDATE')=0
                AND HAS_PERMS_BY_NAME(N'bms_version454.MutationPermit',N'OBJECT',N'DELETE')=0
                AND HAS_PERMS_BY_NAME(N'bms_version454',N'SCHEMA',N'ALTER')=0
                THEN 1 ELSE 0 END AS bit)
                """;
            return await q.ExecuteScalarAsync(cancellationToken) is true ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Active composition safeguards are not ready.");
        }
        catch (Exception) { return HealthCheckResult.Unhealthy("Active composition database check failed."); }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
