using System.Globalization;
using Bms.Application.Treasury;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Bms.Api.Exports;

public static class GoodsCalendarExcel458
{
    private static readonly string[] Labels = ["Предоплата", "Консигнация", "Реализация"];
    public static MemoryStream CreateStream(GoodsCalendarDto458 result, CancellationToken token)
    {
        var stream = PaymentCalendarGridExcelExporter434.CreateStream(result.Calendar, token);
        try
        {
            using (var document = SpreadsheetDocument.Open(stream, true))
            {
                var workbook = document.WorkbookPart!;
                var sheets = workbook.Workbook!.Sheets!.Elements<Sheet>().ToArray();
                var days = ((WorksheetPart)workbook.GetPartById(sheets[0].Id!)).Worksheet!.GetFirstChild<SheetData>()!;
                var weeks = ((WorksheetPart)workbook.GetPartById(sheets[1].Id!)).Worksheet!.GetFirstChild<SheetData>()!;
                var daily = result.Categories.Select(c => c.Calendar.Days.Select(d => d.Amounts.TotalAmountKzt)
                    .Append(c.Calendar.Totals.TotalAmountKzt).ToArray()).ToArray();
                var weekly = result.Categories.Select(c => c.Calendar.Days.GroupBy(d => d.Date.DayNumber - ((int)d.Date.DayOfWeek + 6) % 7)
                    .Select(g => g.Any(d => d.Amounts.TotalAmountKzt is null) ? (decimal?)null : g.Sum(d => d.Amounts.TotalAmountKzt!.Value)).ToArray()).ToArray();
                AddColumns(days, daily, token); AddColumns(weeks, weekly, token);
                var details = ((WorksheetPart)workbook.GetPartById(sheets[2].Id!)).Worksheet!.GetFirstChild<SheetData>()!;
                details.RemoveAllChildren<Row>();
                details.Append(new Row(new[] { "Дата", "Статья", "Вид", "Контрагент", "В валюте", "Валюта", "Сумма, ₸", "Документ 1С" }.Select(s => (OpenXmlElement)Text(s, 1))));
                foreach (var row in result.Categories.SelectMany((c, i) => c.Calendar.Entries.Select(e => (Entry: e, Label: Labels[i])))
                    .OrderBy(r => r.Entry.BusinessDate))
                {
                    token.ThrowIfCancellationRequested(); var e = row.Entry;
                    details.Append(new Row(Text(e.BusinessDate.ToString("dd.MM.yy", CultureInfo.InvariantCulture)), Text(row.Label),
                        Text(e.Kind == "PLAN" ? "План" : e.Kind == "BANK_REVERSAL" ? "Сторно" : "Оплата"),
                        Text(e.CounterpartyName ?? "—"), Money(e.Amount), Text(e.CurrencyCode), Money(e.AmountKzt), Text(e.DocumentNumber ?? "—")));
                }
                var parameters = ((WorksheetPart)workbook.GetPartById(sheets[3].Id!)).Worksheet!.GetFirstChild<SheetData>()!;
                parameters.Append(new Row(Text("Товарные статьи"), Text("710004 Предоплата; 710002 Консигнация; 710001 Реализация.")));
                parameters.Append(new Row(Text("Планы смешанных заявок"), Text("Неоплаченный план распределяется пропорционально суммам строк заявки; округление до тиына с точным остатком в последней группе. Факт берётся по исходной статье ОДДС, не по пропорции заявки. Финансовые записи не меняются.")));
                parameters.Append(new Row(Text("Источник факта"), Text("ОДДС: все выплаты по трём товарным статьям на исходные даты, независимо от наличия заявки. Факт в тенге по данным 1С; возвраты уменьшают сумму. Оплаты из заявок повторно не добавляются.")));
                parameters.Append(new Row(Text("Фильтр статуса"), Text("Все/Оплачено включают независимый факт ОДДС. Иные статусы отбирают только строки с точной связью с видимой заявкой; статус несвязанным платежам не назначается.")));
            }
            token.ThrowIfCancellationRequested(); stream.Position = 0; return stream;
        }
        catch { stream.Dispose(); throw; }
    }
    private static void AddColumns(SheetData data, decimal?[][] values, CancellationToken token)
    {
        var rows = data.Elements<Row>().ToArray();
        rows[0].Append(Labels.Select(s => (OpenXmlElement)Text(s + ", ₸", 1)).ToArray());
        for (var i = 1; i < rows.Length; i++)
        { token.ThrowIfCancellationRequested(); rows[i].Append(values.Select(v => (OpenXmlElement)Money(v[i - 1])).ToArray()); }
    }
    private static Cell Text(string value, uint style = 0) => new(new InlineString(new Text(value) { Space = SpaceProcessingModeValues.Preserve }))
    { DataType = CellValues.InlineString, StyleIndex = style };
    private static Cell Money(decimal? value)
    {
        if (value is null) return Text("Нет курса");
        var number = value.Value.ToString("0.##", CultureInfo.InvariantCulture);
        if (decimal.Round(value.Value, 2) != value.Value || number.Replace(".", "").Replace("-", "").TrimStart('0').Length > 15)
            throw new PaymentCalendarExcelPrecisionException();
        return new(new CellValue(number)) { DataType = CellValues.Number, StyleIndex = 2 };
    }
}
