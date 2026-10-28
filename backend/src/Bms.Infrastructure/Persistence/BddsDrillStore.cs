using System.Data;
using System.Text.Json;
using Bms.Application.Budgeting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class BddsDrillStore(BmsDbContext db, AccountingReplicaLocation location) : IBddsDrillStore
{
    public async Task<BddsDrillPage> ReadAsync(BddsDrillQuery query, CancellationToken token)
    {
        var builder = new SqlConnectionStringBuilder(db.Database.GetConnectionString())
        { InitialCatalog = location.Catalog, Pooling = false };
        if (!builder.IntegratedSecurity) throw new BddsDrillUnavailableException();
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure; command.CommandText = "dbo.ReadDdsDrill467"; command.CommandTimeout = 30;
        command.Parameters.Add("@SnapshotId", SqlDbType.UniqueIdentifier).Value = query.SnapshotId;
        command.Parameters.Add("@ArticleIds", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(query.ArticleIds);
        command.Parameters.Add("@FromMonth", SqlDbType.Date).Value = query.FromMonth.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@ToMonth", SqlDbType.Date).Value = query.ToMonth.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@Skip", SqlDbType.Int).Value = query.Skip;
        command.Parameters.Add("@Take", SqlDbType.Int).Value = query.Take;
        command.Parameters.Add("@SortBy", SqlDbType.NVarChar, 128).Value = (object?)query.SortBy ?? DBNull.Value;
        command.Parameters.Add("@SortDirection", SqlDbType.NVarChar, 16).Value = (object?)query.SortDirection ?? DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token) || !reader.GetBoolean(0) || reader.IsDBNull(1)) throw new BddsDrillUnavailableException();
        var captured = reader.GetFieldValue<DateTimeOffset>(1);
        if (!await reader.NextResultAsync(token) || !await reader.ReadAsync(token)) throw new BddsDrillControlException();
        var total = reader.GetInt32(0); var amount = reader.GetDecimal(1);
        if (!await reader.NextResultAsync(token)) throw new BddsDrillControlException();
        var rows = new List<BddsDrillRow>();
        while (await reader.ReadAsync(token))
        {
            if (rows.Count >= query.Take) throw new BddsDrillControlException();
            rows.Add(new(reader.GetDateTime(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetDecimal(7), reader.GetInt32(8)));
        }
        return new(query.SnapshotId, captured, rows, query.Skip, query.Take, total, amount);
    }
}
