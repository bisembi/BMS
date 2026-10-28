using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Bms.Domain.Common;

namespace Bms.Application.Budgeting;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CashFlowActualsSourceRow(int SourceOrdinal, string ArticleName,
    DateOnly BudgetMonth, decimal SignedAmountKzt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportCashFlowActualsCommand(
    [property: JsonRequired] string SourceHash,
    [property: JsonRequired] string SourceGeneration,
    [property: JsonRequired] DateTimeOffset CapturedAtUtc,
    [property: JsonRequired] IReadOnlyList<DateOnly> AvailableMonths,
    [property: JsonRequired] IReadOnlyList<string> ArticleNames,
    [property: JsonRequired] IReadOnlyList<CashFlowActualsSourceRow> Rows,
    [property: JsonRequired] decimal ReceiptsAmountKzt,
    [property: JsonRequired] decimal PaymentsAmountKzt,
    [property: JsonRequired] decimal NetCashFlowAmountKzt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewBudgetActualsCommand(
    [property: JsonRequired] Guid CostCenterId,
    [property: JsonRequired] DateOnly FromMonth,
    [property: JsonRequired] DateOnly ToMonth,
    [property: JsonRequired] IReadOnlyList<Guid> CashFlowItemIds,
    Guid? BudgetInstanceId = null,
    Guid? ExpectedConcurrencyStamp = null);

public sealed record BudgetActualsLineDto(Guid CashFlowItemId, DateOnly BudgetMonth,
    decimal LimitAmountKzt, string FlowDirection);

public sealed record BudgetActualsPreviewDto(Guid SnapshotId, string SourceHash,
    DateTimeOffset CapturedAtUtc, Guid CostCenterId, Guid? BudgetInstanceId,
    DateOnly FromMonth, DateOnly ToMonth, IReadOnlyList<Guid> CashFlowItemIds,
    IReadOnlyList<DateOnly> AvailableMonths, IReadOnlyList<BudgetActualsLineDto> Lines,
    decimal ReceiptsAmountKzt, decimal PaymentsAmountKzt, decimal NetCashFlowAmountKzt);

public sealed record CashFlowActualsImportResultDto(Guid SnapshotId, string SourceHash,
    bool AlreadyImported, int RowCount, int ArticleCount, int UnmappedArticleCount,
    IReadOnlyList<string> UnmappedArticleNames, IReadOnlyList<DateOnly> AvailableMonths,
    decimal ReceiptsAmountKzt, decimal PaymentsAmountKzt, decimal NetCashFlowAmountKzt);

public sealed class CashFlowActualsNotAvailableException : Exception;
public sealed class CashFlowActualsPeriodUnavailableException : Exception;
public sealed class CashFlowActualsMappingUnavailableException : Exception;
public sealed class CashFlowActualsSourceInvalidException : Exception;

public static class CashFlowActualsSource
{
    public const int Year = 2026;
    public const decimal MaximumSafeMoney = 90071992547409.91m;

    public static bool IsExcludedArticle(string value) =>
        value.Contains("ВГО", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Внутригрупп", StringComparison.OrdinalIgnoreCase)
        || new[] { "111116", "710006", "111121", "111122", "859701", "859702" }
            .Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal));

    public static IReadOnlyList<DateOnly> Months(DateOnly from, DateOnly to)
    {
        if (!AccountingPeriod467.IsSourceYear(from.Year) || to.Year != from.Year || from.Day != 1 || to.Day != 1 || from > to)
            throw new DomainRuleViolationException("Actuals must retain their original months within one source year.");
        var months = new List<DateOnly>();
        for (var month = from; month <= to; month = month.AddMonths(1)) months.Add(month);
        return months;
    }

    public static void Validate(ImportCashFlowActualsCommand command)
    {
        if (command.SourceHash is null || command.SourceGeneration is null
            || command.SourceHash.Length != 64 || command.SourceGeneration.Length != 64
            || !command.SourceHash.All(IsUpperHex) || !command.SourceGeneration.All(IsUpperHex)
            || command.CapturedAtUtc == default || command.CapturedAtUtc.Offset != TimeSpan.Zero
            || command.AvailableMonths is null || command.AvailableMonths.Count is < 1 or > 12
            || command.ArticleNames is null || command.ArticleNames.Count is < 1 or > 2000
            || command.Rows is null || command.Rows.Count is < 1 or > 24000
            || command.Rows.Count != command.AvailableMonths.Count * command.ArticleNames.Count
            || command.ReceiptsAmountKzt < 0 || command.PaymentsAmountKzt < 0
            || command.ReceiptsAmountKzt > MaximumSafeMoney || command.PaymentsAmountKzt > MaximumSafeMoney
            || Math.Abs(command.NetCashFlowAmountKzt) > MaximumSafeMoney)
            throw new CashFlowActualsSourceInvalidException();
        var sourceYear = ImportYear(command.AvailableMonths);
        var months = command.AvailableMonths.ToHashSet();
        if (months.Count != command.AvailableMonths.Count
            || months.Any(month => month.Year != sourceYear || month.Day != 1))
            throw new CashFlowActualsSourceInvalidException();
        var articles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in command.ArticleNames)
            if (!UsableArticle(name) || !articles.Add(name)) throw new CashFlowActualsSourceInvalidException();
        var keys = new HashSet<(string, DateOnly)>();
        var ordinal = 0;
        foreach (var row in command.Rows)
        {
            if (row is null || row.SourceOrdinal != ++ordinal || !articles.Contains(row.ArticleName)
                || !months.Contains(row.BudgetMonth) || !keys.Add((row.ArticleName, row.BudgetMonth))
                || decimal.Round(row.SignedAmountKzt, 2) != row.SignedAmountKzt
                || Math.Abs(row.SignedAmountKzt) > MaximumSafeMoney)
                throw new CashFlowActualsSourceInvalidException();
        }
        if (command.Rows.Where(row => row.SignedAmountKzt > 0).Sum(row => row.SignedAmountKzt) != command.ReceiptsAmountKzt
            || -command.Rows.Where(row => row.SignedAmountKzt < 0).Sum(row => row.SignedAmountKzt) != command.PaymentsAmountKzt
            || command.Rows.Sum(row => row.SignedAmountKzt) != command.NetCashFlowAmountKzt
            || !string.Equals(Hash(command), command.SourceHash, StringComparison.Ordinal))
            throw new CashFlowActualsSourceInvalidException();
    }

    public static string Hash(ImportCashFlowActualsCommand command)
    {
        // The manual loader implements this exact canonical representation in memory.
        var canonical = new StringBuilder(ImportYear(command.AvailableMonths).ToString(CultureInfo.InvariantCulture))
            .Append('\n').Append(command.SourceGeneration).Append('\n');
        foreach (var month in command.AvailableMonths.Order())
            canonical.Append("M\u001f").Append(month.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
        foreach (var name in command.ArticleNames.Order(StringComparer.Ordinal))
            canonical.Append("A\u001f").Append(name).Append('\n');
        foreach (var row in command.Rows.OrderBy(row => row.SourceOrdinal))
            canonical.Append(row.SourceOrdinal.ToString(CultureInfo.InvariantCulture)).Append('\u001f')
                .Append(row.ArticleName).Append('\u001f')
                .Append(row.BudgetMonth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\u001f')
                .Append(row.SignedAmountKzt.ToString("0.00", CultureInfo.InvariantCulture)).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    public static int ImportYear(IReadOnlyList<DateOnly>? availableMonths)
    {
        if (availableMonths is null || availableMonths.Count is < 1 or > 12)
            throw new CashFlowActualsSourceInvalidException();
        var sourceYear = availableMonths[0].Year;
        if (!AccountingPeriod467.IsReportYear(sourceYear)
            || availableMonths.Any(month => month.Year != sourceYear || month.Day != 1))
            throw new CashFlowActualsSourceInvalidException();
        return sourceYear;
    }

    private static bool IsUpperHex(char value) => value is >= '0' and <= '9' or >= 'A' and <= 'F';
    private static bool UsableArticle(string? name) => !string.IsNullOrWhiteSpace(name)
        && name.Length <= 500 && name == name.Trim() && !name.Any(char.IsControl) && !IsExcludedArticle(name);
}
