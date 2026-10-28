using System.Globalization;

namespace Bms.Api.Exports;

internal static class ExcelDecimalPrecision
{
    internal static string Format(decimal value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        // Same numeric contract as the calendar exports. Do not round stored
        // money or silently turn numeric amounts into text to hide the loss.
        var digits = text.Replace(".", string.Empty, StringComparison.Ordinal)
            .TrimStart('-', '0').TrimEnd('0').Length;
        if (digits > 15) throw new ExcelExportPrecisionException();
        return text;
    }
}

internal sealed class ExcelExportPrecisionException : Exception;
