using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Ordering projection only. It never replaces the captured whole-scope
/// payment-ledger proof, which also checks event identity, currency and reversal
/// relationships. Every returned page time is compared with that canonical proof.
/// </summary>
internal static class ActiveAccrualPaymentTimeQuery
{
    internal static IQueryable<FirstFullPaymentTime> Read(BmsDbContext context) =>
        context.Database.SqlQueryRaw<FirstFullPaymentTime>(
            """
            SELECT [request].[Id] AS [ExpenseRequestId],
                [first].[FirstFullyPaidAtUtc]
            FROM [bms_web].[ExpenseRequests] AS [request]
            OUTER APPLY
            (
                SELECT MIN([ledger].[PaidAt]) AS [FirstFullyPaidAtUtc]
                FROM
                (
                    SELECT [instant].[PaidAt],
                        SUM([instant].[NetAmount]) OVER
                        (
                            ORDER BY [instant].[PaidAt]
                            ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                        ) AS [RunningAmount]
                    FROM
                    (
                        SELECT [fact].[PaidAt],
                            SUM(CASE WHEN [fact].[IsReversal] = 1
                                THEN -[fact].[Amount] ELSE [fact].[Amount] END) AS [NetAmount]
                        FROM [bms_web].[ExpenseRequestPaymentFacts] AS [fact]
                        WHERE [fact].[ExpenseRequestId] = [request].[Id]
                        GROUP BY [fact].[PaidAt]
                    ) AS [instant]
                ) AS [ledger]
                CROSS APPLY
                (
                    SELECT SUM([line].[Amount]) AS [Amount]
                    FROM [bms_web].[ExpenseRequestLines] AS [line]
                    WHERE [line].[ExpenseRequestId] = [request].[Id]
                ) AS [obligation]
                WHERE [ledger].[RunningAmount] >= [obligation].[Amount]
            ) AS [first]
            """);

    internal static string SqlServerTimeZone(TimeZoneInfo timeZone) =>
        TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZone.Id, out var windowsId)
            ? windowsId
            : timeZone.Id;

    internal sealed class FirstFullPaymentTime
    {
        public Guid ExpenseRequestId { get; set; }

        public DateTimeOffset? FirstFullyPaidAtUtc { get; set; }
    }
}
