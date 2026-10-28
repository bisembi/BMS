using System.Data;
using Bms.Application.Audit;
using Microsoft.Data.SqlClient;

namespace Bms.Infrastructure.Persistence;

public sealed partial class ExchangeJournalReader
{
    private async Task<SourcePage> ReadDrillRunsAsync(ExchangeJournalFilter filter, int take, List<string> warnings, CancellationToken token)
    {
        await using var connection = Replica(); await connection.OpenAsync(token);
        await using var q = connection.CreateCommand(); q.CommandType = CommandType.StoredProcedure; q.CommandText = "dbo.ReadDdsDrillRuns277"; q.CommandTimeout = 30;
        q.Parameters.Add("@FromUtc", SqlDbType.DateTimeOffset).Value = (object?)filter.FromUtc ?? DBNull.Value;
        q.Parameters.Add("@ToUtc", SqlDbType.DateTimeOffset).Value = (object?)filter.ToUtc ?? DBNull.Value;
        q.Parameters.Add("@Status", SqlDbType.VarChar, 10).Value = filter.Status;
        q.Parameters.Add("@Take", SqlDbType.Int).Value = take;
        await using var r = await q.ExecuteReaderAsync(token);
        if (!await r.ReadAsync(token)) throw new InvalidOperationException("Missing detail exchange total.");
        var total = r.GetInt32(0);
        if (!r.GetBoolean(1)) warnings.Add("Расшифровка текущего снимка факта ещё не загружена. Проверьте последний обмен детализации ДДС.");
        if (!await r.NextResultAsync(token)) throw new InvalidOperationException("Missing detail exchange rows.");
        var rows = new List<ExchangeJournalItemDto>();
        while (await r.ReadAsync(token))
        {
            if (rows.Count >= take) throw new InvalidOperationException("Detail journal page exceeded.");
            var status = r.GetString(3);
            rows.Add(new(r.GetGuid(0), "dds", r.GetFieldValue<DateTimeOffset>(1), r.IsDBNull(2) ? null : r.GetFieldValue<DateTimeOffset>(2),
                "Расшифровка ДДС · 1С → SQL BMS", status == "PASS" ? "success" : status == "FAIL" ? "failed" : "running",
                r.IsDBNull(4) ? null : r.GetInt32(4), status == "PASS"
                    ? "Реквизиты загружены. Каждая проводка и суммы по всем статьям и месяцам точно сверены со снимком БДДС."
                    : status == "RUNNING" ? "Загрузка реквизитов начата; успешный результат ещё не подтверждён."
                    : "Детализация не опубликована: источник недоступен, изменился или не прошёл сверку. Предыдущие снимки сохранены.", status == "PASS"));
        }
        return new(rows, total, null);
    }

    private async Task<ExchangeJournalDetailDto?> ReadDrillRegistryAsync(Guid id, bool excluded, int skip, int take, CancellationToken token)
    {
        await using var connection = Replica(); await connection.OpenAsync(token);
        await using var q = connection.CreateCommand(); q.CommandType = CommandType.StoredProcedure; q.CommandText = "dbo.ReadDdsDrillRegistry277"; q.CommandTimeout = 30;
        q.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        q.Parameters.Add("@IncludeExcluded", SqlDbType.Bit).Value = excluded;
        q.Parameters.Add("@Skip", SqlDbType.Int).Value = skip; q.Parameters.Add("@Take", SqlDbType.Int).Value = take;
        await using var r = await q.ExecuteReaderAsync(token);
        if (!await r.ReadAsync(token) || !r.GetBoolean(0)) return null;
        if (!await r.NextResultAsync(token) || !await r.ReadAsync(token)) throw new InvalidOperationException("Missing detail registry total.");
        var total = r.GetInt32(0); var amount = r.GetDecimal(1);
        if (!await r.NextResultAsync(token)) throw new InvalidOperationException("Missing detail registry rows.");
        var rows = new List<IReadOnlyList<string>>();
        while (await r.ReadAsync(token))
        {
            if (rows.Count >= take) throw new InvalidOperationException("Detail registry page exceeded.");
            rows.Add([r.GetDateTime(0).ToString("dd.MM.yyyy"), ExchangeJournalDisplay.Text(r.GetString(1)),
                ExchangeJournalDisplay.Text(r.GetString(2)), ExchangeJournalDisplay.Text(r.GetString(3)),
                ExchangeJournalDisplay.Text(r.GetString(4)), ExchangeJournalDisplay.Text(r.GetString(5)),
                ExchangeJournalDisplay.Text(r.GetString(6)), ExchangeJournalDisplay.Money(r.GetDecimal(7))]);
        }
        return new("Расшифровка факта ДДС", "Неизменяемый реестр выбранного обмена из SQL BMS. Итог по всем строкам выбранного режима, не только по странице.",
            ["Дата", "ЮЛ", "Тип документа", "Документ", "Контрагент", "Примечания", "Комментарии", "Сумма, ₸"], rows, skip, take, total,
            [new("Итого", ExchangeJournalDisplay.Money(amount))]);
    }
}
