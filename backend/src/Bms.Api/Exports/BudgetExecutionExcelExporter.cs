using System.Globalization;
using System.Text;
using Bms.Application.Abstractions;
using Bms.Application.Budgeting;
using Bms.Application.Common;
using Bms.Domain.ExpenseRequests;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Bms.Api.Exports;

/// <summary>
/// Writes the budget execution area as a workbook, using the same OpenXML
/// mechanism the expense request export already uses.
///
/// The sheet publishes the effective approved limit. If no approved version
/// covers a key, the business rule is an explicit zero, so rows and totals stay
/// numeric and additive. The totals under the rows describe the whole exported
/// area rather than only the current page.
/// </summary>
internal static class BudgetExecutionExcelExporter
{
    internal const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    internal const string MissingReferenceText = "Нет в справочнике";

    private const uint DefaultStyle = 0U;
    private const uint HeaderStyle = 1U;
    private const uint MonthStyle = 2U;
    private const uint MoneyStyle = 3U;
    private const uint GuardedTextStyle = 4U;
    private const uint LabelStyle = 5U;
    private const uint TotalMoneyStyle = 6U;
    private const uint CountStyle = 7U;
    private const uint DayStyle = 8U;

    private static readonly string[] Headers =
    [
        "Месяц бюджета",
        "Код ЦФО",
        "ЦФО",
        "Код статьи ДДС",
        "Статья ДДС",
        "Лимит, KZT",
        "Зарезервировано, KZT",
        "Оплачено, KZT",
        "Доступно, KZT",
        "Лимит задан"
    ];

    /// <summary>
    /// The composition sheet. There is no request number in this system - the
    /// request form itself shows the number as one still to be assigned - so a
    /// reserving request is named by its purpose, its planned payment date and
    /// its status. No identifier appears on this sheet.
    /// </summary>
    private static readonly string[] ReservationHeaders =
    [
        "Месяц бюджета",
        "Код ЦФО",
        "ЦФО",
        "Код статьи ДДС",
        "Статья ДДС",
        "Назначение платежа",
        "Статус",
        "Плановая оплата",
        "Строк заявки",
        "Зарезервировано, KZT"
    ];

    internal static byte[] Create(BudgetExecutionExportData export)
    {
        using var stream = CreateStream(export, CancellationToken.None);
        return stream.ToArray();
    }

    /// <summary>
    /// Builds the package with a forward-only worksheet writer. The returned
    /// stream owns the compressed package and is positioned at its beginning;
    /// the HTTP result disposes it after the response is sent. This avoids both
    /// an in-memory worksheet DOM and the second full package copy made by
    /// <see cref="MemoryStream.ToArray"/>.
    /// </summary>
    internal static MemoryStream CreateStream(
        BudgetExecutionExportData export,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(export.Rows);
        ArgumentNullException.ThrowIfNull(export.Totals);
        ArgumentNullException.ThrowIfNull(export.Reservations);
        if (export.Rows.Count > BudgetExecutionExportPolicy.MaximumRows)
        {
            throw new BudgetExecutionExportTooLargeException(
                export.Rows.Count,
                BudgetExecutionExportPolicy.MaximumRows);
        }

        if (export.Reservations.Count > BudgetExecutionExportPolicy.MaximumRows)
        {
            throw new BudgetExecutionExportTooLargeException(
                export.Reservations.Count,
                BudgetExecutionExportPolicy.MaximumRows);
        }

        var stream = new MemoryStream();
        try
        {
            using (var document = SpreadsheetDocument.Create(
                       stream,
                       SpreadsheetDocumentType.Workbook,
                       true))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = CreateStylesheet();

                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                WriteWorksheet(worksheetPart, export, cancellationToken);

                var reservationPart = workbookPart.AddNewPart<WorksheetPart>();
                WriteReservationWorksheet(reservationPart, export.Reservations, cancellationToken, export.Totals.ReservedAmountKzt);

                var sheets = workbookPart.Workbook.AppendChild(new Sheets());
                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1U,
                    Name = "Исполнение бюджета"
                });
                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(reservationPart),
                    SheetId = 2U,
                    Name = "Состав резерва"
                });

                workbookPart.Workbook.Save();
            }

            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static void WriteWorksheet(
        WorksheetPart worksheetPart,
        BudgetExecutionExportData export,
        CancellationToken cancellationToken)
    {
        using var writer = OpenXmlWriter.Create(worksheetPart);
        writer.WriteStartElement(new Worksheet());
        writer.WriteElement(new SheetViews(
            new SheetView(
                new Pane
                {
                    VerticalSplit = 1D,
                    TopLeftCell = "A2",
                    ActivePane = PaneValues.BottomLeft,
                    State = PaneStateValues.Frozen
                })
            { WorkbookViewId = 0U }));
        writer.WriteElement(new SheetFormatProperties { DefaultRowHeight = 18D });
        writer.WriteElement(CreateColumns());
        writer.WriteStartElement(new SheetData());
        writer.WriteElement(CreateHeaderRow());

        // W593. A caller that has already given up is answered before the row
        // sequence is touched at all: the first MoveNext may be a database
        // read, and there is no reason to make it for a workbook nobody wants.
        cancellationToken.ThrowIfCancellationRequested();

        uint rowIndex = 2;
        foreach (var row in export.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(row);
            writer.WriteElement(CreateDataRow(rowIndex++, row));
        }

        var lastDataRowIndex = Math.Max(1U, rowIndex - 1U);
        // One blank row keeps the totals outside the filtered range, so
        // filtering or sorting the rows can never move a total in among them.
        foreach (var totalsRow in CreateTotalsRows(lastDataRowIndex + 2U, export.Totals))
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteElement(totalsRow);
        }
        if (export.FactSource is { } fact)
        {
            var sourceIndex = lastDataRowIndex + 7U;
            var source = new Row { RowIndex = sourceIndex };
            source.Append(TextCell(sourceIndex, 1, $"Оплачено — выплаты факта ДДС за {fact.SourceYear} по {fact.CutoffExclusiveLocal.AddDays(-1):dd.MM.yyyy} включительно, без внутренних перемещений и депозитов; зарезервировано — расходные заявки", LabelStyle));
            writer.WriteElement(source);
            var captured = new Row { RowIndex = sourceIndex + 1U };
            captured.Append(TextCell(sourceIndex + 1U, 1, $"Снимок {fact.SnapshotId:D}; источник {fact.SourceGeneration}; SHA256 {fact.GrossHash}; получен {fact.CapturedAtUtc:O}", DefaultStyle));
            writer.WriteElement(captured);
        }

        writer.WriteEndElement();
        writer.WriteElement(new AutoFilter { Reference = $"A1:J{lastDataRowIndex}" });
        writer.WriteEndElement();
    }

    internal static string FileName(DateTimeOffset generatedAtUtc) =>
        $"budget-execution-{generatedAtUtc.UtcDateTime:yyyyMMdd-HHmmss}Z.xlsx";

    /// <summary>
    /// A spreadsheet cell whose text begins with one of these is treated by
    /// spreadsheet software as something to evaluate rather than to show. The
    /// cell is marked as literal text instead, which leaves the value itself
    /// untouched: nothing is trimmed, prefixed or rewritten on the way out.
    /// </summary>
    internal static bool NeedsFormulaGuard(string value) =>
        !string.IsNullOrEmpty(value)
        && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n';

    /// <summary>
    /// One row per reserving request, under the position it reserves against.
    /// Only the current reserve is described: the paid part of a position is
    /// never attributed to individual requests, so no such column exists here.
    /// </summary>
    private static void WriteReservationWorksheet(
        WorksheetPart worksheetPart,
        IReadOnlyList<BudgetReservationExportRow> reservations,
        CancellationToken cancellationToken,
        decimal completeReservedAmountKzt)
    {
        ArgumentNullException.ThrowIfNull(reservations);

        using var writer = OpenXmlWriter.Create(worksheetPart);
        writer.WriteStartElement(new Worksheet());
        writer.WriteElement(new SheetViews(
            new SheetView(
                new Pane
                {
                    VerticalSplit = 1D,
                    TopLeftCell = "A2",
                    ActivePane = PaneValues.BottomLeft,
                    State = PaneStateValues.Frozen
                })
            { WorkbookViewId = 0U }));
        writer.WriteElement(new SheetFormatProperties { DefaultRowHeight = 18D });
        writer.WriteElement(CreateReservationColumns());
        writer.WriteStartElement(new SheetData());
        var header = new Row { RowIndex = 1U, Height = 26D, CustomHeight = true };
        for (var index = 0; index < ReservationHeaders.Length; index++)
        {
            header.Append(TextCell(1U, index + 1, ReservationHeaders[index], HeaderStyle));
        }

        writer.WriteElement(header);

        // W593. The same answer at the seam: a caller that gave up while the
        // first sheet was being written does not make the second sheet's read.
        cancellationToken.ThrowIfCancellationRequested();

        uint rowIndex = 2;
        var lineTotal = 0;
        var reservedTotal = 0m;
        foreach (var reservation in reservations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(reservation);
            var row = new Row { RowIndex = rowIndex };
            row.Append(MonthCell(rowIndex, 1, reservation.BudgetMonth));
            row.Append(DataTextCell(rowIndex, 2, Readable(reservation.CostCenterCode)));
            row.Append(DataTextCell(rowIndex, 3, Readable(reservation.CostCenterName)));
            row.Append(DataTextCell(rowIndex, 4, Readable(reservation.CashFlowItemCode)));
            row.Append(DataTextCell(rowIndex, 5, Readable(reservation.CashFlowItemName)));
            row.Append(DataTextCell(rowIndex, 6, Purpose(reservation.Purpose)));
            row.Append(DataTextCell(rowIndex, 7, StatusLabel(reservation.Status)));
            row.Append(DayCell(rowIndex, 8, reservation.PlannedPaymentDate));
            row.Append(CountCell(rowIndex, 9, reservation.LineCount));
            row.Append(NumberCell(rowIndex, 10, reservation.ReservedAmountKzt, MoneyStyle));
            writer.WriteElement(row);
            lineTotal = checked(lineTotal + reservation.LineCount);
            reservedTotal += reservation.ReservedAmountKzt;
            rowIndex++;
        }

        var lastDataRowIndex = Math.Max(1U, rowIndex - 1U);
        var totalsIndex = lastDataRowIndex + 2U;
        var totals = new Row { RowIndex = totalsIndex };
        totals.Append(TextCell(totalsIndex, 1, "По доступной детализации", LabelStyle));
        totals.Append(CountCell(totalsIndex, 9, lineTotal));
        totals.Append(NumberCell(totalsIndex, 10, reservedTotal, TotalMoneyStyle));
        cancellationToken.ThrowIfCancellationRequested();
        writer.WriteElement(totals);

        var requests = new Row { RowIndex = totalsIndex + 1U };
        requests.Append(TextCell(totalsIndex + 1U, 1, "Заявок в выборке", LabelStyle));
        requests.Append(CountCell(totalsIndex + 1U, 2, reservations.Count));
        writer.WriteElement(requests);
        if (completeReservedAmountKzt != reservedTotal)
        {
            var complete = new Row { RowIndex = totalsIndex + 2U };
            complete.Append(TextCell(totalsIndex + 2U, 1, "Полный резерв выбранной области", LabelStyle));
            complete.Append(NumberCell(totalsIndex + 2U, 10, completeReservedAmountKzt, TotalMoneyStyle));
            writer.WriteElement(complete);
        }
        writer.WriteEndElement();
        writer.WriteElement(new AutoFilter { Reference = $"A1:J{lastDataRowIndex}" });
        writer.WriteEndElement();
    }

    private static string Purpose(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Назначение не указано" : value;

    private static string StatusLabel(string value) =>
        ExpenseRequestStatusCode.TryParse(value, out var status)
            ? status.ToDisplayName()
            : throw new BudgetProjectionDriftException(
                "A reserving request carries a status outside the published set.");

    private static Cell CountCell(uint row, int column, int value) => new()
    {
        CellReference = CellReference(row, column),
        StyleIndex = CountStyle,
        CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
    };

    private static Cell DayCell(uint row, int column, DateOnly value) => new()
    {
        CellReference = CellReference(row, column),
        StyleIndex = DayStyle,
        CellValue = new CellValue(
            value.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture))
    };

    private static Columns CreateReservationColumns() => new(
        new Column { Min = 1U, Max = 1U, Width = 18D, CustomWidth = true },
        new Column { Min = 2U, Max = 2U, Width = 14D, CustomWidth = true },
        new Column { Min = 3U, Max = 3U, Width = 30D, CustomWidth = true },
        new Column { Min = 4U, Max = 4U, Width = 18D, CustomWidth = true },
        new Column { Min = 5U, Max = 5U, Width = 34D, CustomWidth = true },
        new Column { Min = 6U, Max = 6U, Width = 44D, CustomWidth = true },
        new Column { Min = 7U, Max = 7U, Width = 20D, CustomWidth = true },
        new Column { Min = 8U, Max = 8U, Width = 18D, CustomWidth = true },
        new Column { Min = 9U, Max = 9U, Width = 14D, CustomWidth = true },
        new Column { Min = 10U, Max = 10U, Width = 22D, CustomWidth = true });

    private static Row CreateHeaderRow()
    {
        var row = new Row
        {
            RowIndex = 1U,
            Height = 26D,
            CustomHeight = true
        };
        for (var index = 0; index < Headers.Length; index++)
        {
            row.Append(TextCell(1U, index + 1, Headers[index], HeaderStyle));
        }

        return row;
    }

    private static Row CreateDataRow(uint rowIndex, BudgetExecutionExportRow source)
    {
        var row = new Row { RowIndex = rowIndex };
        row.Append(MonthCell(rowIndex, 1, source.BudgetMonth));
        row.Append(DataTextCell(rowIndex, 2, source.IsCostCenterUnassigned ? "" : Readable(source.CostCenterCode)));
        if (source.PlanCostCenters is { Count: > 0 } centers)
        {
            row.CustomHeight = true;
            row.Height = 32D;
            var cell = DataTextCell(rowIndex, 3, source.IsCostCenterUnassigned ? "" : Readable(source.CostCenterName));
            cell.StyleIndex = 9U; // Wrapped literal text, retaining formula injection protection.
            cell.InlineString = new InlineString(
                new Run(new Text(XmlSafeText(source.IsCostCenterUnassigned ? "" : Readable(source.CostCenterName)))),
                new Run(new RunProperties(new FontSize { Val = 10D }),
                    new Text(XmlSafeText("\nЦФО плана: " + string.Join(", ", centers.Select(c => c.Code ?? c.Name ?? MissingReferenceText))))
                    { Space = SpaceProcessingModeValues.Preserve }));
            row.Append(cell);
        }
        else row.Append(DataTextCell(rowIndex, 3, source.IsCostCenterUnassigned ? "" : Readable(source.CostCenterName)));
        row.Append(DataTextCell(rowIndex, 4, Readable(source.CashFlowItemCode)));
        row.Append(DataTextCell(rowIndex, 5, Readable(source.CashFlowItemName)));
        var effectiveLimit = source.LimitAmountKzt ?? 0m;
        var effectiveAvailable = source.AvailableAmountKzt
            ?? effectiveLimit - source.ReservedAmountKzt - source.PaidAmountKzt;
        row.Append(NumberCell(rowIndex, 6, effectiveLimit, MoneyStyle));
        row.Append(NumberCell(rowIndex, 7, source.ReservedAmountKzt, MoneyStyle));
        row.Append(NumberCell(rowIndex, 8, source.PaidAmountKzt, MoneyStyle));
        row.Append(NumberCell(rowIndex, 9, effectiveAvailable, MoneyStyle));
        row.Append(DataTextCell(
            rowIndex,
            10,
            "Да"));
        return row;
    }

    private static IEnumerable<Row> CreateTotalsRows(
        uint firstRowIndex,
        BudgetPositionTotalsDto totals)
    {
        var effectiveLimit = totals.LimitAmountKzt;
        var effectiveAvailable = totals.AvailableAmountKzt;
        var totalRow = new Row { RowIndex = firstRowIndex };
        totalRow.Append(TextCell(firstRowIndex, 1, "Итого по выбранной области", LabelStyle));
        totalRow.Append(NumberCell(firstRowIndex, 6, effectiveLimit, TotalMoneyStyle));
        totalRow.Append(NumberCell(firstRowIndex, 7, totals.ReservedAmountKzt, TotalMoneyStyle));
        totalRow.Append(NumberCell(firstRowIndex, 8, totals.PaidAmountKzt, TotalMoneyStyle));
        totalRow.Append(NumberCell(firstRowIndex, 9, effectiveAvailable, TotalMoneyStyle));
        yield return totalRow;

        yield return CountRow(firstRowIndex + 1U, "Позиций в выборке", totals.TotalPositions);
        yield return CountRow(
            firstRowIndex + 2U,
            "Из них с эффективным лимитом",
            totals.TotalPositions);
        yield return CountRow(
            firstRowIndex + 3U,
            "Из них без эффективного лимита",
            0);
    }

    private static Row CountRow(uint rowIndex, string label, int value)
    {
        var row = new Row { RowIndex = rowIndex };
        row.Append(TextCell(rowIndex, 1, label, LabelStyle));
        row.Append(new Cell
        {
            CellReference = CellReference(rowIndex, 2),
            StyleIndex = CountStyle,
            CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
        });
        return row;
    }

    private static string Readable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? MissingReferenceText : value;

    private static Cell DataTextCell(uint row, int column, string value) =>
        TextCell(row, column, value, NeedsFormulaGuard(value) ? GuardedTextStyle : DefaultStyle);

    private static Cell TextCell(uint row, int column, string value, uint styleIndex) => new()
    {
        CellReference = CellReference(row, column),
        DataType = CellValues.InlineString,
        StyleIndex = styleIndex,
        InlineString = new InlineString(
            new Text(XmlSafeText(value)) { Space = SpaceProcessingModeValues.Preserve })
    };

    private static Cell MonthCell(uint row, int column, DateOnly value) => new()
    {
        CellReference = CellReference(row, column),
        StyleIndex = MonthStyle,
        CellValue = new CellValue(
            value.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture))
    };

    private static Cell MoneyOrBlankCell(uint row, int column, decimal? value, uint styleIndex) =>
        value.HasValue
            ? NumberCell(row, column, value.Value, styleIndex)
            : BlankCell(row, column, styleIndex);

    private static Cell NumberCell(uint row, int column, decimal value, uint styleIndex) => new()
    {
        CellReference = CellReference(row, column),
        StyleIndex = styleIndex,
        CellValue = new CellValue(ExcelDecimalPrecision.Format(value))
    };

    private static Cell BlankCell(uint row, int column, uint styleIndex) => new()
    {
        CellReference = CellReference(row, column),
        StyleIndex = styleIndex
    };

    private static string CellReference(uint row, int column) =>
        $"{(char)('A' + column - 1)}{row.ToString(CultureInfo.InvariantCulture)}";

    private static string XmlSafeText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        // W592. Both sheets are mostly reference names, purposes and codes that
        // XML can already carry, and for that text the walk below returns the
        // string it was given. Finding that out costs one scan and no
        // allocation; only text that has to be rewritten pays for a builder.
        var first = IndexOfUnrepresentable(value);
        if (first < 0)
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        result.Append(value, 0, first);
        foreach (var rune in value.AsSpan(first).EnumerateRunes())
        {
            var scalar = rune.Value;
            result.Append(
                scalar is 0x9 or 0xA or 0xD ||
                scalar is >= 0x20 and <= 0xD7FF ||
                scalar is >= 0xE000 and <= 0xFFFD ||
                scalar is >= 0x10000 and <= 0x10FFFF
                    ? rune.ToString()
                    : "\uFFFD");
        }

        return result.ToString();
    }

    /// <summary>
    /// W592. The first index from which <see cref="XmlSafeText"/> would not
    /// reproduce its input, or -1 when it would reproduce all of it.
    ///
    /// The rune walk replaces exactly three kinds of scalar: a control
    /// character other than tab, newline and carriage return; an unpaired
    /// surrogate, which the walk itself reports as U+FFFD; and U+FFFE or
    /// U+FFFF. Everything else - including every astral pair - it copies
    /// through unchanged. A valid surrogate pair is stepped over whole, so its
    /// low half is never mistaken for an unpaired one.
    /// </summary>
    private static int IndexOfUnrepresentable(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (current < 0x20)
            {
                if (current is not ('\t' or '\n' or '\r'))
                {
                    return index;
                }

                continue;
            }

            if (char.IsHighSurrogate(current))
            {
                if (index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                {
                    index++;
                    continue;
                }

                return index;
            }

            if (char.IsLowSurrogate(current) || current is '\uFFFE' or '\uFFFF')
            {
                return index;
            }
        }

        return -1;
    }

    private static Columns CreateColumns() => new(
        new Column { Min = 1U, Max = 1U, Width = 30D, CustomWidth = true },
        new Column { Min = 2U, Max = 2U, Width = 14D, CustomWidth = true },
        new Column { Min = 3U, Max = 3U, Width = 34D, CustomWidth = true },
        new Column { Min = 4U, Max = 4U, Width = 18D, CustomWidth = true },
        new Column { Min = 5U, Max = 5U, Width = 44D, CustomWidth = true },
        new Column { Min = 6U, Max = 6U, Width = 18D, CustomWidth = true },
        new Column { Min = 7U, Max = 7U, Width = 22D, CustomWidth = true },
        new Column { Min = 8U, Max = 8U, Width = 18D, CustomWidth = true },
        new Column { Min = 9U, Max = 9U, Width = 18D, CustomWidth = true },
        new Column { Min = 10U, Max = 10U, Width = 14D, CustomWidth = true });

    private static Stylesheet CreateStylesheet()
    {
        var numberingFormats = new NumberingFormats(
            new NumberingFormat { NumberFormatId = 164U, FormatCode = "mm.yyyy" },
            new NumberingFormat { NumberFormatId = 165U, FormatCode = "#,##0.00" },
            new NumberingFormat { NumberFormatId = 166U, FormatCode = "#,##0" },
            new NumberingFormat { NumberFormatId = 167U, FormatCode = "dd.mm.yyyy" })
        { Count = 4U };

        var fonts = new Fonts(
            new Font(
                new FontSize { Val = 11D },
                new FontName { Val = "Calibri" }),
            new Font(
                new Bold(),
                new FontSize { Val = 11D },
                new Color { Rgb = "FF000000" },
                new FontName { Val = "Calibri" }))
        { Count = 2U };

        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            new Fill(
                new PatternFill(
                    new ForegroundColor { Rgb = "FFFFE58F" },
                    new BackgroundColor { Indexed = 64U })
                { PatternType = PatternValues.Solid }))
        { Count = 3U };

        var borders = new Borders(
            new Border(),
            new Border(
                new LeftBorder { Style = BorderStyleValues.Thin },
                new RightBorder { Style = BorderStyleValues.Thin },
                new TopBorder { Style = BorderStyleValues.Thin },
                new BottomBorder { Style = BorderStyleValues.Thin }))
        { Count = 2U };

        var cellStyleFormats = new CellStyleFormats(new CellFormat()) { Count = 1U };
        var cellFormats = new CellFormats(
            new CellFormat(),
            new CellFormat
            {
                FontId = 1U,
                FillId = 2U,
                BorderId = 1U,
                ApplyFont = true,
                ApplyFill = true,
                ApplyBorder = true,
                ApplyAlignment = true,
                Alignment = new Alignment
                {
                    Horizontal = HorizontalAlignmentValues.Center,
                    Vertical = VerticalAlignmentValues.Center,
                    WrapText = true
                }
            },
            NumberFormat(164U),
            NumberFormat(165U),
            // The formula guard: the cell is marked as literal text, so its
            // content is shown and never evaluated.
            new CellFormat { QuotePrefix = true },
            new CellFormat { FontId = 1U, ApplyFont = true },
            new CellFormat
            {
                FontId = 1U,
                NumberFormatId = 165U,
                ApplyFont = true,
                ApplyNumberFormat = true,
                Alignment = new Alignment { Vertical = VerticalAlignmentValues.Center }
            },
            NumberFormat(166U),
            NumberFormat(167U),
            new CellFormat { QuotePrefix = true, ApplyAlignment = true,
                Alignment = new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Center } })
        { Count = 10U };

        return new Stylesheet(
            numberingFormats,
            fonts,
            fills,
            borders,
            cellStyleFormats,
            cellFormats,
            new CellStyles(
                new CellStyle { Name = "Обычный", FormatId = 0U, BuiltinId = 0U })
            { Count = 1U },
            new DifferentialFormats { Count = 0U },
            new TableStyles
            {
                Count = 0U,
                DefaultTableStyle = "TableStyleMedium2",
                DefaultPivotStyle = "PivotStyleLight16"
            });
    }

    private static CellFormat NumberFormat(uint formatId) => new()
    {
        NumberFormatId = formatId,
        ApplyNumberFormat = true,
        Alignment = new Alignment { Vertical = VerticalAlignmentValues.Center }
    };
}
