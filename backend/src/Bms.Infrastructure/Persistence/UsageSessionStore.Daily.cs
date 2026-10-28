using System.Data;
using Bms.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed partial class UsageSessionStore
{
    public async Task<UsageDailyPage> ReadDailyAsync(string? search, DateOnly from, DateOnly through,
        int skip, int take, CancellationToken cancellationToken)
    {
        if (search?.Length > 100 || skip is < 0 or > 1_000_000 || take is < 1 or > 100
            || through < from || through.DayNumber - from.DayNumber > 366 || through.Year >= 9999)
            throw new ArgumentException("Invalid daily usage query.");
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandTimeout = 30;
        // Source identity stays server-side. Copy once, clip by local calendar
        // day, then union overlapping/nested intervals BEFORE paging or summing.
        command.CommandText = """
            SET NOCOUNT ON;
            SELECT Id,ActorExternalId,UserName,EnteredAtUtc,LastSeenAtUtc,ExitedAtUtc
              INTO #usage_daily_source FROM bms_web.UsageSessions
              WHERE EnteredAtUtc<@until
                AND (ExitedAtUtc>=@from OR (ExitedAtUtc IS NULL AND LastSeenAtUtc>=DATEADD(second,-180,@from)))
                ;
            DECLARE @now datetimeoffset(7)=TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00');
            ;WITH days AS (
              SELECT CAST(SWITCHOFFSET(@from,'+05:00') AS date) AS [Day]
              UNION ALL SELECT DATEADD(day,1,[Day]) FROM days
                WHERE [Day]<CAST(SWITCHOFFSET(DATEADD(second,-1,@until),'+05:00') AS date)
            )
            SELECT s.Id,s.ActorExternalId,s.UserName,d.[Day],clipped.BeginAt,clipped.EndAt,
              CAST(CASE WHEN s.ExitedAtUtc IS NULL AND s.LastSeenAtUtc<=DATEADD(second,-180,@now) THEN 1 ELSE 0 END AS int) AS ApproximateExit,
              CAST(CASE WHEN s.ExitedAtUtc IS NULL AND s.LastSeenAtUtc>DATEADD(second,-180,@now)
                AND d.[Day]=CAST(SWITCHOFFSET(@now,'+05:00') AS date) THEN 1 ELSE 0 END AS int) AS IsActive
              INTO #usage_daily_segments
              FROM #usage_daily_source s CROSS JOIN days d
              CROSS APPLY(VALUES(TODATETIMEOFFSET(CAST(d.[Day] AS datetime2),'+05:00'),
                TODATETIMEOFFSET(CAST(DATEADD(day,1,d.[Day]) AS datetime2),'+05:00'))) boundary(DayStart,DayEnd)
              CROSS APPLY(VALUES(CASE WHEN s.ExitedAtUtc IS NOT NULL THEN s.ExitedAtUtc
                WHEN s.LastSeenAtUtc<=DATEADD(second,-180,@now) THEN s.LastSeenAtUtc ELSE @now END)) observed(EndAt)
              CROSS APPLY(VALUES(CASE WHEN s.EnteredAtUtc>boundary.DayStart THEN s.EnteredAtUtc ELSE boundary.DayStart END,
                CASE WHEN observed.EndAt<boundary.DayEnd THEN observed.EndAt ELSE boundary.DayEnd END)) clipped(BeginAt,EndAt)
              WHERE clipped.EndAt>clipped.BeginAt
                AND (@search=N'' OR EXISTS(SELECT 1 FROM #usage_daily_source matched
                  WHERE matched.ActorExternalId=s.ActorExternalId AND CHARINDEX(@search,matched.UserName)>0))
              OPTION(MAXRECURSION 367);
            ;WITH previous_ends AS (
              SELECT *,MAX(EndAt) OVER(PARTITION BY ActorExternalId,[Day] ORDER BY BeginAt,EndAt,Id
                ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) AS PreviousEnd
                FROM #usage_daily_segments
            ), numbered AS (
              SELECT *,SUM(CASE WHEN PreviousEnd IS NULL OR BeginAt>PreviousEnd THEN 1 ELSE 0 END)
                OVER(PARTITION BY ActorExternalId,[Day] ORDER BY BeginAt,EndAt,Id ROWS UNBOUNDED PRECEDING) AS Island
                FROM previous_ends
            ), merged AS (
              SELECT ActorExternalId,[Day],Island,MIN(BeginAt) AS BeginAt,MAX(EndAt) AS EndAt
                FROM numbered GROUP BY ActorExternalId,[Day],Island
            ), durations AS (
              SELECT ActorExternalId,[Day],SUM(DATEDIFF_BIG(millisecond,BeginAt,EndAt))/1000 AS DurationSeconds
                FROM merged GROUP BY ActorExternalId,[Day]
            ), facts AS (
              SELECT ActorExternalId,[Day],MIN(CONVERT(char(36),Id)) AS SummaryId,MIN(BeginAt) AS FirstEntryAtUtc,
                MAX(EndAt) AS LastExitAtUtc,COUNT(DISTINCT Id) AS SessionCount,
                MAX(ApproximateExit) AS ApproximateExit,MAX(IsActive) AS IsActive
                FROM #usage_daily_segments GROUP BY ActorExternalId,[Day]
            )
            SELECT CONVERT(uniqueidentifier,f.SummaryId) AS SummaryId,f.[Day],name.UserName,
              f.FirstEntryAtUtc,f.LastExitAtUtc,d.DurationSeconds,f.SessionCount,f.ApproximateExit,f.IsActive
              INTO #usage_daily_result FROM facts f JOIN durations d ON f.ActorExternalId=d.ActorExternalId AND f.[Day]=d.[Day]
              CROSS APPLY(SELECT TOP(1) s.UserName FROM #usage_daily_source s WHERE s.ActorExternalId=f.ActorExternalId
                ORDER BY s.LastSeenAtUtc DESC,s.EnteredAtUtc DESC,s.Id) name;
            SELECT COUNT(*),@now FROM #usage_daily_result;
            SELECT SummaryId,[Day],UserName,FirstEntryAtUtc,LastExitAtUtc,DurationSeconds,SessionCount,ApproximateExit,IsActive
              FROM #usage_daily_result ORDER BY [Day] DESC,UserName,SummaryId
              OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
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
            var rows = new List<UsageDailyRow>();
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new UsageDailyRow(reader.GetGuid(0), DateOnly.FromDateTime(reader.GetDateTime(1)), reader.GetString(2),
                    reader.GetFieldValue<DateTimeOffset>(3), reader.GetFieldValue<DateTimeOffset>(4),
                    reader.GetInt64(5), reader.GetInt32(6), reader.GetInt32(7) == 1, reader.GetInt32(8) == 1));
            return new UsageDailyPage(rows, total, skip, take, now);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
