using System.Globalization;
using System.Text;
using Bms.Application.Accruals;
using Bms.Domain.Accruals;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Bms.Api.Exports;

internal static class ActiveAccrualExcelExporter
{
    internal const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const uint DefaultStyle = 0U;
    private const uint HeaderStyle = 1U;
    private const uint MonthStyle = 2U;
    private const uint DateStyle = 3U;
    private const uint MoneyStyle = 4U;
    private const uint GuardedTextStyle = 5U;
    private const uint TotalLabelStyle = 6U;
    private const uint TotalMoneyStyle = 7U;

    private static readonly string[] Headers =
    [
        "Период начисления с",
        "Период начисления по",
        "Полная оплата",
        "Дней без закрытия",
        "Возрастная зона",
        "Расходная заявка / строка",
        "Строк в заявке",
        "Организация",
        "Инициатор",
        "ЦФО",
        "Статья ДДС",
        "Статья расходов",
        "Контрагент",
        "Договор",
        "Назначение",
        "Состояние закрытия",
        "Исходная сумма",
        "Закрыто",
        "Остаток",
        "Валюта",
        "Исходная сумма, KZT",
        "Закрыто, KZT",
        "Остаток, KZT"
    ];

    internal static readonly string[] SelectableColumnKeys =
    [
        "accrualPeriod", "fullyPaidDate", "ageDays", "ageZone", "requestLine", "lineCount",
        "organization", "initiator", "costCenter", "cashFlowItem", "expenseItem",
        "counterparty", "contract", "purpose", "closingProgress", "originalAmount",
        "confirmedClosedAmount", "remainingAmount", "originalAmountKzt",
        "confirmedClosedAmountKzt", "remainingAmountKzt"
    ];

    private static readonly IReadOnlyDictionary<string, string> SelectableHeaders =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["accrualPeriod"] = "Период начисления",
            ["fullyPaidDate"] = "Полная оплата",
            ["ageDays"] = "Дней без закрытия",
            ["ageZone"] = "Возрастная зона",
            ["requestLine"] = "Расходная заявка / строка",
            ["lineCount"] = "Строк в заявке",
            ["organization"] = "Организация",
            ["initiator"] = "Инициатор",
            ["costCenter"] = "ЦФО",
            ["cashFlowItem"] = "Статья ДДС",
            ["expenseItem"] = "Статья расходов",
            ["counterparty"] = "Контрагент",
            ["contract"] = "Договор",
            ["purpose"] = "Назначение",
            ["closingProgress"] = "Закрытие",
            ["originalAmount"] = "Исходная сумма",
            ["confirmedClosedAmount"] = "Закрыто",
            ["remainingAmount"] = "Остаток",
            ["originalAmountKzt"] = "Исходная сумма, KZT",
            ["confirmedClosedAmountKzt"] = "Закрыто, KZT",
            ["remainingAmountKzt"] = "Остаток, KZT"
        };

    internal static MemoryStream CreateStream(
        ActiveAccrualExportData export,
        CancellationToken cancellationToken) =>
        CreateStream(export, null, cancellationToken);

    internal static MemoryStream CreateStream(
        ActiveAccrualExportData export,
        IReadOnlyList<string>? selectedColumns,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(export.Filter);
        ArgumentNullException.ThrowIfNull(export.Rows);
        ArgumentNullException.ThrowIfNull(export.Totals);
        ArgumentNullException.ThrowIfNull(export.Aging);
        export.Filter.Validate();
        if (export.Aging.AsOfDate == default)
        {
            throw new ActiveAccrualReportUnavailableException();
        }
        if (export.Rows.Count > ActiveAccrualExportPolicy.MaximumRows)
        {
            throw new ActiveAccrualExportTooLargeException(
                export.Rows.Count,
                ActiveAccrualExportPolicy.MaximumRows);
        }
        if (selectedColumns is not null
            && (selectedColumns.Count == 0
                || selectedColumns.Distinct(StringComparer.Ordinal).Count() != selectedColumns.Count
                || selectedColumns.Any(column => !SelectableHeaders.ContainsKey(column))))
        {
            throw new InvalidActiveAccrualQueryException();
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
                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var currencyStyles = selectedColumns is null ? null : new CurrencyStyleRegistry();
                WriteWorksheet(
                    worksheetPart,
                    export,
                    selectedColumns,
                    currencyStyles,
                    cancellationToken);
                stylesPart.Stylesheet = CreateStylesheet(currencyStyles?.CurrencyCodes ?? []);

                var sheets = workbookPart.Workbook.AppendChild(new Sheets());
                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1U,
                    Name = "Активные начисления"
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

    internal static string FileName(DateTimeOffset generatedAtUtc) =>
        $"active-accruals-{generatedAtUtc.UtcDateTime:yyyyMMdd-HHmmss}Z.xlsx";

    private static void WriteWorksheet(
        WorksheetPart worksheetPart,
        ActiveAccrualExportData export,
        IReadOnlyList<string>? selectedColumns,
        CurrencyStyleRegistry? currencyStyles,
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
        writer.WriteElement(selectedColumns is null
            ? CreateColumns()
            : CreateSelectedColumns(selectedColumns.Count));
        writer.WriteStartElement(new SheetData());
        writer.WriteElement(selectedColumns is null
            ? CreateHeaderRow()
            : CreateSelectedHeaderRow(selectedColumns));
        var thresholds = new AccrualAgeThresholds(
            export.Aging.YellowFromDays, export.Aging.OrangeFromDays, export.Aging.RedFromDays);

        // W578. A caller that has already given up is answered before the row
        // sequence is touched at all: the first MoveNext may be a database
        // read, and there is no reason to make it for an answer nobody wants.
        cancellationToken.ThrowIfCancellationRequested();

        uint rowIndex = 2U;
        var grouped = export.Filter.Grouping != ActiveAccrualGrouping.None;
        foreach (var source in export.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(source);
            if (grouped ? source.StartsGroup is null || (rowIndex == 2U && source.StartsGroup != true)
                : source.StartsGroup is not null)
            {
                throw new ActiveAccrualReportUnavailableException();
            }
            if (grouped && source.StartsGroup == true)
            {
                var label = export.Filter.Grouping == ActiveAccrualGrouping.Counterparty
                    ? $"Контрагент: {source.CounterpartyName ?? "Без контрагента"}"
                    : $"Договор: {source.ContractName ?? "Без договора"}";
                var header = new Row { RowIndex = rowIndex };
                for (var column = 1; column <= (selectedColumns?.Count ?? Headers.Length); column++)
                {
                    header.Append(TextCell(rowIndex, column, column == 1 ? label : string.Empty, HeaderStyle));
                }
                writer.WriteElement(header);
                rowIndex++;
            }
            writer.WriteElement(selectedColumns is null
                ? CreateDataRow(rowIndex++, source, export.Aging, thresholds)
                : CreateSelectedDataRow(
                    rowIndex++,
                    source,
                    export.Aging,
                    thresholds,
                    selectedColumns,
                    currencyStyles ?? throw new InvalidOperationException("Currency styles are required for selected columns.")));
        }

        var lastDataRow = Math.Max(1U, rowIndex - 1U);
        writer.WriteElement(selectedColumns is null
            ? CreateTotalsRow(lastDataRow + 2U, export.Totals)
            : CreateSelectedTotalsRow(lastDataRow + 2U, export.Totals, selectedColumns));
        writer.WriteEndElement();
        var lastColumn = ColumnName(selectedColumns?.Count ?? Headers.Length);
        if (!grouped)
        {
            // Excel AutoFilter treats section headers as data. Keep it for the
            // flat table only; the grouped book still uses all server filters.
            writer.WriteElement(new AutoFilter { Reference = $"A1:{lastColumn}{lastDataRow}" });
        }
        writer.WriteElement(new HeaderFooter(new OddFooter(
            $"&LВозраст на {export.Aging.AsOfDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}; "
            + $"жёлтая зона с {thresholds.YellowFromDays}, оранжевая с {thresholds.OrangeFromDays}, "
            + $"красная с {thresholds.RedFromDays} дней")));
        writer.WriteEndElement();
    }

    private static Row CreateHeaderRow()
    {
        var row = new Row { RowIndex = 1U, Height = 30D, CustomHeight = true };
        for (var index = 0; index < Headers.Length; index++)
        {
            row.Append(TextCell(1U, index + 1, Headers[index], HeaderStyle));
        }

        return row;
    }

    private static Row CreateSelectedHeaderRow(IReadOnlyList<string> columns)
    {
        var row = new Row { RowIndex = 1U, Height = 30D, CustomHeight = true };
        for (var index = 0; index < columns.Count; index++)
        {
            row.Append(TextCell(1U, index + 1, SelectableHeaders[columns[index]], HeaderStyle));
        }

        return row;
    }

    private static Row CreateSelectedDataRow(
        uint rowIndex,
        ActiveAccrualRowDto source,
        ActiveAccrualAgingDto aging,
        AccrualAgeThresholds thresholds,
        IReadOnlyList<string> columns,
        CurrencyStyleRegistry currencyStyles)
    {
        var days = aging.AsOfDate.DayNumber - source.FullyPaidBusinessDate.DayNumber;
        if (source.FullyPaidBusinessDate == default || days < 0 || days != source.AgeDays)
        {
            throw new ActiveAccrualReportUnavailableException();
        }

        var zone = thresholds.Classify(days);
        if (source.AgeZone != AccrualAgingContext.PublicZone(zone))
        {
            throw new ActiveAccrualReportUnavailableException();
        }

        var row = new Row { RowIndex = rowIndex };
        for (var index = 0; index < columns.Count; index++)
        {
            var column = index + 1;
            var periodThrough = source.AccrualPeriodThrough ?? source.AccrualPeriod;
            var selected = columns[index];
            var cell = selected switch
            {
                "accrualPeriod" => DataTextCell(
                    rowIndex,
                    column,
                    source.AccrualPeriod == periodThrough
                        ? source.AccrualPeriod.ToString("MM.yyyy", CultureInfo.InvariantCulture)
                        : $"{source.AccrualPeriod:MM.yyyy}–{periodThrough:MM.yyyy}"),
                "fullyPaidDate" => DateCell(rowIndex, column, source.FullyPaidBusinessDate, DateStyle),
                "ageDays" => IntegerCell(rowIndex, column, source.AgeDays),
                "ageZone" => DataTextCell(rowIndex, column, ZoneLabel(zone)),
                "requestLine" => DataTextCell(
                    rowIndex,
                    column,
                    source.IsRequestSummary
                        ? "Заявка"
                        : source.LineNumber.GetValueOrDefault().ToString(CultureInfo.InvariantCulture)),
                "lineCount" => IntegerCell(rowIndex, column, source.LineCount),
                "organization" => DataTextCell(rowIndex, column, source.OrganizationName),
                "initiator" => DataTextCell(rowIndex, column, source.InitiatorName ?? string.Empty),
                "costCenter" => DataTextCell(rowIndex, column, source.CostCenterName),
                "cashFlowItem" => DataTextCell(rowIndex, column, source.CashFlowItemName),
                "expenseItem" => DataTextCell(rowIndex, column, source.ExpenseItemName),
                "counterparty" => DataTextCell(rowIndex, column, source.CounterpartyName ?? string.Empty),
                "contract" => DataTextCell(rowIndex, column, source.ContractName ?? string.Empty),
                "purpose" => DataTextCell(rowIndex, column, source.Purpose ?? string.Empty),
                "closingProgress" => DataTextCell(
                    rowIndex,
                    column,
                    source.ClosingProgress == "partiallyClosed" ? "Закрыто частично" : "Не закрыто"),
                "originalAmount" => NumberCell(
                    rowIndex,
                    column,
                    source.OriginalAmount,
                    currencyStyles.StyleFor(source.CurrencyCode, zone)),
                "confirmedClosedAmount" => NumberCell(
                    rowIndex,
                    column,
                    source.ConfirmedClosedAmount,
                    currencyStyles.StyleFor(source.CurrencyCode, zone)),
                "remainingAmount" => NumberCell(
                    rowIndex,
                    column,
                    source.RemainingAmount,
                    currencyStyles.StyleFor(source.CurrencyCode, zone)),
                "originalAmountKzt" => NumberCell(rowIndex, column, source.OriginalAmountKzt, MoneyStyle),
                "confirmedClosedAmountKzt" => NumberCell(
                    rowIndex, column, source.ConfirmedClosedAmountKzt, MoneyStyle),
                "remainingAmountKzt" => NumberCell(
                    rowIndex, column, source.RemainingAmountKzt, MoneyStyle),
                _ => throw new InvalidActiveAccrualQueryException()
            };
            if (zone != AccrualAgeZone.None
                && selected is not ("originalAmount" or "confirmedClosedAmount" or "remainingAmount"))
            {
                cell.StyleIndex = 8U * (uint)zone + (cell.StyleIndex?.Value ?? DefaultStyle);
            }
            row.Append(cell);
        }

        return row;
    }

    private static Row CreateDataRow(
        uint rowIndex,
        ActiveAccrualRowDto source,
        ActiveAccrualAgingDto aging,
        AccrualAgeThresholds thresholds)
    {
        var days = aging.AsOfDate.DayNumber - source.FullyPaidBusinessDate.DayNumber;
        if (source.FullyPaidBusinessDate == default || days < 0 || days != source.AgeDays)
        {
            throw new ActiveAccrualReportUnavailableException();
        }

        var zone = thresholds.Classify(days);
        if (source.AgeZone != AccrualAgingContext.PublicZone(zone))
        {
            throw new ActiveAccrualReportUnavailableException();
        }

        var row = new Row { RowIndex = rowIndex };
        row.Append(DateCell(rowIndex, 1, source.AccrualPeriod, MonthStyle));
        row.Append(DateCell(
            rowIndex,
            2,
            source.AccrualPeriodThrough ?? source.AccrualPeriod,
            MonthStyle));
        row.Append(DateCell(rowIndex, 3, source.FullyPaidBusinessDate, DateStyle));
        row.Append(IntegerCell(rowIndex, 4, source.AgeDays));
        row.Append(DataTextCell(rowIndex, 5, zone switch
        {
            AccrualAgeZone.Red => "Красная",
            AccrualAgeZone.Orange => "Оранжевая",
            AccrualAgeZone.Yellow => "Жёлтая",
            _ => "Без выделения"
        }));
        row.Append(DataTextCell(
            rowIndex,
            6,
            source.IsRequestSummary
                ? "Свернуто до заявки"
                : source.LineNumber.GetValueOrDefault().ToString(CultureInfo.InvariantCulture)));
        row.Append(IntegerCell(rowIndex, 7, source.LineCount));
        row.Append(DataTextCell(rowIndex, 8, source.OrganizationName));
        row.Append(DataTextCell(rowIndex, 9, source.InitiatorName ?? string.Empty));
        row.Append(DataTextCell(rowIndex, 10, source.CostCenterName));
        row.Append(DataTextCell(rowIndex, 11, source.CashFlowItemName));
        row.Append(DataTextCell(rowIndex, 12, source.ExpenseItemName));
        row.Append(DataTextCell(rowIndex, 13, source.CounterpartyName ?? string.Empty));
        row.Append(DataTextCell(rowIndex, 14, source.ContractName ?? string.Empty));
        row.Append(DataTextCell(rowIndex, 15, source.Purpose ?? string.Empty));
        row.Append(DataTextCell(
            rowIndex,
            16,
            source.ClosingProgress == "partiallyClosed" ? "Закрыт частично" : "Не закрыт"));
        row.Append(NumberCell(rowIndex, 17, source.OriginalAmount, MoneyStyle));
        row.Append(NumberCell(rowIndex, 18, source.ConfirmedClosedAmount, MoneyStyle));
        row.Append(NumberCell(rowIndex, 19, source.RemainingAmount, MoneyStyle));
        row.Append(DataTextCell(rowIndex, 20, source.CurrencyCode));
        row.Append(NumberCell(rowIndex, 21, source.OriginalAmountKzt, MoneyStyle));
        row.Append(NumberCell(rowIndex, 22, source.ConfirmedClosedAmountKzt, MoneyStyle));
        row.Append(NumberCell(rowIndex, 23, source.RemainingAmountKzt, MoneyStyle));
        ApplyZoneStyle(row, zone);

        return row;
    }

    private static string ZoneLabel(AccrualAgeZone zone) => zone switch
    {
        AccrualAgeZone.Red => "Красная",
        AccrualAgeZone.Orange => "Оранжевая",
        AccrualAgeZone.Yellow => "Жёлтая",
        _ => "Без выделения"
    };

    private static void ApplyZoneStyle(Row row, AccrualAgeZone zone)
    {
        if (zone == AccrualAgeZone.None)
        {
            return;
        }

        foreach (var cell in row.Elements<Cell>())
        {
            cell.StyleIndex = 8U * (uint)zone + (cell.StyleIndex?.Value ?? DefaultStyle);
        }
    }

    private static Row CreateTotalsRow(uint rowIndex, ActiveAccrualTotalsDto totals)
    {
        var row = new Row { RowIndex = rowIndex };
        row.Append(TextCell(rowIndex, 1, "Итого по выбранной области", TotalLabelStyle));
        row.Append(NumberCell(rowIndex, 21, totals.OriginalAmountKzt, TotalMoneyStyle));
        row.Append(NumberCell(rowIndex, 22, totals.ConfirmedClosedAmountKzt, TotalMoneyStyle));
        row.Append(NumberCell(rowIndex, 23, totals.RemainingAmountKzt, TotalMoneyStyle));
        return row;
    }

    private static Row CreateSelectedTotalsRow(
        uint rowIndex,
        ActiveAccrualTotalsDto totals,
        IReadOnlyList<string> columns)
    {
        var row = new Row { RowIndex = rowIndex };
        var firstTotalIndex = -1;
        for (var index = 0; index < columns.Count; index++)
        {
            if (IsTotalColumn(columns[index]))
            {
                firstTotalIndex = index;
                break;
            }
        }

        var labelColumn = firstTotalIndex != 0 ? 1 : 0;
        if (labelColumn > 0)
        {
            row.Append(TextCell(rowIndex, labelColumn, "Итого по выбранной области", TotalLabelStyle));
        }

        for (var index = 0; index < columns.Count; index++)
        {
            decimal? value = columns[index] switch
            {
                "originalAmountKzt" => totals.OriginalAmountKzt,
                "confirmedClosedAmountKzt" => totals.ConfirmedClosedAmountKzt,
                "remainingAmountKzt" => totals.RemainingAmountKzt,
                _ => null
            };
            if (value.HasValue)
            {
                row.Append(NumberCell(rowIndex, index + 1, value.Value, TotalMoneyStyle));
            }
        }

        return row;
    }

    private static bool IsTotalColumn(string column) =>
        column is "originalAmountKzt" or "confirmedClosedAmountKzt" or "remainingAmountKzt";

    private static Cell DataTextCell(uint row, int column, string value) =>
        TextCell(
            row,
            column,
            value,
            NeedsFormulaGuard(value) ? GuardedTextStyle : DefaultStyle);

    internal static bool NeedsFormulaGuard(string value) =>
        !string.IsNullOrEmpty(value)
        && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n';

    private static Cell TextCell(
        uint row,
        int column,
        string value,
        uint styleIndex) => new()
        {
            CellReference = CellReference(row, column),
            DataType = CellValues.InlineString,
            StyleIndex = styleIndex,
            InlineString = new InlineString(
            new Text(XmlSafeText(value)) { Space = SpaceProcessingModeValues.Preserve })
        };

    private static Cell DateCell(uint row, int column, DateOnly value, uint styleIndex) => new()
    {
        CellReference = CellReference(row, column),
        StyleIndex = styleIndex,
        CellValue = new CellValue(
            value.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture))
    };

    private static Cell IntegerCell(uint row, int column, int value) => new()
    {
        CellReference = CellReference(row, column),
        CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
    };

    private static Cell NumberCell(uint row, int column, decimal value, uint styleIndex) => new()
    {
        CellReference = CellReference(row, column),
        StyleIndex = styleIndex,
        CellValue = new CellValue(ExcelDecimalPrecision.Format(value))
    };

    /// <summary>
    /// W577. The column names this sheet uses. The report has a fixed set of
    /// columns, and naming them once removes a loop and a string per cell.
    /// </summary>
    private static readonly string[] ColumnNames = BuildColumnNames(Headers.Length);

    private static string[] BuildColumnNames(int count)
    {
        var names = new string[count + 1];
        for (var column = 1; column <= count; column++)
        {
            var dividend = column;
            var name = string.Empty;
            while (dividend > 0)
            {
                var modulo = (dividend - 1) % 26;
                name = (char)('A' + modulo) + name;
                dividend = (dividend - modulo) / 26;
            }

            names[column] = name;
        }

        return names;
    }

    private static string ColumnName(int column) =>
        column >= 1 && column < ColumnNames.Length
            ? ColumnNames[column]
            : BuildColumnNames(column)[column];

    private static string CellReference(uint row, int column) =>
        string.Concat(ColumnName(column), row.ToString(CultureInfo.InvariantCulture));

    private static string XmlSafeText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        // W577. Almost every cell already holds text XML can carry, and for
        // that text the walk below returns the string it was given. Finding
        // that out costs one scan and no allocation; only text that really has
        // to be rewritten pays for a builder.
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
                scalar is 0x9 or 0xA or 0xD
                || scalar is >= 0x20 and <= 0xD7FF
                || scalar is >= 0xE000 and <= 0xFFFD
                || scalar is >= 0x10000 and <= 0x10FFFF
                    ? rune.ToString()
                    : "\uFFFD");
        }

        return result.ToString();
    }

    /// <summary>
    /// W577. The first index from which <see cref="XmlSafeText"/> would not
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
        new Column { Min = 1U, Max = 3U, Width = 20D, CustomWidth = true },
        new Column { Min = 4U, Max = 5U, Width = 20D, CustomWidth = true },
        new Column { Min = 6U, Max = 7U, Width = 20D, CustomWidth = true },
        new Column { Min = 8U, Max = 14U, Width = 32D, CustomWidth = true },
        new Column { Min = 15U, Max = 15U, Width = 45D, CustomWidth = true },
        new Column { Min = 16U, Max = 16U, Width = 19D, CustomWidth = true },
        new Column { Min = 17U, Max = 19U, Width = 18D, CustomWidth = true },
        new Column { Min = 20U, Max = 20U, Width = 10D, CustomWidth = true },
        new Column { Min = 21U, Max = 23U, Width = 20D, CustomWidth = true });

    private static Columns CreateSelectedColumns(int count)
    {
        var columns = new Columns();
        for (uint index = 1; index <= count; index++)
        {
            columns.Append(new Column
            {
                Min = index,
                Max = index,
                Width = 22D,
                CustomWidth = true
            });
        }

        return columns;
    }

    private static Stylesheet CreateStylesheet(IReadOnlyList<string> currencyCodes)
    {
        var numberingFormats = new NumberingFormats(
            new NumberingFormat { NumberFormatId = 164U, FormatCode = "mm.yyyy" },
            new NumberingFormat { NumberFormatId = 165U, FormatCode = "dd.mm.yyyy" },
            new NumberingFormat { NumberFormatId = 166U, FormatCode = "#,##0" })
        { Count = 3U };
        for (var index = 0; index < currencyCodes.Count; index++)
        {
            numberingFormats.Append(new NumberingFormat
            {
                NumberFormatId = 167U + (uint)index,
                FormatCode = $"#,##0 \"{currencyCodes[index]}\""
            });
        }
        numberingFormats.Count = (uint)numberingFormats.ChildElements.Count;
        var fonts = new Fonts(
            new Font(new FontSize { Val = 11D }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 11D }, new FontName { Val = "Calibri" }))
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
            NumberFormat(166U),
            new CellFormat { QuotePrefix = true },
            new CellFormat { FontId = 1U, ApplyFont = true },
            new CellFormat
            {
                FontId = 1U,
                NumberFormatId = 166U,
                ApplyFont = true,
                ApplyNumberFormat = true
            })
        { Count = 8U };

        var baseFormats = cellFormats.Elements<CellFormat>().ToArray();
        foreach (var color in new[] { "FFFFF3CD", "FFFFE1B5", "FFFADBD8" })
        {
            var fillId = (uint)fills.ChildElements.Count;
            fills.Append(new Fill(new PatternFill(
                new ForegroundColor { Rgb = color }, new BackgroundColor { Indexed = 64U })
            { PatternType = PatternValues.Solid }));
            foreach (var format in baseFormats)
            {
                var colored = (CellFormat)format.CloneNode(true);
                colored.FillId = fillId;
                colored.ApplyFill = true;
                cellFormats.Append(colored);
            }
        }
        for (var currencyIndex = 0; currencyIndex < currencyCodes.Count; currencyIndex++)
        {
            var numberFormatId = 167U + (uint)currencyIndex;
            for (uint zone = 0; zone < 4; zone++)
            {
                var format = NumberFormat(numberFormatId);
                if (zone > 0)
                {
                    format.FillId = 2U + zone;
                    format.ApplyFill = true;
                }
                cellFormats.Append(format);
            }
        }
        fills.Count = (uint)fills.ChildElements.Count;
        cellFormats.Count = (uint)cellFormats.ChildElements.Count;

        return new Stylesheet(
            numberingFormats,
            fonts,
            fills,
            borders,
            new CellStyleFormats(new CellFormat()) { Count = 1U },
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

    private sealed class CurrencyStyleRegistry
    {
        private readonly Dictionary<string, int> indexes = new(StringComparer.Ordinal);
        private readonly List<string> currencyCodes = [];

        internal IReadOnlyList<string> CurrencyCodes => currencyCodes;

        internal uint StyleFor(string currencyCode, AccrualAgeZone zone)
        {
            if (currencyCode.Length != 3 || currencyCode.Any(character => character is < 'A' or > 'Z'))
            {
                throw new ActiveAccrualReportUnavailableException();
            }

            if (!indexes.TryGetValue(currencyCode, out var index))
            {
                index = currencyCodes.Count;
                indexes.Add(currencyCode, index);
                currencyCodes.Add(currencyCode);
            }

            return 32U + (uint)(index * 4) + (uint)zone;
        }
    }
}
