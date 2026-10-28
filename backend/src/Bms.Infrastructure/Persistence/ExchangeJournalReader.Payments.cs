using System.Data;
using System.Text.Json;
using Bms.Application.Audit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed partial class ExchangeJournalReader
{
    private async Task<SourcePage> ReadPaymentRunsAsync(ExchangeJournalFilter filter,int take,CancellationToken token)
    {
        await using var connection=Replica();await connection.OpenAsync(token);
        await using var q=connection.CreateCommand();q.CommandType=CommandType.StoredProcedure;q.CommandText="dbo.ReadPaymentExchange258";q.CommandTimeout=30;
        q.Parameters.Add("@FromUtc",SqlDbType.DateTimeOffset).Value=(object?)filter.FromUtc??DBNull.Value;
        q.Parameters.Add("@ToUtc",SqlDbType.DateTimeOffset).Value=(object?)filter.ToUtc??DBNull.Value;
        q.Parameters.Add("@Status",SqlDbType.VarChar,10).Value=filter.Status=="warning"?"all":filter.Status;
        q.Parameters.Add("@Take",SqlDbType.Int).Value=take;
        await using var r=await q.ExecuteReaderAsync(token);if(!await r.ReadAsync(token))throw new InvalidOperationException("Payment journal count missing.");
        var total=r.GetInt32(0);if(!await r.NextResultAsync(token))throw new InvalidOperationException("Payment journal rows missing.");
        var rows=new List<ExchangeJournalItemDto>();
        while(await r.ReadAsync(token))
        {
            if(rows.Count>=take)throw new InvalidOperationException("Payment journal bound exceeded.");
            var state=r.GetString(3);var reason=r.IsDBNull(4)?null:r.GetString(4);
            rows.Add(new(r.GetGuid(0),"payments",r.GetFieldValue<DateTimeOffset>(1),r.IsDBNull(2)?null:r.GetFieldValue<DateTimeOffset>(2),
                "Платёжные документы · 1С → SQL BMS",state=="PASS"?"success":state=="FAIL"?"failed":"running",
                r.IsDBNull(5)?null:r.GetInt32(5),state=="PASS"?"Первичные платёжные документы загружены. Это ещё не означает, что каждая оплата сопоставлена с заявкой.":
                state=="RUNNING"?"Загрузка начата; результат ещё не зарегистрирован.":reason=="DDS_FAILED"?"Не обновлён факт ДДС. Сверка оплат не запускается по несогласованным данным.":
                reason=="SOURCE_FAILED"?"Не получены платёжные документы 1С. Предыдущий подтверждённый срез сохранён.":"Не удалось опубликовать платёжные документы. Предыдущий подтверждённый срез сохранён.",state=="PASS"));
        }
        return filter.Status=="warning"?new([],0,null):new(rows,total,null);
    }

    private async Task<ExchangeJournalDetailDto> ReadPaymentDetailAsync(Guid id,int skip,int take,CancellationToken token)
    {
        var entry=await db.BusinessAuditEntries.AsNoTracking().Where(a=>a.Id==id&&a.ObjectType=="AccountingPaymentExchange")
            .Select(a=>a.AfterJson).SingleOrDefaultAsync(token);
        if(entry is not null)
        {
            using var document=JsonDocument.Parse(entry);var value=document.RootElement;
            var rows=new List<IReadOnlyList<string>>();
            foreach(var field in new[]{("appliedDocuments","Подтверждено документов"),("reversedDocuments","Сторнировано документов"),("ambiguousRequests","Неоднозначных заявок")})
                if(value.TryGetProperty(field.Item1,out var n)&&n.ValueKind==JsonValueKind.Number&&n.TryGetInt32(out var count)&&count>=0)rows.Add([field.Item2,count.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            if(value.TryGetProperty("warnings",out var warnings)&&warnings.ValueKind==JsonValueKind.Array)
                foreach(var warning in warnings.EnumerateArray().Take(100))rows.Add(["Предупреждение",ExchangeJournalDisplay.Text(warning.GetString())]);
            if(value.TryGetProperty("information",out var info)&&info.ValueKind==JsonValueKind.String)rows.Add(["Результат",ExchangeJournalDisplay.Text(info.GetString())]);
            return new("Результат сверки оплат","Данные журнала SQL BMS. Неоднозначная, неполная или несогласованная с ДДС оплата не подтверждается автоматически.",
                ["Показатель / заявка","Результат"],rows.Skip(skip).Take(take).ToArray(),skip,take,rows.Count,[]);
        }
        await using var connection=Replica();await connection.OpenAsync(token);
        await using var q=connection.CreateCommand();q.CommandType=CommandType.StoredProcedure;q.CommandText="dbo.ReadPaymentRegistry258";q.CommandTimeout=30;
        q.Parameters.Add("@RunId",SqlDbType.UniqueIdentifier).Value=id;q.Parameters.Add("@Skip",SqlDbType.Int).Value=skip;q.Parameters.Add("@Take",SqlDbType.Int).Value=take;
        await using var r=await q.ExecuteReaderAsync(token);
        if(!await r.ReadAsync(token)||!r.GetBoolean(0))throw new ExchangeJournalEntryNotFoundException();
        var available=r.GetBoolean(1);
        if(!await r.NextResultAsync(token)||!await r.ReadAsync(token))throw new InvalidOperationException("Payment registry totals missing.");
        var total=r.GetInt32(0);var amount=r.GetDecimal(1);
        if(!await r.NextResultAsync(token))throw new InvalidOperationException("Payment registry rows missing.");
        var rowsSource=new List<(string Key,string Org,string Party,DateOnly Day,string Number,decimal Amount,string Numeric,decimal Kzt)>();
        while(await r.ReadAsync(token))
        {
            if(rowsSource.Count>=take)throw new InvalidOperationException("Payment registry bound exceeded.");
            rowsSource.Add((r.GetString(0),r.GetString(1),r.GetString(2),DateOnly.FromDateTime(r.GetDateTime(3)),r.GetString(4),r.GetDecimal(5),r.GetString(6),r.GetDecimal(7)));
        }
        await r.DisposeAsync();
        var codes=await db.Currencies.AsNoTracking().ToArrayAsync(token);
        var organizations=await db.Organizations.AsNoTracking().ToArrayAsync(token);
        var keys=rowsSource.Select(row=>row.Key).ToArray();
        var links=await db.AccountingPaymentLinks.AsNoTracking().Where(l=>keys.Contains(l.DocumentKey)&&l.ReversedAtUtc==null)
            .Select(l=>new{l.DocumentKey,l.ExpenseRequestId}).ToArrayAsync(token);
        var requestIds=links.Select(l=>l.ExpenseRequestId).ToArray();
        var numbers=await db.ExpenseRequests.AsNoTracking().Where(e=>requestIds.Contains(e.Id)).Select(e=>new{e.Id,e.RequestNumber}).ToDictionaryAsync(e=>e.Id,e=>e.RequestNumber,token);
        var rowsFinal=rowsSource.Select(row=>(IReadOnlyList<string>)new[]{
            ExchangeJournalDisplay.Text(organizations.SingleOrDefault(o=>o.Code==row.Org)?.Name),row.Day.ToString("dd.MM.yyyy"),ExchangeJournalDisplay.Text(row.Party),
            ExchangeJournalDisplay.Text(row.Number),ExchangeJournalDisplay.Money(row.Amount)+" "+(codes.SingleOrDefault(c=>c.NumericCode==row.Numeric)?.Code??row.Numeric),
            ExchangeJournalDisplay.Money(row.Kzt),links.SingleOrDefault(l=>l.DocumentKey==row.Key) is { } link
                ?ExchangeJournalDisplay.Text(numbers.GetValueOrDefault(link.ExpenseRequestId),"Сопоставлена с недоступной заявкой"):"Не сопоставлена с заявкой BMS"}).ToArray();
        return new("Реестр платёжных документов 1С","Состав выбранного неизменяемого среза из SQL BMS. Показаны проведённые исходящие платежи; наличие в реестре само по себе не подтверждает оплату заявки.",
            ["Организация","День оплаты","Контрагент","Платёжный документ","Сумма в валюте","Эквивалент, ₸","Связь с заявкой"],rowsFinal,skip,take,total,
            available?[new("Эквивалент исходящих платежей",ExchangeJournalDisplay.Money(amount))]:[]);
    }
}
