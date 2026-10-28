using System.Globalization;
using Bms.Application.Treasury;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Bms.Api.Exports;

/// <summary>Exports the same complete authorized grid model, never the list page.</summary>
public static class PaymentCalendarGridExcelExporter434
{
    public static MemoryStream CreateStream(PaymentCalendarGridDto grid, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var stream = new MemoryStream();
        try
        {
            using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
            {
                var workbook = document.AddWorkbookPart(); workbook.Workbook = new Workbook();
                workbook.AddNewPart<WorkbookStylesPart>().Stylesheet = Styles();
                var sheets = workbook.Workbook.AppendChild(new Sheets());
                var days = AddSheet(workbook, sheets, 1, "Дни");
                days.Append(Header("Дата", "План, ₸", "Оплачено, ₸", "Итого, ₸", "Строк без курса"));
                foreach (var day in grid.Days)
                {
                    token.ThrowIfCancellationRequested();
                    days.Append(new Row(Text(day.Date.ToString("dd.MM.yy", CultureInfo.InvariantCulture)),
                        Money(day.Amounts.PlanAmountKzt), Money(day.Amounts.ActualAmountKzt), Money(day.Amounts.TotalAmountKzt), Number(day.Amounts.UnknownPlanCount)));
                }
                days.Append(new Row(Text("Итого за период", 1), Money(grid.Totals.PlanAmountKzt), Money(grid.Totals.ActualAmountKzt),
                    Money(grid.Totals.TotalAmountKzt), Number(grid.Totals.UnknownPlanCount)));
                var weeks = AddSheet(workbook, sheets, 2, "Недели");
                weeks.Append(Header("Неделя с", "по", "План, ₸", "Оплачено, ₸", "Итого, ₸", "Строк без курса"));
                foreach (var week in grid.Days.GroupBy(day => day.Date.DayNumber - ((int)day.Date.DayOfWeek + 6) % 7))
                {
                    token.ThrowIfCancellationRequested();
                    var first = week.First().Date; var last = week.Last().Date;
                    var known = week.Sum(day => day.Amounts.KnownPlanAmountKzt);
                    var unknown = week.Sum(day => day.Amounts.UnknownPlanCount);
                    var actual = week.Sum(day => day.Amounts.ActualAmountKzt);
                    var amounts = new PaymentCalendarGridAmounts(known, unknown, actual);
                    weeks.Append(new Row(Text(first.ToString("dd.MM.yy", CultureInfo.InvariantCulture)), Text(last.ToString("dd.MM.yy", CultureInfo.InvariantCulture)),
                        Money(amounts.PlanAmountKzt), Money(amounts.ActualAmountKzt), Money(amounts.TotalAmountKzt), Number(unknown)));
                }
                var details = AddSheet(workbook, sheets, 3, "Расшифровка");
                details.Append(Header("Дата", "Вид", "Контрагент", "В валюте", "Валюта", "Сумма, ₸"));
                foreach (var entry in grid.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    details.Append(new Row(Text(entry.BusinessDate.ToString("dd.MM.yy", CultureInfo.InvariantCulture)),
                        Text(entry.Kind == "PLAN" ? "План" : entry.Kind == "BANK_REVERSAL" ? "Сторно" : "Оплата"),
                        Text(entry.CounterpartyName ?? "—"), Money(entry.Amount), Text(entry.CurrencyCode), Money(entry.AmountKzt)));
                }
                var parameters = AddSheet(workbook, sheets, 4, "Параметры");
                parameters.Append(Header("Параметр", "Значение"));
                parameters.Append(new Row(Text("Период"), Text($"{grid.From:dd.MM.yy} — {grid.Through:dd.MM.yy}")));
                parameters.Append(new Row(Text("Часовой пояс"), Text(grid.BusinessTimeZoneId)));
                parameters.Append(new Row(Text("План"), Text("Неоплаченный остаток на плановую дату; сохранённый предварительный курс заявки.")));
                parameters.Append(new Row(Text("Оплачено"), Text("Банковские движения на фактические даты, за вычетом сторно; без повторного учёта зачётов и округлений.")));
                parameters.Append(new Row(Text("Неделя"), Text("Сумма дат выбранного периода; неполная крайняя неделя не включает даты вне фильтра.")));
                parameters.Append(new Row(Text("Нет курса"), Text("Неизвестная сумма не заменяется нулём. Полный итог для такой даты или недели не рассчитан.")));
                workbook.Workbook.Save();
            }
            token.ThrowIfCancellationRequested(); stream.Position = 0; return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    private static SheetData AddSheet(WorkbookPart workbook, Sheets sheets, uint id, string name)
    {
        var part = workbook.AddNewPart<WorksheetPart>();
        var data = new SheetData();
        part.Worksheet = new Worksheet(new Columns(new Column { Min = 1, Max = 8, Width = 24, CustomWidth = true }), data);
        sheets.Append(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = id, Name = name });
        return data;
    }
    private static Row Header(params string[] values) => new(values.Select(value => (OpenXmlElement)Text(value, 1)));
    private static Cell Text(string value, uint style = 0) => new(new InlineString(new Text(value) { Space = SpaceProcessingModeValues.Preserve }))
        { DataType = CellValues.InlineString, StyleIndex = style };
    private static Cell Number(int value) => new(new CellValue(value.ToString(CultureInfo.InvariantCulture))) { DataType = CellValues.Number };
    private static Cell Money(decimal? value)
    {
        if (value is null) return Text("Нет курса");
        var text = value.Value.ToString("0.##", CultureInfo.InvariantCulture);
        if (decimal.Round(value.Value, 2, MidpointRounding.ToEven) != value.Value
            || text.Replace(".", string.Empty).Replace("-", string.Empty).TrimStart('0').Length > 15)
            throw new PaymentCalendarExcelPrecisionException();
        return new(new CellValue(text)) { DataType = CellValues.Number, StyleIndex = 2 };
    }
    private static Stylesheet Styles() => new(
        new NumberingFormats(new NumberingFormat { NumberFormatId = 164, FormatCode = "#,##0" }) { Count = 1 },
        new Fonts(new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 11 }, new Color { Rgb = "FF8F0B24" }, new FontName { Val = "Calibri" })) { Count = 2 },
        new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FFF1F1F1" }) { PatternType = PatternValues.Solid })) { Count = 3 },
        new Borders(new Border()) { Count = 1 }, new CellStyleFormats(new CellFormat()) { Count = 1 },
        new CellFormats(new CellFormat(),
            new CellFormat(new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center, WrapText = true })
                { FontId = 1, FillId = 2, ApplyFont = true, ApplyFill = true, ApplyAlignment = true },
            new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true }) { Count = 3 });
}
