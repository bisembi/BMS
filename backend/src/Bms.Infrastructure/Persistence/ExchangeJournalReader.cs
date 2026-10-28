using System.Data;
using Bms.Application.Abstractions;
using Bms.Application.Audit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed partial class ExchangeJournalReader(BmsDbContext db, IBusinessTransactionRunner transactions,
    AccountingPaymentSettings? accounting=null,AccountingReplicaLocation? location=null) : IExchangeJournalReader
{
    private static readonly string[] ReferenceTypes = ["ReferenceData", "CashFlowItem", "CashFlowCatalog", "Organization", "ExpenseItem", "Contract",
        "CostCenter", "Employee", "AuthorizationDirectoryEmployee", "BudgetOwnerAssignment", "FunctionalRole", "FunctionalRoleReference", "BudgetCostCenterSettings", "Counterparty", "CounterpartyImport"];
    private sealed record SourcePage(IReadOnlyList<ExchangeJournalItemDto> Items, int Total, ExchangeFactSummaryDto? Fact);
    private SqlConnection Replica()
    {
        var builder = new SqlConnectionStringBuilder(db.Database.GetConnectionString()) {
            InitialCatalog = (location??new()).Catalog, ApplicationName = "BMS-ExchangeJournal-ReadOnly", Pooling = false, ConnectTimeout = 10 };
#if BMS_OWNED419_ACCEPTANCE
        AccountingReplicaLocation.RequireReader419(builder);
#else
        if (!builder.IntegratedSecurity || builder.UserID.Length != 0 || builder.Password.Length != 0)
            throw new InvalidOperationException("Existing Windows SQL identity required.");
#endif
        return new(builder.ConnectionString);
    }
    public async Task<ExchangeJournalReadPage> ReadAsync(ExchangeJournalFilter filter, int skip, int take, CancellationToken token)
    {
        var count = checked(skip + take);
        var dds = new SourcePage([], 0, null); var warnings = new List<string>();
        if(accounting is {BudgetControlEnabled:true,ReconciliationEnabled:false})
            warnings.Add("Автоматическое подтверждение оплат приостановлено администратором. Факт и сохранённые месяцы резерва продолжают учитываться; новые оплаты не подтверждаются.");
        try { dds = await ReadDdsAsync(filter, filter.Kind is "all" or "dds" ? count : 1, token); }
        catch (OperationCanceledException) { throw; }
        catch (SqlException) { warnings.Add("Журнал обмена ДДС в SQL временно недоступен. Показаны только доступные записи; полнота журнала не подтверждена."); }
        var audit = filter.Kind == "dds" ? new SourcePage([], 0, null)
            : await ReadAuditAsync(filter, count, token);
        var payment=new SourcePage([],0,null);
        if(filter.Kind is "all" or "payments")
        {
            if(accounting?.BudgetControlEnabled==true)
            {
                try{payment=await ReadPaymentRunsAsync(filter,count,token);}
                catch(OperationCanceledException){throw;}
                catch(SqlException){warnings.Add("Журнал загрузки платёжных документов 1С недоступен; полнота сверки оплат не подтверждена.");}
            }
            else if(filter.Kind=="payments")warnings.Add("Автоматическая сверка оплат ещё не активирована.");
        }
        var drill = new SourcePage([], 0, null);
        if (filter.Kind is "all" or "dds")
        {
            try { drill = await ReadDrillRunsAsync(filter, count, warnings, token); }
            catch (OperationCanceledException) { throw; }
            catch (SqlException) { warnings.Add("Журнал расшифровки ДДС недоступен; полнота загрузки реквизитов не подтверждена."); }
        }
        var rows = (filter.Kind is "all" or "dds" ? dds.Items.Concat(audit.Items) : audit.Items).Concat(payment.Items).Concat(drill.Items);
        var total = audit.Total + (filter.Kind is "all" or "dds" ? dds.Total : 0)+payment.Total+drill.Total;
        return new(rows.OrderByDescending(row => row.StartedAtUtc).ThenByDescending(row => row.Kind, StringComparer.Ordinal)
            .ThenByDescending(row => row.Id.ToString("D"), StringComparer.Ordinal).Skip(skip).Take(take).ToArray(), total, warnings.Count == 0, warnings, dds.Fact);
    }
    private Task<SourcePage> ReadAuditAsync(ExchangeJournalFilter filter, int take, CancellationToken token) => transactions.ExecuteAsync(async ct =>
    {
        var query = db.BusinessAuditEntries.AsNoTracking().Where(a => a.ObjectType == "CurrencyRateImport" || a.ObjectType=="AccountingPaymentExchange" || ReferenceTypes.Contains(a.ObjectType));
        if(filter.Kind=="payments")query=query.Where(a=>a.ObjectType=="AccountingPaymentExchange");
        if (filter.Kind == "currency") query = query.Where(a => a.ObjectType == "CurrencyRateImport");
        if (filter.Kind == "references") query = query.Where(a => ReferenceTypes.Contains(a.ObjectType));
        if (filter.FromUtc is not null) query = query.Where(a => a.OccurredAtUtc >= filter.FromUtc);
        if (filter.ToUtc is not null) query = query.Where(a => a.OccurredAtUtc < filter.ToUtc);
        if (filter.Status == "running") query = query.Where(_ => false);
        if (filter.Status == "failed") query = query.Where(a => a.ObjectType == "CurrencyRateImport" && a.Action != "CurrencyRateImportSucceeded"
            ||a.ObjectType=="AccountingPaymentExchange"&&a.Action=="AccountingPaymentExchangeFailed"
            ||a.ObjectType=="CounterpartyImport"&&a.Action!="CounterpartyImportCompleted"&&a.Action!="CounterpartyImportWarnings"&&a.Action!="CounterpartyImportConflict");
        if(filter.Status=="warning")query=query.Where(a=>a.ObjectType=="AccountingPaymentExchange"&&a.Action=="AccountingPaymentExchangeWarnings"
            ||a.ObjectType=="CounterpartyImport"&&(a.Action=="CounterpartyImportWarnings"||a.Action=="CounterpartyImportConflict"));
        if (filter.Status == "success") query = query.Where(a => a.ObjectType != "CurrencyRateImport" && a.ObjectType!="AccountingPaymentExchange" && a.ObjectType!="CounterpartyImport"
            || a.Action == "CurrencyRateImportSucceeded" ||a.ObjectType=="AccountingPaymentExchange"&&a.Action=="AccountingPaymentExchangeCompleted"
            ||a.ObjectType=="CounterpartyImport"&&a.Action=="CounterpartyImportCompleted");
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.OccurredAtUtc)
            .ThenByDescending(a => a.ObjectType == "CurrencyRateImport" ? "currency" : a.ObjectType=="AccountingPaymentExchange"?"payments":"references")
            .ThenByDescending(a => a.Id.ToString()).Take(take)
            .Select(a => new { a.Id, a.OccurredAtUtc, a.ObjectType, a.Action, After = a.AfterJson.Length <= 65536 ? a.AfterJson : null }).ToArrayAsync(ct);
        return new SourcePage(rows.Select(a => ExchangeJournalDisplay.Audit(a.Id, a.OccurredAtUtc, a.ObjectType, a.Action, a.After)).ToArray(), total, null);
    }, token);
    private async Task<SourcePage> ReadDdsAsync(ExchangeJournalFilter filter, int take, CancellationToken token)
    {
        await using var connection = Replica(); await connection.OpenAsync(token);
        await using var command = connection.CreateCommand(); command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.ReadExchangeRuns255"; command.CommandTimeout = 30;
        command.Parameters.Add("@FromUtc", SqlDbType.DateTimeOffset).Value = (object?)filter.FromUtc ?? DBNull.Value;
        command.Parameters.Add("@ToUtc", SqlDbType.DateTimeOffset).Value = (object?)filter.ToUtc ?? DBNull.Value;
        command.Parameters.Add("@Status", SqlDbType.VarChar, 10).Value = filter.Status=="warning"?"all":filter.Status;
        command.Parameters.Add("@Skip", SqlDbType.Int).Value = 0; command.Parameters.Add("@Take", SqlDbType.Int).Value = take;
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new InvalidOperationException("Missing journal total.");
        var total = reader.GetInt32(0); var rows = new List<ExchangeJournalItemDto>();
        if (!await reader.NextResultAsync(token)) throw new InvalidOperationException("Missing journal rows.");
        while (await reader.ReadAsync(token))
        {
            if (rows.Count >= take) throw new InvalidOperationException("Unbounded journal rows.");
            var started = reader.GetFieldValue<DateTimeOffset>(1); var state = reader.GetString(3);
            var finished = reader.IsDBNull(2) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(2);
            var published = reader.IsDBNull(7) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(7);
            var unchanged = state == "PASS" && published < started;
            rows.Add(new(reader.GetGuid(0), "dds", started, finished, "Факт ДДС · 1С → SQL BMS",
                state == "PASS" ? "success" : state == "FAIL" ? "failed" : "running",
                reader.IsDBNull(6) ? null : reader.GetInt32(6), state == "FAIL"
                    ? ExchangeJournalDisplay.Failure(reader.IsDBNull(5) ? null : reader.GetString(5))
                    : unchanged ? "Проверка источника выполнена: изменений нет. В реестре показан состав действующего снимка."
                    : state == "PASS" ? "Снимок факта опубликован в SQL BMS. Реестр показывает принятые записи; исключённые доступны отдельно."
                    : "Обмен начат; окончательный результат ещё не зарегистрирован.", !reader.IsDBNull(4)));
        }
        ExchangeFactSummaryDto? fact = null;
        if (!await reader.NextResultAsync(token)) throw new InvalidOperationException("Missing journal summary.");
        if (await reader.ReadAsync(token)) fact = new(reader.GetFieldValue<DateTimeOffset>(0), reader.GetFieldValue<DateTimeOffset>(1),
            DateOnly.FromDateTime(reader.GetDateTime(2)), reader.IsDBNull(3) ? null : DateOnly.FromDateTime(reader.GetDateTime(3)),
            reader.GetInt32(4), ExchangeJournalDisplay.Money(reader.GetDecimal(5)), ExchangeJournalDisplay.Money(reader.GetDecimal(6)),
            ExchangeJournalDisplay.Money(reader.GetDecimal(7)));
        return filter.Status=="warning"?new([],0,fact):new(rows, total, fact);
    }
    public Task<ExchangeJournalDetailDto> ReadDetailAsync(string kind, Guid id, bool includeExcluded, int skip, int take, CancellationToken token) =>
        kind == "dds" ? ReadRegistryAsync(id, includeExcluded, skip, take, token)
            :kind=="payments"?ReadPaymentDetailAsync(id,skip,take,token):ReadAuditDetailAsync(kind, id, skip, take, token);

    private async Task<ExchangeJournalDetailDto> ReadRegistryAsync(Guid id, bool includeExcluded, int skip, int take, CancellationToken token)
    {
        var drill = await ReadDrillRegistryAsync(id, includeExcluded, skip, take, token);
        if (drill is not null) return drill;
        await using var connection = Replica(); await connection.OpenAsync(token);
        await using var command = connection.CreateCommand(); command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.ReadExchangeRegistry255"; command.CommandTimeout = 30;
        command.Parameters.Add("@RunId", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@IncludeExcluded", SqlDbType.Bit).Value = includeExcluded;
        command.Parameters.Add("@Skip", SqlDbType.Int).Value = skip; command.Parameters.Add("@Take", SqlDbType.Int).Value = take;
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token) || !reader.GetBoolean(0)) throw new ExchangeJournalEntryNotFoundException();
        var available = !reader.IsDBNull(1);
        if (!await reader.NextResultAsync(token) || !await reader.ReadAsync(token)) throw new InvalidOperationException("Missing registry totals.");
        var total = reader.GetInt32(0); var receipts = reader.GetDecimal(1); var payments = reader.GetDecimal(2);
        if (!await reader.NextResultAsync(token)) throw new InvalidOperationException("Missing registry rows.");
        var rows = new List<IReadOnlyList<string>>();
        while (await reader.ReadAsync(token))
        {
            if (rows.Count >= take) throw new InvalidOperationException("Unbounded registry rows.");
            var rule = reader.IsDBNull(6) ? "MISSING" : reader.GetString(6);
            rows.Add(new[] { ExchangeJournalDisplay.Text(reader.GetString(0)), ExchangeJournalDisplay.Text(reader.GetString(1)),
                ExchangeJournalDisplay.Text(reader.GetString(2)), ExchangeJournalDisplay.Text(reader.IsDBNull(3) ? null : reader.GetString(3)),
                reader.GetString(4) == "Dt" ? "Поступление" : "Выплата", ExchangeJournalDisplay.Money(reader.GetDecimal(5)),
                rule == "NONE" ? "Учтено" : rule == "MISSING" ? "Нет контрольной записи" : "Исключено по правилу обмена" });
        }
        return new("Реестр факта ДДС", available
            ? "Данные из SQL BMS, состав неизменяемого снимка выбранного обмена. Дата операции не равна времени загрузки. Итоги — только учтённые записи, независимо от страницы."
            : "Снимок для этого обмена не сформирован. Ошибочная загрузка не выдаётся за пустой успешный реестр.",
            ["Дата операции", "Источник SQL", "Статья ДДС источника", "Контрагент", "Движение", "Сумма, ₸", "Учёт"], rows, skip, take, total,
            available ? [new("Поступления", ExchangeJournalDisplay.Money(receipts)), new("Выплаты", ExchangeJournalDisplay.Money(payments)),
                new("Чистый поток", ExchangeJournalDisplay.Money(receipts - payments))] : []);
    }
    private Task<ExchangeJournalDetailDto> ReadAuditDetailAsync(string kind, Guid id, int skip, int take, CancellationToken token) => transactions.ExecuteAsync<ExchangeJournalDetailDto>(async ct =>
    {
        var entry = await db.BusinessAuditEntries.AsNoTracking().Where(a => a.Id == id
            && (kind == "currency" ? a.ObjectType == "CurrencyRateImport" : ReferenceTypes.Contains(a.ObjectType)))
            .Select(a => new { a.Action, a.ObjectType, a.ObjectId, a.CorrelationId, Before = a.BeforeJson != null && a.BeforeJson.Length <= 65536 ? a.BeforeJson : null,
                After = a.AfterJson.Length <= 65536 ? a.AfterJson : null }).SingleOrDefaultAsync(ct)
            ?? throw new ExchangeJournalEntryNotFoundException();
        IReadOnlyList<IReadOnlyList<string>> rows;
        if (kind == "currency")
        {
            var rates = await db.BusinessAuditEntries.AsNoTracking().Where(a => a.ObjectType == "CurrencyRate" && a.Action == "CurrencyRateImported"
                && a.CorrelationId == entry.CorrelationId).OrderBy(a => a.Id).Take(1000)
                .Select(a => a.AfterJson.Length <= 65536 ? a.AfterJson : null).ToArrayAsync(ct);
            rows = rates.Select(row => ExchangeCurrencyAuditRow(row, entry.ObjectId)).Where(row => row.Count > 0).ToArray();
        }
        else rows = entry.ObjectType == "CounterpartyImport" ? ExchangeJournalDisplay.CounterpartyImportDetails(entry.After)
            : ExchangeJournalDisplay.ReferenceChanges(entry.Before, entry.After);
        return new(kind == "currency" ? "Курсы валют выбранного обмена" : "Изменения справочника", rows.Count == 0
            ? "Событие зарегистрировано, но исторический журнал не содержит однозначно связанной детализации. Это не означает отсутствие изменений."
            : "Сохранённые в SQL BMS бизнес-поля события. Технические идентификаторы и служебные данные не выводятся.",
            kind == "currency" ? ["Валюта", "Дата курса", "Курс, ₸", "Номинал"] : ["Поле", "Было", "Стало"],
            rows.Skip(skip).Take(take).ToArray(), skip, take, rows.Count, []);
    }, token);
    private static IReadOnlyList<string> ExchangeCurrencyAuditRow(string? json, string rateDate)
    {
        try { using var value = System.Text.Json.JsonDocument.Parse(json ?? "{}");
            string Field(params string[] names) { foreach (var name in names) if (value.RootElement.TryGetProperty(name, out var p))
                return ExchangeJournalDisplay.Text(p.ValueKind == System.Text.Json.JsonValueKind.String ? p.GetString() : p.ToString()); return "—"; }
            return [Field("currencyCode", "code"), ExchangeJournalDisplay.Text(rateDate), Field("rateKzt", "rate"), Field("multiplicity", "nominal", "unit")];
        } catch (System.Text.Json.JsonException) { return []; }
    }
}
