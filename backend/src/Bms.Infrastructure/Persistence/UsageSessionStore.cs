using System.Data;
using System.Data.Common;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace Bms.Infrastructure.Persistence;

public sealed partial class UsageSessionStore(BmsDbContext db) : IUsageSessionStore
{
    public async Task<bool> TouchAsync(string actor, Guid tab, long sequence, string name, bool closed, CancellationToken cancellationToken)
    {
        if (!ActorIdentity.IsCanonical(actor) || tab == Guid.Empty || sequence is < 1 or > 9_007_199_254_740_991
            || string.IsNullOrWhiteSpace(name) || name.Length > 512) throw new ArgumentException("Invalid usage event.");
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandTimeout = 10;
        command.CommandText = """
            SET NOCOUNT ON; SET XACT_ABORT ON;
            BEGIN TRY
             BEGIN TRANSACTION;
             DECLARE @lock int, @resource nvarchar(255)=N'BMS:Usage:'+CONVERT(varchar(64),HASHBYTES('SHA2_256',@actor),2);
             EXEC @lock=sys.sp_getapplock @Resource=@resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=5000;
             IF @lock<0 THROW 65510,N'Usage log busy.',1;
             DECLARE @now datetimeoffset(7)=TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00'),@session uniqueidentifier,@previous bigint;
             SELECT @previous=Sequence FROM bms_web.UsageTabs WHERE ActorExternalId=@actor AND TabId=@tab;
             IF @previous IS NOT NULL AND @previous>=@sequence BEGIN COMMIT; SELECT CAST(0 AS bit); RETURN; END;
             -- Bound new durable keys, including close-before-open tombstones.
             -- Known heartbeats/closes and stale events do not consume admission.
             IF @previous IS NULL AND (SELECT COUNT(*) FROM
              (SELECT TOP(32) 1 AS Admitted FROM bms_web.UsageTabs WHERE ActorExternalId=@actor
               AND LastSeenAtUtc>DATEADD(second,-180,@now)) AS recentTabs)>=32
              THROW 65511,N'Usage tab admission limit.',1;
             UPDATE bms_web.UsageSessions SET ExitedAtUtc=LastSeenAtUtc,EndReason='TIMEOUT'
              WHERE ActorExternalId=@actor AND ExitedAtUtc IS NULL AND LastSeenAtUtc<=DATEADD(second,-180,@now);
             SELECT @session=Id FROM bms_web.UsageSessions WHERE ActorExternalId=@actor AND ExitedAtUtc IS NULL;
             IF @closed=0
             BEGIN
              IF NOT EXISTS(SELECT 1 FROM bms_web.UsageTabs WHERE ActorExternalId=@actor AND TabId=@tab AND IsClosed=0 AND LastSeenAtUtc>DATEADD(second,-180,@now))
               AND (SELECT COUNT(*) FROM bms_web.UsageTabs WHERE ActorExternalId=@actor AND IsClosed=0 AND LastSeenAtUtc>DATEADD(second,-180,@now))>=32
               THROW 65511,N'Usage tab limit.',1;
              IF @session IS NULL
              BEGIN
               -- A known tab can reopen after logout. Bound those new intervals
               -- too, without deleting history or rejecting ordinary pulses.
               IF (SELECT COUNT(*) FROM
                (SELECT TOP(32) 1 AS Admitted FROM bms_web.UsageSessions WHERE ActorExternalId=@actor
                 AND EnteredAtUtc>DATEADD(second,-180,@now)) AS recentSessions)>=32
                THROW 65511,N'Usage session admission limit.',1;
               SET @session=NEWID();
               INSERT bms_web.UsageSessions(Id,ActorExternalId,UserName,EnteredAtUtc,LastSeenAtUtc)
                VALUES(@session,@actor,@name,@now,@now);
              END;
              UPDATE bms_web.UsageSessions SET LastSeenAtUtc=@now,UserName=@name WHERE Id=@session;
             END;
             IF @previous IS NULL
              INSERT bms_web.UsageTabs(ActorExternalId,TabId,SessionId,Sequence,LastSeenAtUtc,IsClosed)
               VALUES(@actor,@tab,@session,@sequence,@now,@closed);
             ELSE
              UPDATE bms_web.UsageTabs SET SessionId=CASE WHEN @closed=0 THEN @session ELSE SessionId END,
               Sequence=@sequence,LastSeenAtUtc=@now,IsClosed=@closed WHERE ActorExternalId=@actor AND TabId=@tab;
             IF @closed=1 AND @session IS NOT NULL AND NOT EXISTS(SELECT 1 FROM bms_web.UsageTabs
              WHERE SessionId=@session AND IsClosed=0 AND LastSeenAtUtc>DATEADD(second,-180,@now))
              UPDATE bms_web.UsageSessions SET LastSeenAtUtc=@now,ExitedAtUtc=@now,EndReason='EXIT' WHERE Id=@session;
             COMMIT; SELECT CAST(1 AS bit);
            END TRY
            BEGIN CATCH
             IF XACT_STATE()<>0 ROLLBACK;
             THROW;
            END CATCH;
            """;
        Add(command, "@actor", DbType.String, actor, 256); Add(command, "@tab", DbType.Guid, tab);
        Add(command, "@sequence", DbType.Int64, sequence); Add(command, "@name", DbType.String, name, 512);
        Add(command, "@closed", DbType.Boolean, closed);
        try { return await command.ExecuteScalarAsync(cancellationToken) is true; }
        catch (SqlException error) when (error.Number == 65511) { throw new UsageSessionCapacityException(); }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    public async Task<UsageSessionPage> ReadAsync(string? search, DateOnly from, DateOnly through, int skip, int take, CancellationToken cancellationToken)
    {
        if (search?.Length > 100 || skip is < 0 or > 1_000_000 || take is < 1 or > 100
            || through < from || through.DayNumber - from.DayNumber > 366 || through.Year >= 9999)
            throw new ArgumentException("Invalid usage query.");
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandTimeout = 15;
        // Copy a bounded date interval under one statement snapshot into a private table;
        // count and page therefore describe the same records, even during heartbeats.
        command.CommandText = """
            SET NOCOUNT ON;
            SELECT Id,UserName,EnteredAtUtc,LastSeenAtUtc,ExitedAtUtc,EndReason INTO #usage FROM bms_web.UsageSessions
             WHERE EnteredAtUtc>=@from AND EnteredAtUtc<@until AND (@search=N'' OR CHARINDEX(@search,UserName)>0);
            -- A writer can commit while the SELECT is waiting on its row lock.
            -- Capture the observation time AFTER the private copy, never before.
            DECLARE @now datetimeoffset(7)=TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00');
            SELECT COUNT(*),@now FROM #usage;
            SELECT Id,UserName,EnteredAtUtc,LastSeenAtUtc,observed.ExitedAtUtc,
             DATEDIFF_BIG(second,EnteredAtUtc,COALESCE(observed.ExitedAtUtc,@now)),
             COALESCE(EndReason,CASE WHEN LastSeenAtUtc<=DATEADD(second,-180,@now) THEN 'TIMEOUT' ELSE 'ACTIVE' END)
             FROM #usage AS s CROSS APPLY(VALUES(COALESCE(s.ExitedAtUtc,
              CASE WHEN LastSeenAtUtc<=DATEADD(second,-180,@now) THEN LastSeenAtUtc END))) AS observed(ExitedAtUtc)
             ORDER BY EnteredAtUtc DESC,Id OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """;
        Add(command, "@from", DbType.DateTimeOffset, new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(5)));
        Add(command, "@until", DbType.DateTimeOffset, new DateTimeOffset(through.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(5)));
        Add(command, "@search", DbType.String, search?.Trim() ?? "", 100);
        Add(command, "@skip", DbType.Int32, skip); Add(command, "@take", DbType.Int32, take);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var total = reader.GetInt32(0); var now = reader.GetFieldValue<DateTimeOffset>(1);
            await reader.NextResultAsync(cancellationToken);
            var rows = new List<UsageSessionRow>();
            while (await reader.ReadAsync(cancellationToken)) rows.Add(new UsageSessionRow(reader.GetGuid(0), reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2), reader.GetFieldValue<DateTimeOffset>(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4), reader.GetInt64(5), reader.GetString(6)));
            return new UsageSessionPage(rows, total, skip, take, now);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static void Add(DbCommand command, string name, DbType type, object value, int size = 0)
    {
        var p = command.CreateParameter(); p.ParameterName = name; p.DbType = type; p.Value = value;
        if (size > 0) p.Size = size;
        command.Parameters.Add(p);
    }
}
