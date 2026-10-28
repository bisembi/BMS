using Bms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bms.Infrastructure.Health;

public sealed class UsageLogHealthCheck(BmsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandTimeout = 10;
            command.CommandText = """
                SELECT CAST(CASE WHEN
                 (SELECT COUNT(*) FROM bms_web.UsageLogSchema)=1 AND EXISTS(SELECT 1 FROM bms_web.UsageLogSchema WHERE Version=1)
                 AND (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.UsageSessions'))=7
                 AND (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.UsageTabs'))=6
                 AND COL_LENGTH(N'bms_web.UsageSessions',N'ActorExternalId')=512
                 AND COL_LENGTH(N'bms_web.UsageSessions',N'UserName')=1024
                 AND COL_LENGTH(N'bms_web.UsageTabs',N'Sequence')=8
                 AND EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'bms_web.UsageSessions') AND name=N'IX_UsageSessions_OpenActor' AND is_unique=1 AND has_filter=1 AND is_disabled=0)
                 AND EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'bms_web.UsageSessions') AND name=N'IX_UsageSessions_EnteredAt' AND is_disabled=0)
                 AND EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'bms_web.UsageTabs') AND name=N'IX_UsageTabs_Session' AND is_disabled=0)
                 AND (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id IN(OBJECT_ID(N'bms_web.UsageTabs'),OBJECT_ID(N'bms_web.UsageSessions')) AND is_disabled=0 AND is_not_trusted=0)=2
                 AND EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'bms_web.UsageTabs') AND name=N'FK_UsageTabs_Session' AND is_disabled=0 AND is_not_trusted=0)
                 THEN 1 ELSE 0 END AS bit);
                """;
            var ready = await command.ExecuteScalarAsync(cancellationToken) is true;
            return ready ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Usage log schema is not ready.");
        }
        catch (Exception) { return HealthCheckResult.Unhealthy("Usage log database check failed."); }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
