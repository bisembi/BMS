using System.Data;
using Bms.Application.Abstractions;
using Bms.Application.Integration;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class OutboxBacklogSummaryReader(BmsDbContext dbContext) :
    IOutboxBacklogSummaryReader
{
    internal const string CommandText = """
        SELECT
            COUNT_BIG(*) AS TotalCount,
            COALESCE(SUM(CASE WHEN ProcessedAtUtc IS NULL THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END), CONVERT(bigint, 0)) AS PendingCount,
            COALESCE(SUM(CASE WHEN ProcessedAtUtc IS NOT NULL THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END), CONVERT(bigint, 0)) AS ProcessedCount,
            COALESCE(SUM(CASE WHEN ProcessedAtUtc IS NULL AND RetryCount > 0 THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END), CONVERT(bigint, 0)) AS RetriedPendingCount,
            COALESCE(SUM(CASE WHEN ProcessedAtUtc IS NULL AND LastError IS NOT NULL THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END), CONVERT(bigint, 0)) AS PendingWithLastErrorCount,
            COALESCE(MAX(CASE WHEN ProcessedAtUtc IS NULL THEN RetryCount END), 0) AS MaxPendingRetryCount,
            MIN(CASE WHEN ProcessedAtUtc IS NULL THEN OccurredAtUtc END) AS OldestPendingOccurredAtUtc
        FROM bms_web.OutboxMessages;
        """;

    public async Task<OutboxBacklogSummaryRecord> ReadAsync(
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        try
        {
            if (closeConnection)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = CommandText;
            var commandTimeout = dbContext.Database.GetCommandTimeout();
            if (commandTimeout.HasValue)
            {
                command.CommandTimeout = commandTimeout.Value;
            }

            await using var dataReader = await command.ExecuteReaderAsync(
                CommandBehavior.SingleResult | CommandBehavior.SingleRow,
                cancellationToken);
            if (!await dataReader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "Outbox backlog summary query returned no safe state.");
            }

            var result = new OutboxBacklogSummaryRecord(
                dataReader.GetInt64(0),
                dataReader.GetInt64(1),
                dataReader.GetInt64(2),
                dataReader.GetInt64(3),
                dataReader.GetInt64(4),
                dataReader.GetInt32(5),
                dataReader.IsDBNull(6) ? null : dataReader.GetFieldValue<DateTimeOffset>(6));
            if (await dataReader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "Outbox backlog summary query returned multiple safe states.");
            }

            return result;
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }
}
