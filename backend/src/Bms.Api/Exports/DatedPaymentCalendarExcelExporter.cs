using System.Globalization;
using System.Xml;
using Bms.Application.Treasury;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Bms.Api.Exports;

/// <summary>
/// Renders the complete, previously reconciled dated export model. It does not
/// read pages, recalculate money, convert currencies or cap actual movements.
/// User text is literal; all numeric cells preserve the accepted Excel precision.
/// </summary>
public static class DatedPaymentCalendarExcelExporter
{
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const long MaximumExcelRows = 1_048_576;
    private const uint HeaderStyle = 1;
    private const uint TextStyle = 2;
    private const uint MoneyStyle = 3;
    private const uint TotalStyle = 4;
    private const string KztTotalLabel = "KZT — общий итог";

    private static readonly string[] Headers =
    [
        "Дата", "Вид строки", "Статус", "Валюта", "Контрагент", "Назначение",
        "Код организации", "Организация", "План, валюта", "План, KZT",
        "Факт, валюта", "Факт, KZT"
    ];

    private static readonly string[] SummaryHeaders =
    [
        "Валюта / итог", "Плановых строк", "План", "Банк", "Сторно банка",
        "Зачёт", "Факт, всего", "Плановых строк без KZT"
    ];

    public static string FileName(DateTimeOffset generatedAtUtc) =>
        "dated-payment-calendar-" + generatedAtUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "Z.xlsx";

    /// <summary>
    /// Completes the workbook before response publication. The caller owns the
    /// returned stream; cancellation or failure disposes the unfinished package.
    /// </summary>
    public static MemoryStream CreateStream(
        DatedPaymentCalendarExportData export, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(export.Query);
        ArgumentNullException.ThrowIfNull(export.Rows);
        ArgumentNullException.ThrowIfNull(export.CurrencyTotals);
        ArgumentNullException.ThrowIfNull(export.KztTotals);
        ArgumentNullException.ThrowIfNull(export.OrganizationGroups);
        cancellationToken.ThrowIfCancellationRequested();
        new DatedPaymentCalendarQuery(export.Query.From, export.Query.Through, 0, 1,
            export.Query.StatusCode, export.Query.CurrencyCode)
        { Grouping = export.Query.Grouping }.Validate();
        ValidateShape(export, cancellationToken);

        var stream = new MemoryStream();
        try
        {
            using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
            {
                var workbook = document.AddWorkbookPart();
                workbook.Workbook = new Workbook();
                workbook.AddNewPart<WorkbookStylesPart>().Stylesheet = Styles();
                var sheets = workbook.Workbook.AppendChild(new Sheets());
                var calendar = AddSheet(workbook, sheets, 1, "Датированный календарь");
                WriteCalendar(calendar, export, cancellationToken);
                var summary = AddSheet(workbook, sheets, 2, "Итоги");
                WriteSummary(summary, export, cancellationToken);
                var parameters = AddSheet(workbook, sheets, 3, "Параметры");
                WriteParameters(parameters, export, cancellationToken);
                if (export.Query.Grouping == PaymentCalendarGroupingCodes.Organization)
                {
                    var organizations = AddSheet(workbook, sheets, 4, "Организации");
                    WriteOrganizations(organizations, export, cancellationToken);
                }
                workbook.Workbook.Save();
                cancellationToken.ThrowIfCancellationRequested();
            }
            cancellationToken.ThrowIfCancellationRequested();
            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static WorksheetPart AddSheet(WorkbookPart workbook, Sheets sheets, uint id, string name)
    {
        var part = workbook.AddNewPart<WorksheetPart>();
        sheets.Append(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = id, Name = name });
        return part;
    }

    private static void ValidateShape(DatedPaymentCalendarExportData export, CancellationToken cancellationToken)
    {
        if (export.Rows.Count > PaymentCalendarExportPolicy.MaximumRows
            || export.CurrencyTotals.Count > PaymentCalendarExportPolicy.MaximumRows
            || export.OrganizationGroups.Count > PaymentCalendarExportPolicy.MaximumRows
            || 1L + export.Rows.Count > MaximumExcelRows
            || 3L + export.CurrencyTotals.Count > MaximumExcelRows)
        {
            throw new DatedPaymentCalendarScopeTooLargeException();
        }
        if (string.IsNullOrWhiteSpace(export.BusinessTimeZoneId)) throw new PaymentCalendarUnavailableException();

        // Only structural/count/null consistency is checked here. Monetary
        // totals are authoritative values from the full application report.
        var counts = new Dictionary<string, RowCounts>(StringComparer.Ordinal);
        foreach (var row in export.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row is null || row.BusinessDate < export.Query.From || row.BusinessDate > export.Query.Through
                || !IsCurrencyCode(row.CurrencyCode)
                || (export.Query.StatusCode is not null && row.Status != export.Query.StatusCode)
                || (export.Query.CurrencyCode is not null && row.CurrencyCode != export.Query.CurrencyCode)
                || (row.OrganizationCode is null) != (row.OrganizationName is null))
            {
                throw new PaymentCalendarUnavailableException();
            }
            _ = KindLabel(row.Kind);
            _ = PaymentCalendarExcelExporter.StatusLabel(row.Status);
            var plan = row.Kind == DatedPaymentCalendarRowKindCodes.Plan;
            if (plan
                ? row.PlanAmount is null || row.SignedAmount is not null || row.SignedAmountKzt is not null
                : row.PlanAmount is not null || row.PlanAmountKzt is not null || row.SignedAmount is null || row.SignedAmountKzt is null)
            {
                throw new PaymentCalendarUnavailableException();
            }
            CountRow(counts, row);
        }
        ValidateSummaryShape(export.CurrencyTotals, export.KztTotals, counts, cancellationToken);

        if (export.Query.Grouping == PaymentCalendarGroupingCodes.Date)
        {
            if (export.OrganizationGroups.Count != 0) throw new PaymentCalendarUnavailableException();
            return;
        }

        var nextRow = 0;
        long organizationSheetRows = 1;
        foreach (var group in export.OrganizationGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group is null || group.CurrencyTotals is null || group.KztTotals is null
                || group.FirstRowIndex != nextRow || group.Total <= 0 || group.Total > export.Rows.Count - nextRow
                || (group.OrganizationCode is null) != (group.OrganizationName is null)
                || (!group.HasSourceOrganization && group.OrganizationCode is not null))
            {
                throw new PaymentCalendarUnavailableException();
            }
            organizationSheetRows += (long)group.CurrencyTotals.Count + 1;
            if (organizationSheetRows > MaximumExcelRows) throw new DatedPaymentCalendarScopeTooLargeException();
            var groupCounts = new Dictionary<string, RowCounts>(StringComparer.Ordinal);
            var end = nextRow + group.Total;
            for (var index = nextRow; index < end; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = export.Rows[index];
                if (row.OrganizationCode != group.OrganizationCode || row.OrganizationName != group.OrganizationName)
                {
                    throw new PaymentCalendarUnavailableException();
                }
                CountRow(groupCounts, row);
            }
            ValidateSummaryShape(group.CurrencyTotals, group.KztTotals, groupCounts, cancellationToken);
            nextRow = end;
        }
        if (nextRow != export.Rows.Count) throw new PaymentCalendarUnavailableException();
    }

    private static bool IsCurrencyCode(string? value) =>
        value is { Length: 3 } && value.All(character => character is >= 'A' and <= 'Z');

    private static void CountRow(Dictionary<string, RowCounts> counts, DatedPaymentCalendarExportRow row)
    {
        counts.TryGetValue(row.CurrencyCode, out var previous);
        counts[row.CurrencyCode] = row.Kind == DatedPaymentCalendarRowKindCodes.Plan
            ? new RowCounts(previous.Plans + 1, previous.UnknownKztPlans + (row.PlanAmountKzt is null ? 1 : 0))
            : previous;
    }

    private static void ValidateSummaryShape(
        IReadOnlyList<DatedPaymentCalendarCurrencyTotalsDto> currencies,
        DatedPaymentCalendarKztTotalsDto kzt,
        Dictionary<string, RowCounts> counts,
        CancellationToken cancellationToken)
    {
        if (currencies.Count != counts.Count) throw new PaymentCalendarUnavailableException();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var plans = 0;
        var unknown = 0;
        foreach (var total in currencies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (total is null || !IsCurrencyCode(total.CurrencyCode) || !seen.Add(total.CurrencyCode)
                || !counts.TryGetValue(total.CurrencyCode, out var count) || total.PlanCount != count.Plans)
            {
                throw new PaymentCalendarUnavailableException();
            }
            plans += count.Plans;
            unknown += count.UnknownKztPlans;
        }
        if (kzt.PlanCount != plans || kzt.PlanKztUnknownCount != unknown
            || (kzt.PlanAmountKzt is null) != (unknown > 0))
        {
            throw new PaymentCalendarUnavailableException();
        }
    }

    private static void WriteCalendar(
        WorksheetPart part, DatedPaymentCalendarExportData export, CancellationToken cancellationToken)
    {
        using var writer = OpenXmlWriter.Create(part);
        StartSheet(writer, [17, 29, 24, 12, 32, 48, 24, 42, 22, 22, 22, 22]);
        WriteHeader(writer, Headers);
        uint index = 2;
        var groupIndex = 0;
        foreach (var item in export.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var organizationName = item.OrganizationName ?? "Нет справочных данных";
            if (export.Query.Grouping == PaymentCalendarGroupingCodes.Organization)
            {
                while ((long)index - 2 >= (long)export.OrganizationGroups[groupIndex].FirstRowIndex
                    + export.OrganizationGroups[groupIndex].Total)
                {
                    groupIndex++;
                }
                organizationName = OrganizationLabel(export.OrganizationGroups[groupIndex]);
            }
            writer.WriteElement(new Row(
                TextCell(index, 1, item.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                TextCell(index, 2, KindLabel(item.Kind)),
                TextCell(index, 3, PaymentCalendarExcelExporter.StatusLabel(item.Status)),
                TextCell(index, 4, item.CurrencyCode),
                TextCell(index, 5, item.CounterpartyName ?? string.Empty),
                TextCell(index, 6, item.Purpose ?? string.Empty),
                TextCell(index, 7, item.OrganizationCode ?? string.Empty),
                TextCell(index, 8, organizationName),
                NullableNumberCell(index, 9, item.PlanAmount),
                NullableNumberCell(index, 10, item.PlanAmountKzt),
                NullableNumberCell(index, 11, item.SignedAmount),
                NullableNumberCell(index, 12, item.SignedAmountKzt))
            { RowIndex = index });
            index++;
        }
        cancellationToken.ThrowIfCancellationRequested();
        writer.WriteEndElement();
        // The main sheet contains only the header and ALL business rows. Summary
        // sheets cannot enter this filter, including for an empty period.
        writer.WriteElement(new AutoFilter { Reference = "A1:L" + (index - 1).ToString(CultureInfo.InvariantCulture) });
        writer.WriteEndElement();
    }

    private static void WriteSummary(
        WorksheetPart part, DatedPaymentCalendarExportData export, CancellationToken cancellationToken)
    {
        using var writer = OpenXmlWriter.Create(part);
        StartSheet(writer, [27, 20, 23, 23, 23, 23, 23, 27]);
        WriteHeader(writer, SummaryHeaders);
        uint index = 2;
        foreach (var total in export.CurrencyTotals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteElement(new Row(TextCell(index, 1, total.CurrencyCode),
                NumberCell(index, 2, total.PlanCount), NumberCell(index, 3, total.PlanAmount),
                NumberCell(index, 4, total.BankPaymentAmount), NumberCell(index, 5, total.BankReversalAmount),
                NumberCell(index, 6, total.AppliedAllocationAmount), NumberCell(index, 7, total.MovementAmount),
                NullableNumberCell(index, 8, null))
            { RowIndex = index });
            index++;
        }
        index++;
        var kzt = export.KztTotals;
        writer.WriteElement(new Row(TextCell(index, 1, KztTotalLabel), NumberCell(index, 2, kzt.PlanCount, TotalStyle),
            NullableNumberCell(index, 3, kzt.PlanAmountKzt, TotalStyle),
            NumberCell(index, 4, kzt.BankPaymentAmountKzt, TotalStyle), NumberCell(index, 5, kzt.BankReversalAmountKzt, TotalStyle),
            NumberCell(index, 6, kzt.AppliedAllocationAmountKzt, TotalStyle), NumberCell(index, 7, kzt.MovementAmountKzt, TotalStyle),
            NumberCell(index, 8, kzt.PlanKztUnknownCount, TotalStyle))
        { RowIndex = index });
        cancellationToken.ThrowIfCancellationRequested();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteOrganizations(
        WorksheetPart part, DatedPaymentCalendarExportData export, CancellationToken cancellationToken)
    {
        using var writer = OpenXmlWriter.Create(part);
        StartSheet(writer, [12, 24, 42, 25, 18, 27, 20, 23, 23, 23, 23, 23, 27]);
        WriteHeader(writer, ["Группа", "Код организации", "Организация", "Строки календаря", "Строк в группе",
            "Валюта / итог", "Плановых строк", "План", "Банк", "Сторно банка", "Зачёт", "Факт, всего", "Плановых строк без KZT"]);
        uint index = 2;
        for (var groupIndex = 0; groupIndex < export.OrganizationGroups.Count; groupIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var group = export.OrganizationGroups[groupIndex];
            foreach (var total in group.CurrencyTotals)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = OrganizationPrefix(index, groupIndex, group, total.CurrencyCode);
                row.Append(NumberCell(index, 7, total.PlanCount), NumberCell(index, 8, total.PlanAmount),
                    NumberCell(index, 9, total.BankPaymentAmount), NumberCell(index, 10, total.BankReversalAmount),
                    NumberCell(index, 11, total.AppliedAllocationAmount), NumberCell(index, 12, total.MovementAmount),
                    NullableNumberCell(index, 13, null));
                writer.WriteElement(row);
                index++;
            }
            var kzt = group.KztTotals;
            var kztRow = OrganizationPrefix(index, groupIndex, group, KztTotalLabel);
            kztRow.Append(NumberCell(index, 7, kzt.PlanCount, TotalStyle),
                NullableNumberCell(index, 8, kzt.PlanAmountKzt, TotalStyle),
                NumberCell(index, 9, kzt.BankPaymentAmountKzt, TotalStyle), NumberCell(index, 10, kzt.BankReversalAmountKzt, TotalStyle),
                NumberCell(index, 11, kzt.AppliedAllocationAmountKzt, TotalStyle), NumberCell(index, 12, kzt.MovementAmountKzt, TotalStyle),
                NumberCell(index, 13, kzt.PlanKztUnknownCount, TotalStyle));
            writer.WriteElement(kztRow);
            index++;
        }
        cancellationToken.ThrowIfCancellationRequested();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static Row OrganizationPrefix(uint index, int groupIndex, DatedPaymentCalendarExportOrganizationGroup group, string currency)
    {
        var range = "A" + (group.FirstRowIndex + 2).ToString(CultureInfo.InvariantCulture)
            + ":L" + (group.FirstRowIndex + group.Total + 1).ToString(CultureInfo.InvariantCulture);
        return new Row(NumberCell(index, 1, groupIndex + 1), TextCell(index, 2, group.OrganizationCode ?? string.Empty),
            TextCell(index, 3, OrganizationLabel(group)), TextCell(index, 4, range), NumberCell(index, 5, group.Total),
            TextCell(index, 6, currency))
        { RowIndex = index };
    }

    private static string OrganizationLabel(DatedPaymentCalendarExportOrganizationGroup group) =>
        group.OrganizationName ?? (group.HasSourceOrganization ? "Не найдена в справочнике" : "Не указана");

    private static void WriteParameters(
        WorksheetPart part, DatedPaymentCalendarExportData export, CancellationToken cancellationToken)
    {
        using var writer = OpenXmlWriter.Create(part);
        StartSheet(writer, [32, 105]);
        WriteHeader(writer, ["Параметр", "Значение"]);
        (string Label, string Value)[] parameters =
        [
            ("Период с (включительно)", export.Query.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("Период по (включительно)", export.Query.Through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("Часовой пояс дат", export.BusinessTimeZoneId),
            ("Статус", export.Query.StatusCode is null ? "Все" : PaymentCalendarExcelExporter.StatusLabel(export.Query.StatusCode)),
            ("Валюта", export.Query.CurrencyCode ?? "Все; итоги раздельно по валютам"),
            ("Группировка", export.Query.Grouping == PaymentCalendarGroupingCodes.Organization ? "По организациям" : "По датам"),
            ("Охват", "Вся доступная выборка по фильтрам; без пагинации. Итоги получены из того же полного отчёта."),
            ("Сформирован (UTC)", export.GeneratedAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            ("Точность сумм", "Суммы показаны целыми; исходная точность сохранена в числовых ячейках. Значения свыше 15 значащих цифр отклоняются без округления."),
            ("План и факт", "План показан отдельно от фактических движений. Банк и зачёт не ограничены суммой плана; сторно банка показано со знаком минус на собственной дате."),
            ("Неизвестный план, KZT", "Пустая ячейка «План, KZT» у плановой строки означает неизвестный исторический KZT, не ноль. При таких строках итог плана KZT тоже пуст; их количество показано отдельно."),
            ("Неприменимые поля", "У плана поля факта пусты; у движения поля плана пусты. В итогах по валютам количество неизвестного KZT не подставляется: этот показатель приведён в отдельном итоге KZT."),
            ("Номер группы и диапазон", "На листе «Организации» порядковая группа и диапазон строк связывают итоги с календарём. Это не идентификаторы организации. При группировке по датам отсутствие справочных данных не уточняет причину.")
        ];
        uint index = 2;
        foreach (var parameter in parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteElement(new Row(TextCell(index, 1, parameter.Label), TextCell(index, 2, parameter.Value))
            { RowIndex = index, Height = 45, CustomHeight = true });
            index++;
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void StartSheet(OpenXmlWriter writer, double[] widths)
    {
        writer.WriteStartElement(new Worksheet());
        writer.WriteElement(new SheetViews(new SheetView(new Pane
        {
            VerticalSplit = 1,
            TopLeftCell = "A2",
            ActivePane = PaneValues.BottomLeft,
            State = PaneStateValues.Frozen
        })
        { WorkbookViewId = 0 }));
        var columns = new Columns();
        for (var index = 0; index < widths.Length; index++)
        {
            columns.Append(new Column { Min = (uint)index + 1, Max = (uint)index + 1, Width = widths[index], CustomWidth = true });
        }
        writer.WriteElement(columns);
        writer.WriteStartElement(new SheetData());
    }

    private static void WriteHeader(OpenXmlWriter writer, string[] headers) =>
        writer.WriteElement(new Row(headers.Select((text, index) => TextCell(1, index + 1, text, HeaderStyle)))
        { RowIndex = 1, Height = 36, CustomHeight = true });

    private static Cell TextCell(uint row, int column, string text, uint style = TextStyle)
    {
        if (text.Length > 32_767) throw new PaymentCalendarUnavailableException();
        try
        {
            XmlConvert.VerifyXmlChars(text);
        }
        catch (XmlException exception)
        {
            throw new PaymentCalendarUnavailableException(exception);
        }
        return new Cell(new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve }))
        { CellReference = Reference(row, column), DataType = CellValues.InlineString, StyleIndex = style };
    }

    private static Cell NullableNumberCell(uint row, int column, decimal? value, uint style = MoneyStyle) =>
        value is { } known ? NumberCell(row, column, known, style)
            : new Cell { CellReference = Reference(row, column), StyleIndex = style };

    private static Cell NumberCell(uint row, int column, decimal value, uint style = MoneyStyle)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        var significantDigits = text.Replace(".", string.Empty, StringComparison.Ordinal)
            .TrimStart('-', '0').TrimEnd('0').Length;
        if (significantDigits > 15) throw new PaymentCalendarExcelPrecisionException();
        return new Cell(new CellValue(text))
        { CellReference = Reference(row, column), DataType = CellValues.Number, StyleIndex = style };
    }

    private static string Reference(uint row, int column) =>
        $"{(char)('A' + column - 1)}{row.ToString(CultureInfo.InvariantCulture)}";

    private static string KindLabel(string kind) => kind switch
    {
        DatedPaymentCalendarRowKindCodes.Plan => "План",
        DatedPaymentCalendarRowKindCodes.BankPayment => "Банковский платёж",
        DatedPaymentCalendarRowKindCodes.BankReversal => "Сторно банковского платежа",
        DatedPaymentCalendarRowKindCodes.AppliedAllocation => "Зачёт",
        _ => throw new PaymentCalendarUnavailableException()
    };

    private static Stylesheet Styles() => new(
        new NumberingFormats(new NumberingFormat { NumberFormatId = 164, FormatCode = "#,##0" }) { Count = 1 },
        new Fonts(new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" }))
        { Count = 2 },
        new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FFE3EDF7" }, new BackgroundColor { Indexed = 64 })
            { PatternType = PatternValues.Solid }))
        { Count = 3 },
        new Borders(new Border()) { Count = 1 },
        new CellStyleFormats(new CellFormat()) { Count = 1 },
        new CellFormats(new CellFormat(),
            new CellFormat
            {
                FontId = 1,
                FillId = 2,
                ApplyFont = true,
                ApplyFill = true,
                ApplyAlignment = true,
                Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center, WrapText = true }
            },
            new CellFormat
            {
                QuotePrefix = true,
                ApplyAlignment = true,
                Alignment = new Alignment { Vertical = VerticalAlignmentValues.Top, WrapText = true }
            },
            new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true },
            new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true, FontId = 1, ApplyFont = true })
        { Count = 5 },
        new CellStyles(new CellStyle { Name = "Обычный", FormatId = 0, BuiltinId = 0 }) { Count = 1 });

    private readonly record struct RowCounts(int Plans, int UnknownKztPlans);
}
