using System.Globalization;
using System.Xml;
using Bms.Application.Treasury;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Bms.Api.Exports;

/// <summary>A numeric Excel cell cannot preserve this monetary value.</summary>
internal sealed class PaymentCalendarExcelPrecisionException : Exception;

/// <summary>
/// Writes the complete reconciled calendar, with separate parameters and
/// totals outside the filtered rows. No technical identifier or formula is
/// emitted. Numeric values keep their stored precision even when displayed
/// as whole amounts, as requested for management spreadsheets.
/// </summary>
internal static class PaymentCalendarExcelExporter
{
    internal const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const uint HeaderStyle = 1;
    private const uint TextStyle = 2;
    private const uint MoneyStyle = 3;
    private const uint TotalStyle = 4;
    private const uint RoundingStyle = 5;

    private static readonly string[] Headers =
    [
        "Плановая дата", "Контрагент", "Назначение", "Статус", "Валюта",
        "Сумма заявки", "Факт погашения", "Банк", "Зачёт", "Остаток", "Оплата"
    ];

    internal static string FileName(DateTimeOffset generatedAtUtc) =>
        $"payment-calendar-{generatedAtUtc.UtcDateTime:yyyyMMdd-HHmmss}Z.xlsx";

    /// <summary>
    /// Builds before the HTTP response starts. The caller owns the returned
    /// stream; any failure or cancellation disposes the incomplete package.
    /// </summary>
    internal static MemoryStream CreateStream(
        PaymentCalendarExportData export, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(export.Query);
        ArgumentNullException.ThrowIfNull(export.Rows);
        ArgumentNullException.ThrowIfNull(export.Totals);
        export.Query.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (export.Rows.Count > PaymentCalendarExportPolicy.MaximumRows)
        {
            throw new PaymentCalendarExportTooLargeException();
        }
        ValidateOrganizationSections(export, cancellationToken);

        var stream = new MemoryStream();
        try
        {
            using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
            {
                var workbook = document.AddWorkbookPart();
                workbook.Workbook = new Workbook();
                workbook.AddNewPart<WorkbookStylesPart>().Stylesheet = Styles();
                var calendar = workbook.AddNewPart<WorksheetPart>();
                WriteCalendar(calendar, export, cancellationToken);
                var parameters = workbook.AddNewPart<WorksheetPart>();
                WriteParameters(parameters, export, cancellationToken);
                var sheets = workbook.Workbook.AppendChild(new Sheets());
                sheets.Append(new Sheet
                {
                    Id = workbook.GetIdOfPart(calendar),
                    SheetId = 1,
                    Name = "Платёжный календарь"
                });
                sheets.Append(new Sheet
                {
                    Id = workbook.GetIdOfPart(parameters),
                    SheetId = 2,
                    Name = "Параметры"
                });
                if (export.Query.Grouping == PaymentCalendarGroupingCodes.Organization)
                {
                    var organizations = workbook.AddNewPart<WorksheetPart>();
                    WriteOrganizations(organizations, export, cancellationToken);
                    sheets.Append(new Sheet
                    {
                        Id = workbook.GetIdOfPart(organizations),
                        SheetId = 3,
                        Name = "Организации"
                    });
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

    private static void WriteCalendar(
        WorksheetPart part, PaymentCalendarExportData export, CancellationToken cancellationToken)
    {
        var grouped = export.Query.Grouping == PaymentCalendarGroupingCodes.Organization;
        var rounding = export.Totals.ApprovedRoundingAmount > 0;
        var offset = grouped ? 3 : 0;
        using var writer = OpenXmlWriter.Create(part);
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
        if (grouped) columns.Append(Column(1, 12), Column(2, 24), Column(3, 42));
        double[] widths = [17, 32, 48, 24, 12, 21, 25, 21, 21, 21, 24];
        if(rounding)widths=[..widths,21];
        for (var column = 0; column < widths.Length; column++)
        {
            columns.Append(Column((uint)(column + offset + 1), widths[column]));
        }
        writer.WriteElement(columns);
        writer.WriteStartElement(new SheetData());
        string[] headers = grouped ? ["Группа", "Код организации", "Организация", .. Headers] : Headers;
        if(rounding)headers=[..headers,"Округление (не платёж)"];
        writer.WriteElement(new Row(headers.Select((text, index) => TextCell(1, index + 1, text, HeaderStyle)))
        {
            RowIndex = 1,
            Height = 32,
            CustomHeight = true
        });
        uint index = 2;
        var organizationIndex = 0;
        foreach (var item in export.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // ISO calendar dates preserve the full DateOnly range and sort
            // chronologically as literal text, without an Excel epoch conversion.
            var row = new Row { RowIndex = index };
            if (grouped)
            {
                var section = export.OrganizationGroups[organizationIndex];
                if (index - 2 >= section.FirstRowIndex + section.Total)
                {
                    section = export.OrganizationGroups[++organizationIndex];
                }
                row.Append(NumberCell(index, 1, organizationIndex + 1),
                    TextCell(index, 2, section.OrganizationCode ?? string.Empty),
                    TextCell(index, 3, OrganizationLabel(section)));
            }
            row.Append(
                TextCell(index, offset + 1, item.PlannedPaymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                TextCell(index, offset + 2, item.CounterpartyName ?? string.Empty),
                TextCell(index, offset + 3, item.Purpose ?? string.Empty),
                TextCell(index, offset + 4, StatusLabel(item.Status)),
                TextCell(index, offset + 5, item.CurrencyCode),
                NumberCell(index, offset + 6, item.ObligationAmount),
                NumberCell(index, offset + 7, item.PaidAmount),
                NumberCell(index, offset + 8, item.BankPaidAmount),
                NumberCell(index, offset + 9, item.AppliedOverpaymentAmount),
                NumberCell(index, offset + 10, item.RemainingAmount),
                TextCell(index, offset + 11, ProgressLabel(item.Progress)));
            if(rounding)row.Append(NumberCell(index,offset+12,item.ApprovedRoundingAmount,RoundingStyle));
            writer.WriteElement(row);
            index++;
        }

        var lastDataRow = index - 1;
        index++;
        writer.WriteElement(new Row(
            TextCell(index, grouped ? 2 : 1, "Итого за период"), TextCell(index, offset + 5, "KZT"),
            NumberCell(index, offset + 6, export.Totals.ObligationAmount, TotalStyle),
            NumberCell(index, offset + 7, export.Totals.PaidAmount, TotalStyle),
            NumberCell(index, offset + 8, export.Totals.BankPaidAmount, TotalStyle),
            NumberCell(index, offset + 9, export.Totals.AppliedOverpaymentAmount, TotalStyle),
            NumberCell(index, offset + 10, export.Totals.RemainingAmount, TotalStyle))
        { RowIndex = index });
        index++;
        if(rounding)
        {
            writer.WriteElement(new Row(TextCell(index,grouped?2:1,"Согласованное округление, KZT (не банк)"),
                NumberCell(index,offset+12,export.Totals.ApprovedRoundingAmount,RoundingStyle)){RowIndex=index});
            index++;
        }
        writer.WriteElement(new Row(
            TextCell(index, grouped ? 2 : 1, "Переплата, KZT"),
            NumberCell(index, offset + 6, export.Totals.OverpaidAmount, TotalStyle))
        { RowIndex = index });
        index++;
        writer.WriteElement(new Row(
            TextCell(index, grouped ? 2 : 1, "Заявок в выборке"), NumberCell(index, offset + 6, export.Rows.Count))
        { RowIndex = index });
        cancellationToken.ThrowIfCancellationRequested();
        writer.WriteEndElement();
        writer.WriteElement(new AutoFilter { Reference = $"A1:{(rounding ? (grouped?"O":"L") : (grouped ? "N" : "K"))}{lastDataRow}" });
        writer.WriteEndElement();
    }

    private static void WriteOrganizations(
        WorksheetPart part, PaymentCalendarExportData export, CancellationToken cancellationToken)
    {
        using var writer = OpenXmlWriter.Create(part);
        writer.WriteStartElement(new Worksheet());
        writer.WriteElement(new SheetViews(new SheetView(new Pane
        {
            VerticalSplit = 1,
            TopLeftCell = "A2",
            ActivePane = PaneValues.BottomLeft,
            State = PaneStateValues.Frozen
        })
        { WorkbookViewId = 0 }));
        writer.WriteElement(new Columns(Column(1, 12), Column(2, 24), Column(3, 42), Column(4, 14),
            Column(5, 24), Column(6, 27), Column(7, 24), Column(8, 24), Column(9, 24), Column(10, 24)));
        writer.WriteStartElement(new SheetData());
        string[] headers = ["Группа", "Код организации", "Организация", "Заявок", "Сумма заявок, KZT",
            "Факт погашения, KZT", "Банк, KZT", "Зачёт, KZT", "Остаток, KZT", "Переплата, KZT"];
        writer.WriteElement(new Row(headers.Select((text, column) => TextCell(1, column + 1, text, HeaderStyle)))
        { RowIndex = 1, Height = 32, CustomHeight = true });
        uint index = 2;
        foreach (var group in export.OrganizationGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteElement(new Row(NumberCell(index, 1, index - 1),
                TextCell(index, 2, group.OrganizationCode ?? string.Empty), TextCell(index, 3, OrganizationLabel(group)),
                NumberCell(index, 4, group.Total), NumberCell(index, 5, group.Totals.ObligationAmount),
                NumberCell(index, 6, group.Totals.PaidAmount),
                NumberCell(index, 7, group.Totals.BankPaidAmount), NumberCell(index, 8, group.Totals.AppliedOverpaymentAmount),
                NumberCell(index, 9, group.Totals.RemainingAmount), NumberCell(index, 10, group.Totals.OverpaidAmount))
            { RowIndex = index });
            index++;
        }
        var lastDataRow = index - 1;
        index++;
        writer.WriteElement(new Row(TextCell(index, 3, "Итого за период"),
            NumberCell(index, 4, export.Rows.Count, TotalStyle),
            NumberCell(index, 5, export.Totals.ObligationAmount, TotalStyle),
            NumberCell(index, 6, export.Totals.PaidAmount, TotalStyle),
            NumberCell(index, 7, export.Totals.BankPaidAmount, TotalStyle),
            NumberCell(index, 8, export.Totals.AppliedOverpaymentAmount, TotalStyle),
            NumberCell(index, 9, export.Totals.RemainingAmount, TotalStyle),
            NumberCell(index, 10, export.Totals.OverpaidAmount, TotalStyle))
        { RowIndex = index });
        writer.WriteEndElement();
        writer.WriteElement(new AutoFilter { Reference = $"A1:J{lastDataRow}" });
        writer.WriteEndElement();
    }

    private static string OrganizationLabel(PaymentCalendarExportOrganizationGroup group) =>
        group.OrganizationName ?? (group.HasSourceOrganization ? "Не найдена в справочнике" : "Не указана");

    private static void ValidateOrganizationSections(PaymentCalendarExportData export, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(export.OrganizationGroups);
        if (export.Query.Grouping == PaymentCalendarGroupingCodes.Date)
        {
            if (export.OrganizationGroups.Count != 0) throw new PaymentCalendarUnavailableException();
            return;
        }

        var nextRow = 0;
        decimal obligation = 0, paid = 0, remaining = 0, overpaid = 0, applied = 0, rounding = 0;
        foreach (var group in export.OrganizationGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.FirstRowIndex != nextRow || group.Total <= 0 || group.Total > export.Rows.Count - nextRow
                || (group.OrganizationCode is null) != (group.OrganizationName is null)
                || (!group.HasSourceOrganization && group.OrganizationCode is not null))
            {
                throw new PaymentCalendarUnavailableException();
            }

            decimal groupObligation = 0, groupPaid = 0, groupRemaining = 0, groupApplied = 0, groupRounding = 0;
            for (var index = nextRow; index < nextRow + group.Total; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = export.Rows[index];
                if (row.OrganizationCode != group.OrganizationCode || row.OrganizationName != group.OrganizationName
                    || row.AppliedOverpaymentAmount < 0 || row.BankPaidAmount < 0 || row.ApprovedRoundingAmount < 0)
                {
                    throw new PaymentCalendarUnavailableException();
                }
                groupObligation += row.ObligationAmount;
                groupPaid += row.PaidAmount;
                groupRemaining += row.RemainingAmount;
                groupApplied += row.AppliedOverpaymentAmount;
                groupRounding += row.ApprovedRoundingAmount;
            }
            if (group.Totals.ObligationAmount != groupObligation || group.Totals.PaidAmount != groupPaid
                || group.Totals.RemainingAmount != groupRemaining || group.Totals.OverpaidAmount < 0
                || group.Totals.AppliedOverpaymentAmount != groupApplied || group.Totals.ApprovedRoundingAmount!=groupRounding)
            {
                throw new PaymentCalendarUnavailableException();
            }
            nextRow += group.Total;
            obligation += groupObligation;
            paid += groupPaid;
            remaining += groupRemaining;
            overpaid += group.Totals.OverpaidAmount;
            applied += groupApplied;
            rounding += groupRounding;
        }
        if (nextRow != export.Rows.Count || new PaymentCalendarTotalsDto(obligation, paid, remaining, overpaid)
        { AppliedOverpaymentAmount = applied, ApprovedRoundingAmount=rounding } != export.Totals)
        {
            throw new PaymentCalendarUnavailableException();
        }
    }

    private static void WriteParameters(
        WorksheetPart part, PaymentCalendarExportData export, CancellationToken cancellationToken)
    {
        using var writer = OpenXmlWriter.Create(part);
        writer.WriteStartElement(new Worksheet());
        writer.WriteElement(new Columns(Column(1, 30), Column(2, 85)));
        writer.WriteStartElement(new SheetData());
        var parameters = new List<(string Label, string Value)>
        {
            ("Параметр", "Значение"),
            ("Период с (включительно)", export.Query.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("Период по (включительно)", export.Query.Through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("Статус", export.Query.StatusCode is null ? "Все" : StatusLabel(export.Query.StatusCode)),
            ("Валюта календаря", "KZT"),
            ("Охват", "Вся доступная выборка по фильтрам; без пагинации"),
            ("Сформирован (UTC)", export.GeneratedAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            ("Точность сумм", "Суммы показаны целыми; исходная точность сохранена в числовых ячейках.")
        };
        parameters.Add(("Группировка", export.Query.Grouping == PaymentCalendarGroupingCodes.Organization ? "По организациям" : "По датам"));
        if (export.Query.Grouping == PaymentCalendarGroupingCodes.Organization)
        {
            parameters.Add(("Номер группы", "Порядковый номер в этой выгрузке, не код организации. Связывает строки и итоги, в том числе при отсутствии справочных данных."));
        }
        uint index = 1;
        foreach (var (label, value) in parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var style = index == 1 ? HeaderStyle : TextStyle;
            writer.WriteElement(new Row(TextCell(index, 1, label, style), TextCell(index, 2, value, style))
            {
                RowIndex = index,
                Height = index == 8 || index == 10 ? 32 : 22,
                CustomHeight = true
            });
            index++;
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static Cell TextCell(uint row, int column, string text, uint style = TextStyle)
    {
        // Refuse unrepresentable text rather than quietly changing a stored
        // business label. Inline strings and quote-prefix prohibit evaluation.
        XmlConvert.VerifyXmlChars(text);
        if (text.Length > 32_767) throw new PaymentCalendarUnavailableException();
        return new Cell(new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve }))
        {
            CellReference = Reference(row, column),
            DataType = CellValues.InlineString,
            StyleIndex = style
        };
    }

    private static Cell NumberCell(uint row, int column, decimal value, uint style = MoneyStyle)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        // Exact decimal XML alone is insufficient: Excel retains 15 significant
        // digits. Trailing integer zeros need no additional precision. Refuse
        // the entire workbook rather than lose money digits or silently change
        // a numeric column to text. This applies to totals as well as rows.
        var significantDigits = text.Replace(".", string.Empty, StringComparison.Ordinal)
            .TrimStart('-', '0').TrimEnd('0').Length;
        if (significantDigits > 15) throw new PaymentCalendarExcelPrecisionException();
        return new Cell(new CellValue(text))
        {
            CellReference = Reference(row, column),
            DataType = CellValues.Number,
            StyleIndex = style
        };
    }

    private static string Reference(uint row, int column) =>
        $"{(char)('A' + column - 1)}{row.ToString(CultureInfo.InvariantCulture)}";

    private static Column Column(uint index, double width) =>
        new() { Min = index, Max = index, Width = width, CustomWidth = true };

    internal static string StatusLabel(string status) => status switch
    {
        "TO_PAY" => "В оплату",
        "PARTIALLY_PAID" => "Оплачена частично",
        "PAID" => "Оплачена",
        "CLOSED" => "Закрыта",
        _ => throw new PaymentCalendarUnavailableException()
    };

    private static string ProgressLabel(string progress) => progress switch
    {
        "unpaid" => "Не оплачена",
        "partiallyPaid" => "Оплачена частично",
        "paid" => "Оплачена",
        _ => throw new PaymentCalendarUnavailableException()
    };

    private static Stylesheet Styles() => new(
        new NumberingFormats(new NumberingFormat { NumberFormatId = 164, FormatCode = "#,##0" },
            new NumberingFormat { NumberFormatId=165,FormatCode="#,##0.00" }) { Count = 2 },
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
        new CellFormats(
            new CellFormat(),
            new CellFormat
            {
                FontId = 1,
                FillId = 2,
                ApplyFont = true,
                ApplyFill = true,
                ApplyAlignment = true,
                Alignment = new Alignment
                {
                    Horizontal = HorizontalAlignmentValues.Center,
                    Vertical = VerticalAlignmentValues.Center,
                    WrapText = true
                }
            },
            new CellFormat
            {
                QuotePrefix = true,
                ApplyAlignment = true,
                Alignment = new Alignment { Vertical = VerticalAlignmentValues.Top, WrapText = true }
            },
            new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true },
            new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true, FontId = 1, ApplyFont = true },
            new CellFormat { NumberFormatId=165,ApplyNumberFormat=true })
        { Count = 6 },
        new CellStyles(new CellStyle { Name = "Обычный", FormatId = 0, BuiltinId = 0 }) { Count = 1 });
}
