using System.Data;
using System.Data.Common;
using System.Text;
using Bms.Application.Abstractions;
using Bms.Application.ReferenceData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Reads the optional schema-52 rank without mapping it onto the shared EF entity.
/// That is intentional: the same compatibility binary remains usable against
/// reviewed schema 51 and 52. A pre-52 package whose readiness probe rejects
/// the extra column is not a valid file-only rollback after migration.
/// </summary>
public sealed class CashFlowCatalogOrderStore(BmsDbContext dbContext) : ICashFlowCatalogOrderStore
{
    private const int MaximumRows = 10_000;
    private const int ParameterBatchSize = 500;

    public async Task<CashFlowCatalogOrderData> ReadAsync(
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        RequireSerializableTransaction();
        var supportsDisplayOrder = await SupportsDisplayOrderAsync(cancellationToken);
        var command = CreateCommand();
        await using (command)
        {
            var rank = supportsDisplayOrder
                ? "item.[DisplayOrder]"
                : "CAST(NULL AS int)";
            var hint = forUpdate ? "WITH (UPDLOCK,HOLDLOCK)" : "WITH (HOLDLOCK)";
            command.CommandText = $"""
                SELECT TOP ({MaximumRows + 1})
                    item.[Id], item.[Code], item.[Name], item.[ParentId],
                    parentItem.[Code], parentItem.[Name], item.[ActivityType],
                    item.[FlowDirection], item.[FormsManagerialAccrual],
                    item.[AccrualExpenseItemId], expense.[Name],
                    item.[CostCenterId], center.[Name], {rank}
                FROM [bms_web].[CashFlowItems] AS item {hint}
                LEFT JOIN [bms_web].[CashFlowItems] AS parentItem WITH (HOLDLOCK)
                    ON parentItem.[Id] = item.[ParentId] AND parentItem.[IsGroup] = 1
                LEFT JOIN [bms_web].[ExpenseItems] AS expense WITH (HOLDLOCK)
                    ON expense.[Id] = item.[AccrualExpenseItemId]
                LEFT JOIN [bms_web].[CostCenters] AS center WITH (HOLDLOCK)
                    ON center.[Id] = item.[CostCenterId]
                WHERE item.[IsActive] = 1
                  AND item.[IsGroup] = 0
                  AND item.[IsHistoricalForActuals] = 0
                ORDER BY item.[Id]
                """;
            var rows = new List<CashFlowCatalogOrderRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new CashFlowCatalogOrderRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    NullableGuid(reader, 3),
                    NullableString(reader, 4),
                    NullableString(reader, 5),
                    reader.GetString(6),
                    NullableString(reader, 7),
                    reader.GetBoolean(8),
                    NullableGuid(reader, 9),
                    NullableString(reader, 10),
                    NullableGuid(reader, 11),
                    NullableString(reader, 12),
                    reader.IsDBNull(13) ? null : reader.GetInt32(13)));
            }

            if (rows.Count > MaximumRows)
                throw new CashFlowCatalogOrderUnavailableException();
            return new CashFlowCatalogOrderData(supportsDisplayOrder, rows);
        }
    }

    public async Task<IReadOnlyDictionary<Guid, int?>> ReadDisplayOrdersAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0) return new Dictionary<Guid, int?>();
        if (ids.Any(id => id == Guid.Empty))
            throw new CashFlowCatalogOrderUnavailableException();

        var distinctIds = ids.Distinct().ToArray();
        var ownsConnection = dbContext.Database.GetDbConnection().State != ConnectionState.Open;
        if (ownsConnection)
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            if (!await SupportsDisplayOrderAsync(cancellationToken))
                return distinctIds.ToDictionary(id => id, _ => (int?)null);

            var result = new Dictionary<Guid, int?>(distinctIds.Length);
            foreach (var batch in distinctIds.Chunk(ParameterBatchSize))
            {
                await using var command = CreateCommand();
                var names = new string[batch.Length];
                for (var index = 0; index < batch.Length; index++)
                {
                    names[index] = $"@id{index}";
                    AddParameter(command, names[index], DbType.Guid, batch[index]);
                }
                command.CommandText = $"""
                    SELECT [Id], [DisplayOrder]
                    FROM [bms_web].[CashFlowItems]
                    WHERE [Id] IN ({string.Join(',', names)})
                    """;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    result.Add(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetInt32(1));
            }
            foreach (var id in distinctIds) result.TryAdd(id, null);
            return result;
        }
        finally
        {
            if (ownsConnection)
                await dbContext.Database.CloseConnectionAsync();
        }
    }

    public async Task StageDisplayOrdersAsync(
        IReadOnlyList<CashFlowCatalogDisplayOrderChange> changes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        RequireSerializableTransaction();
        if (!await SupportsDisplayOrderAsync(cancellationToken))
            throw new CashFlowCatalogOrderUnavailableException();
        if (changes.Count > MaximumRows
            || changes.Any(change => change.Id == Guid.Empty || change.DisplayOrder < 0)
            || changes.Select(change => change.Id).Distinct().Count() != changes.Count
            || changes.Select(change => change.DisplayOrder).Distinct().Count() != changes.Count)
            throw new CashFlowCatalogOrderUnavailableException();

        foreach (var batch in changes.Chunk(ParameterBatchSize))
        {
            await using var command = CreateCommand();
            var values = new StringBuilder();
            for (var index = 0; index < batch.Length; index++)
            {
                if (index > 0) values.Append(',');
                values.Append($"(@id{index},@rank{index})");
                AddParameter(command, $"@id{index}", DbType.Guid, batch[index].Id);
                AddParameter(command, $"@rank{index}", DbType.Int32, batch[index].DisplayOrder);
            }
            command.CommandText = $"""
                UPDATE item
                SET [DisplayOrder] = source.[DisplayOrder]
                FROM [bms_web].[CashFlowItems] AS item
                INNER JOIN (VALUES {values}) AS source([Id],[DisplayOrder])
                    ON source.[Id] = item.[Id]
                WHERE item.[IsActive] = 1
                  AND item.[IsGroup] = 0
                  AND item.[IsHistoricalForActuals] = 0
                  AND (item.[DisplayOrder] IS NULL OR item.[DisplayOrder] <> source.[DisplayOrder]);
                """;
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected != batch.Length)
                throw new CashFlowCatalogOrderVersionConflictException();
        }
    }

    private async Task<bool> SupportsDisplayOrderAsync(CancellationToken cancellationToken)
    {
        await using var command = CreateCommand();
        command.CommandText = """
            SELECT CONVERT(bit, CASE WHEN COL_LENGTH(
                N'bms_web.CashFlowItems', N'DisplayOrder') = 4 THEN 1 ELSE 0 END)
            """;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is bool result && result;
    }

    private DbCommand CreateCommand()
    {
        var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        return command;
    }

    private void RequireSerializableTransaction()
    {
        if (dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel
            != IsolationLevel.Serializable)
            throw new CashFlowCatalogOrderUnavailableException();
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static Guid? NullableGuid(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

    private static string? NullableString(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
