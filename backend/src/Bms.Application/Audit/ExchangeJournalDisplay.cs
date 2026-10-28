using System.Globalization;
using System.Text.Json;

namespace Bms.Application.Audit;

public static class ExchangeJournalDisplay
{
    public static string Text(string? value, string fallback = "—") => string.IsNullOrWhiteSpace(value)
        || value.Length > 2000 || value.Any(char.IsControl) ? fallback : value;
    public static string Money(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);
    public static string Failure(string? code) => code switch
    {
        "UNAPPROVED_SOURCE_ARTICLE" => "Новая статья источника не сопоставлена со справочником BMS. Факт не опубликован.",
        "SOURCE_ARTICLE_IDENTITY_CHANGED" => "Изменилось наименование или соответствие статьи источника. Требуется проверка сопоставления; факт не опубликован.",
        "SOURCE_UNAVAILABLE" or "SQL_SOURCE_UNAVAILABLE" => "SQL-источник недоступен. Обновление факта не подтверждено.",
        _ => "Обмен завершился с ошибкой. Новый факт не опубликован; проверьте источник и настройки обмена."
    };
    public static ExchangeJournalItemDto Audit(Guid id, DateTimeOffset at, string objectType, string action, string? after)
    {
        if (objectType == "CounterpartyImport")
        {
            var state = action == "CounterpartyImportCompleted" ? "success"
                : action is "CounterpartyImportWarnings" or "CounterpartyImportConflict" ? "warning" : "failed";
            int? count = null;
            try { using var json = JsonDocument.Parse(after ?? "{}");
                if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("changed", out var n)
                    && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var value) && value >= 0) count = value;
            } catch (JsonException) { }
            return new(id, "references", at, at, "Контрагенты · ежедневная загрузка 1С", state, count,
                state == "failed" ? "Загрузка контрагентов не завершена. Предыдущие записи сохранены."
                : action == "CounterpartyImportConflict" ? "Конфликт реквизитов: запись не изменена. Откройте подробности."
                : state == "warning" ? "Однозначные записи обработаны. Конфликтные оставлены без изменений; причины доступны в журнале."
                : "Однозначные записи обработаны. Изменений бюджетов и заявок нет.", true);
        }
        if(objectType=="AccountingPaymentExchange")
        {
            var state=action=="AccountingPaymentExchangeCompleted"?"success":action=="AccountingPaymentExchangeWarnings"?"warning":"failed";
            int? count=null;
            try{using var json=JsonDocument.Parse(after??"{}");if(json.RootElement.TryGetProperty("appliedDocuments",out var n)&&n.ValueKind==JsonValueKind.Number&&n.TryGetInt32(out var value)&&value>=0)count=value;}catch(JsonException){}
            return new(id,"payments",at,at,"Сверка оплат · 1С → расходные заявки",state,count,
                state=="failed"?"Сверка недоступна. Новые оплаты не подтверждены; проверьте загрузку платёжных документов и факта ДДС.":
                state=="warning"?"Есть исправления или неоднозначные оплаты, требующие проверки. Смотрите подробности.":
                "Сверка выполнена. Подтверждаются только однозначные оплаты; повторный обмен не создаёт дублей.",true);
        }
        if (objectType == "CurrencyRateImport")
        {
            var failed = action != "CurrencyRateImportSucceeded";
            int? count = null;
            try { using var json = JsonDocument.Parse(after ?? "{}");
                if (json.RootElement.TryGetProperty("count", out var number) && number.TryGetInt32(out var n) && n >= 0) count = n;
            } catch (JsonException) { }
            return new(id, "currency", at, at, "Курсы валют · НБ РК", failed ? "failed" : "success", count,
                failed ? "Курсы не получены. Проверьте доступность источника; при необходимости введите курс вручную."
                    : "Курсы валют записаны в SQL BMS.", true);
        }
        var display = ChangeJournalFormatter.Format(new(at, null, objectType, action, after));
        return new(id, "references", at, at, "Справочники · " + Text(display.Object), "success", 1,
            Text(display.Event) + ". " + Text(display.Description, "Изменение зарегистрировано в SQL BMS."), true);
    }
    public static IReadOnlyList<IReadOnlyList<string>> ReferenceChanges(string? before, string? after)
    {
        var fields = new (string Path, string Label)[] {
            ("name","Наименование"),("code","Код"),("bin","БИН/ИИН"),("isActive","Активен"),("isGroup","Группа"),
            ("contractDate","Дата договора"),("numericCode","Цифровой код валюты"),
            ("settings.name","Наименование"),("settings.costCenter.name","ЦФО"),
            ("settings.formsManagerialAccrual","Управленческие начисления"),
            ("settings.expenseItem.name","Статья затрат"),("settings.groupName","Группа статей"),
            ("settings.activityType","Вид деятельности"),("settings.flowDirection","Направление") };
        try
        {
            using var old = JsonDocument.Parse(string.IsNullOrWhiteSpace(before) ? "{}" : before);
            using var current = JsonDocument.Parse(string.IsNullOrWhiteSpace(after) ? "{}" : after);
            return fields.Select(field => (field.Label, Old: Value(old.RootElement, field.Path), New: Value(current.RootElement, field.Path)))
                .Where(row => row.Old != row.New).Select(row => (IReadOnlyList<string>)new[] { row.Label, row.Old, row.New }).ToArray();
        }
        catch (JsonException) { return []; }
    }
    public static IReadOnlyList<IReadOnlyList<string>> CounterpartyImportDetails(string? after)
    {
        try
        {
            using var document = JsonDocument.Parse(after ?? "{}"); var value = document.RootElement;
            if (value.ValueKind != JsonValueKind.Object) return [];
            var rows = new List<IReadOnlyList<string>>();
            foreach (var (key, label) in new[] { ("sourceRows", "Строк источника"), ("sourceGroups", "Групп источника"),
                ("inserted", "Добавлено"), ("updated", "Обновлено"), ("conflicts", "Пропущено конфликтных групп") })
                if (value.TryGetProperty(key, out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var count) && count >= 0)
                    rows.Add([label, "—", count.ToString(CultureInfo.InvariantCulture)]);
            if (value.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                rows.Add(["Причина пропуска", "—", Text(reason.GetString())]);
            if (value.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Array)
                foreach (var source in sources.EnumerateArray().Take(3))
                {
                    if (source.ValueKind != JsonValueKind.Object) continue;
                    string Field(string name) => source.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? Text(p.GetString()) : "—";
                    var label = "Источник 1С";
                    rows.Add([label + " · наименование", "—", Field("name")]);
                    rows.Add([label + " · БИН/ИИН", "—", Field("bin")]);
                    if (source.TryGetProperty("isActive", out var active) && active.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        rows.Add([label + " · активен", "—", active.GetBoolean() ? "Да" : "Нет"]);
                }
            return rows;
        }
        catch (JsonException) { return []; }
    }
    private static string Value(JsonElement value, string path)
    {
        foreach (var part in path.Split('.'))
        {
            if (value.ValueKind != JsonValueKind.Object) return "—";
            var match = value.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, part, StringComparison.OrdinalIgnoreCase));
            value = match.Value;
        }
        return value.ValueKind switch {
            JsonValueKind.True => "Да", JsonValueKind.False => "Нет",
            JsonValueKind.String => Text(value.GetString()) switch {
                "Operating" => "Операционная", "Investing" => "Инвестиционная", "Financing" => "Финансовая",
                "Receipt" => "Поступление", "Payment" => "Выплата", var text => text },
            JsonValueKind.Number => Text(value.GetRawText()), _ => "—" };
    }
}
