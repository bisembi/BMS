using System.Globalization;
using System.Text;
using Bms.Application.Abstractions;
using Bms.Application.ExpenseRequests;
using Bms.Domain.ExpenseRequests;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Bms.Api.Exports;

internal static class ExpenseRequestExcelExporter
{
    internal const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly string[] Headers =
    [
        "Дата создания (UTC+5)",
        "Планируемая дата оплаты",
        "Назначение",
        "Организация",
        "ЦФО",
        "Контрагент",
        "Инициатор",
        "Статус",
        "Срочность",
        "Сумма",
        "Валюта",
        "Эквивалент KZT",
        "Бюджет"
    ];

    internal static byte[] Create(ExpenseRequestExportData export)
    {
        using var stream = CreateStream(export, CancellationToken.None);
        return stream.ToArray();
    }

    // The HTTP result owns this completed, rewound package. No worksheet DOM
    // or second copy of the compressed workbook is retained by the endpoint.
    internal static MemoryStream CreateStream(
        ExpenseRequestExportData export,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(export.Rows);
        cancellationToken.ThrowIfCancellationRequested();

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

                var sheets = workbookPart.Workbook.AppendChild(new Sheets());
                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1U,
                    Name = "Расходные заявки"
                });

                workbookPart.Workbook.Save();
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

    private static void WriteWorksheet(
        WorksheetPart worksheetPart,
        ExpenseRequestExportData export,
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

        uint rowIndex = 2;
        foreach (var row in export.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(row);
            writer.WriteElement(CreateDataRow(rowIndex++, row));
        }

        var lastRowIndex = Math.Max(1U, rowIndex - 1U);
        writer.WriteEndElement();
        writer.WriteElement(new AutoFilter { Reference = $"A1:M{lastRowIndex}" });
        writer.WriteEndElement();
    }

    internal static string FileName(DateTimeOffset generatedAtUtc) =>
        $"expense-requests-{generatedAtUtc.UtcDateTime:yyyyMMdd-HHmmss}Z.xlsx";

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
            row.Append(TextCell(1U, index + 1, Headers[index], 1U));
        }

        return row;
    }

    private static Row CreateDataRow(uint rowIndex, ExpenseRequestExportRow source)
    {
        var row = new Row { RowIndex = rowIndex };
        row.Append(DateTimeCell(rowIndex, 1, source.CreatedAtUtc.ToOffset(TimeSpan.FromHours(5)).DateTime, 2U));
        row.Append(source.PlannedPaymentDate.HasValue
            ? DateTimeCell(
                rowIndex,
                2,
                source.PlannedPaymentDate.Value.ToDateTime(TimeOnly.MinValue),
                3U)
            : BlankCell(rowIndex, 2, 3U));
        row.Append(TextCell(rowIndex, 3, source.Purpose ?? string.Empty));
        row.Append(TextCell(rowIndex, 4, source.OrganizationName ?? string.Empty));
        row.Append(TextCell(rowIndex, 5, source.CostCenterName ?? string.Empty));
        row.Append(TextCell(rowIndex, 6, source.CounterpartyName ?? string.Empty));
        row.Append(TextCell(rowIndex, 7, InitiatorName(source.InitiatorDisplayName)));
        row.Append(TextCell(rowIndex, 8, StatusLabel(source.Status)));
        row.Append(TextCell(rowIndex, 9, source.IsUrgent ? "Срочная" : "Обычная"));
        row.Append(NumberCell(rowIndex, 10, source.TotalAmount, 4U));
        row.Append(TextCell(rowIndex, 11, source.CurrencyCode));
        row.Append(source.TotalAmountKzt.HasValue
            ? NumberCell(rowIndex, 12, source.TotalAmountKzt.Value, 4U)
            : BlankCell(rowIndex, 12, 4U));
        row.Append(TextCell(
            rowIndex,
            13,
            source.IsOverBudget switch
            {
                true => "Сверх бюджета",
                false => "В рамках бюджета",
                null => string.Empty
            }));
        return row;
    }

    private static Cell TextCell(uint row, int column, string value, uint styleIndex = 0U) => new()
    {
        CellReference = CellReference(row, column),
        DataType = CellValues.InlineString,
        StyleIndex = styleIndex,
        InlineString = new InlineString(
            new Text(XmlSafeText(value)) { Space = SpaceProcessingModeValues.Preserve })
    };

    private static string InitiatorName(string? value)
    {
        var name = value?.Trim();
        return !string.IsNullOrWhiteSpace(name) && name.Length <= 256 && !name.Any(char.IsControl)
            && !name.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)
                ? name : "Инициатор недоступен";
    }

    private static Cell DateTimeCell(uint row, int column, DateTime value, uint styleIndex) => new()
    {
        CellReference = CellReference(row, column),
        StyleIndex = styleIndex,
        CellValue = new CellValue(value.ToOADate().ToString(CultureInfo.InvariantCulture))
    };

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

    private static string StatusLabel(string value) =>
        ExpenseRequestStatusCode.TryParse(value, out var status)
            ? status.ToDisplayName()
            : "Неизвестный статус";

    private static string XmlSafeText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var result = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
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

    private static Columns CreateColumns() => new(
        new Column { Min = 1U, Max = 1U, Width = 20D, CustomWidth = true },
        new Column { Min = 2U, Max = 2U, Width = 23D, CustomWidth = true },
        new Column { Min = 3U, Max = 3U, Width = 48D, CustomWidth = true },
        new Column { Min = 4U, Max = 4U, Width = 28D, CustomWidth = true },
        new Column { Min = 5U, Max = 5U, Width = 28D, CustomWidth = true },
        new Column { Min = 6U, Max = 6U, Width = 30D, CustomWidth = true },
        new Column { Min = 7U, Max = 7U, Width = 28D, CustomWidth = true },
        new Column { Min = 8U, Max = 8U, Width = 18D, CustomWidth = true },
        new Column { Min = 9U, Max = 9U, Width = 14D, CustomWidth = true },
        new Column { Min = 10U, Max = 10U, Width = 18D, CustomWidth = true },
        new Column { Min = 11U, Max = 11U, Width = 12D, CustomWidth = true },
        new Column { Min = 12U, Max = 12U, Width = 20D, CustomWidth = true },
        new Column { Min = 13U, Max = 13U, Width = 22D, CustomWidth = true });

    private static Stylesheet CreateStylesheet()
    {
        var numberingFormats = new NumberingFormats(
            new NumberingFormat { NumberFormatId = 164U, FormatCode = "dd.mm.yyyy hh:mm" },
            new NumberingFormat { NumberFormatId = 165U, FormatCode = "dd.mm.yyyy" },
            new NumberingFormat { NumberFormatId = 166U, FormatCode = "#,##0.00" })
        { Count = 3U };

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
            NumberFormat(166U))
        { Count = 5U };

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
