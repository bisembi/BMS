using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Bms.Domain.Common;

namespace Bms.Application.Budgeting;

public sealed record BddsSourceRowCommand(
    int SourceOrdinal,
    string SourceCostCenterName,
    string ActivityName,
    string SectionName,
    string GroupName,
    string ArticleBusinessCode,
    string ArticleName,
    DateOnly BudgetMonth,
    decimal SignedAmountKzt,
    string SourceFile,
    string SourceSheet,
    int SourceRowNumber,
    DateTimeOffset SourceLoadedAtUtc);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportBddsSnapshotCommand(
    [property: JsonRequired] DateOnly SourceVersionDate,
    [property: JsonRequired] string Scenario,
    [property: JsonRequired] int BudgetYear,
    [property: JsonRequired] string SourceHash,
    [property: JsonRequired] IReadOnlyList<BddsSourceRowCommand> Rows);

public sealed record BddsImportResultDto(
    Guid SnapshotId,
    string SourceHash,
    bool AlreadyImported,
    int SourceRowCount,
    int BudgetInstanceCount,
    int BudgetLineCount,
    decimal ReceiptsAmountKzt,
    decimal PaymentsAmountKzt,
    decimal NetCashFlowAmountKzt,
    DateTimeOffset ApprovedAtUtc);

public sealed record BddsCostCenterDto(Guid Id, string Code, string Name);

public sealed record BddsReportRowDto(
    string Key,
    string? ParentKey,
    string Kind,
    string Name,
    IReadOnlyList<decimal> Amounts,
    decimal Total)
{
    public string? CostCenterCode { get; init; }
    public int? DisplayOrder { get; init; }
    public Guid? CashFlowItemId { get; init; }
    public Guid? DisplayGroupId { get; init; }
    public string? DisplayGroupName { get; init; }
    public string? DisplayBlockId { get; init; }
    public int? CataloguePosition { get; init; }
    public IReadOnlyList<decimal?>? ActualAmounts { get; init; }
    public decimal? ActualTotal { get; init; }
    public IReadOnlyList<decimal?>? VarianceAmounts { get; init; }
    public decimal? VarianceTotal { get; init; }
    public IReadOnlyList<decimal?>? VariancePercentages { get; init; }
    public decimal? VariancePercentageTotal { get; init; }
    public IReadOnlyList<decimal>? CompareAmounts { get; init; }
    public decimal? CompareTotal { get; init; }
    public IReadOnlyList<decimal>? VersionVarianceAmounts { get; init; }
    public decimal? VersionVarianceTotal { get; init; }
    public IReadOnlyList<decimal?>? VersionVariancePercentages { get; init; }
    public decimal? VersionVariancePercentageTotal { get; init; }
}

public sealed record BddsReportDto(
    Guid SnapshotId,
    DateOnly SourceVersionDate,
    string Scenario,
    int BudgetYear,
    Guid? CostCenterId,
    IReadOnlyList<BddsCostCenterDto> CostCenters,
    IReadOnlyList<BddsReportRowDto> Rows,
    decimal ReceiptsAmountKzt,
    decimal PaymentsAmountKzt,
    decimal NetCashFlowAmountKzt,
    DateTimeOffset ApprovedAtUtc)
{
    // Legacy snapshot fields identify the preserved import/hierarchy anchor,
    // not the financial source of the current Plan projection.
    public string? PlanSource { get; init; }
    public BddsCompanyVersionDto? PlanVersion { get; init; }
    public BddsCompanyVersionDto? CompareVersion { get; init; }
    public decimal? CompareReceiptsAmountKzt { get; init; }
    public decimal? ComparePaymentsAmountKzt { get; init; }
    public decimal? CompareNetCashFlowAmountKzt { get; init; }
    public IReadOnlyList<DateOnly>? Months { get; init; }
    public bool CanIncludeActuals { get; init; }
    public bool IncludesActuals { get; init; }
    public int? ActualsYear { get; init; }
    public int? ActualsSourceYear { get; init; }
    public Guid? ActualsSnapshotId { get; init; }
    public DateTimeOffset? ActualsCapturedAtUtc { get; init; }
    public IReadOnlyList<DateOnly>? ActualsAvailableMonths { get; init; }
    public decimal? ActualReceiptsAmountKzt { get; init; }
    public decimal? ActualPaymentsAmountKzt { get; init; }
    public decimal? ActualNetCashFlowAmountKzt { get; init; }
}

public sealed class BddsSnapshotNotFoundException : Exception;
public sealed record BddsCompanyVersionDto(Guid Id, string Name, Guid ConcurrencyStamp, bool IsActive, bool IsPublished);
public sealed class CompanyBudgetOrderUnresolvedException : Exception;

public sealed class BddsImportConflictException(string message) : Exception(message);

public static class BddsCanonicalHash
{
    public static string Compute(IReadOnlyList<BddsSourceRowCommand> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var row in rows.OrderBy(value => value.SourceOrdinal))
        {
            ArgumentNullException.ThrowIfNull(row);
            var fields = new[]
            {
                row.SourceOrdinal.ToString(CultureInfo.InvariantCulture),
                row.SourceCostCenterName,
                row.ActivityName,
                row.SectionName,
                row.GroupName,
                row.ArticleBusinessCode,
                row.ArticleName,
                row.BudgetMonth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                row.SignedAmountKzt.ToString("0.00", CultureInfo.InvariantCulture),
                row.SourceFile,
                row.SourceSheet,
                row.SourceRowNumber.ToString(CultureInfo.InvariantCulture),
                row.SourceLoadedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            };
            hash.AppendData(Encoding.UTF8.GetBytes(string.Join('\u001f', fields) + "\n"));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
