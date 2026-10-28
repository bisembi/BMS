using Bms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bms.Infrastructure.Health;

public sealed class StaffSubstitutionReadiness471(BmsDbContext db):IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,CancellationToken token=default)
    {
        try
        {
            var ready=await db.Database.SqlQueryRaw<int>("""
                SELECT CASE WHEN
                (SELECT COUNT(*) FROM sys.extended_properties WHERE class=1 AND name=N'BmsStaff471Version' AND CONVERT(nvarchar(20),value)=N'1'
                    AND major_id IN(OBJECT_ID(N'bms_web.StaffSubstitutions471'),OBJECT_ID(N'bms_web.StaffSubstitutionCommands471'),OBJECT_ID(N'bms_web.StaffSubstitutionActions471')))=3
                AND (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.StaffSubstitutions471'))=14
                AND (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.StaffSubstitutionCommands471'))=7
                AND (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.StaffSubstitutionActions471'))=10
                AND (SELECT COUNT(*) FROM sys.key_constraints WHERE type=N'PK' AND parent_object_id IN(OBJECT_ID(N'bms_web.StaffSubstitutions471'),OBJECT_ID(N'bms_web.StaffSubstitutionCommands471'),OBJECT_ID(N'bms_web.StaffSubstitutionActions471')))=3
                AND (SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled=0 AND is_not_trusted=0 AND parent_object_id IN(OBJECT_ID(N'bms_web.StaffSubstitutions471'),OBJECT_ID(N'bms_web.StaffSubstitutionCommands471'),OBJECT_ID(N'bms_web.StaffSubstitutionActions471')))=4
                AND (SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled=0 AND is_not_trusted=0 AND parent_object_id IN(OBJECT_ID(N'bms_web.StaffSubstitutions471'),OBJECT_ID(N'bms_web.StaffSubstitutionCommands471'),OBJECT_ID(N'bms_web.StaffSubstitutionActions471')))=6
                AND (SELECT COUNT(*) FROM sys.indexes WHERE is_disabled=0 AND is_hypothetical=0 AND index_id>0 AND object_id IN(OBJECT_ID(N'bms_web.StaffSubstitutions471'),OBJECT_ID(N'bms_web.StaffSubstitutionCommands471'),OBJECT_ID(N'bms_web.StaffSubstitutionActions471')))=8
                AND NOT EXISTS(SELECT 1 FROM sys.triggers WHERE parent_id IN(OBJECT_ID(N'bms_web.StaffSubstitutions471'),OBJECT_ID(N'bms_web.StaffSubstitutionCommands471'),OBJECT_ID(N'bms_web.StaffSubstitutionActions471')))
                THEN 1 ELSE 0 END AS Value
                """).SingleAsync(token);
            return ready==1?HealthCheckResult.Healthy("Staff substitution storage is ready."):
                HealthCheckResult.Unhealthy("Staff substitution storage is not ready.");
        }
        catch(OperationCanceledException){throw;}
        catch(Exception){return HealthCheckResult.Unhealthy("Staff substitution storage is unavailable.");}
    }
}
