using System.Text.Json.Serialization;
using Bms.Application.Common;
using Bms.Domain.Common;

namespace Bms.Application.Budgeting;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BddsDrillQuery(
    [property: JsonRequired] int Year,
    [property: JsonRequired] DateOnly FromMonth,
    [property: JsonRequired] DateOnly ToMonth,
    [property: JsonRequired] Guid SnapshotId,
    [property: JsonRequired] Guid? CostCenterId,
    [property: JsonRequired] Guid? VersionId,
    [property: JsonRequired] IReadOnlyList<Guid> ArticleIds,
    [property: JsonRequired] int Skip,
    [property: JsonRequired] int Take,
    string? SortBy = null,
    string? SortDirection = null);

public sealed record BddsDrillRow(DateTime EntryDateLocal, string Company, string DocumentType,
    string Document, string Counterparty, string Notes, string Comment, decimal AmountKzt, int LineNo);
public sealed record BddsDrillPage(Guid SnapshotId, DateTimeOffset CapturedAtUtc,
    IReadOnlyList<BddsDrillRow> Items, int Skip, int Take, int Total, decimal TotalKzt);
public interface IBddsDrillStore
{
    Task<BddsDrillPage> ReadAsync(BddsDrillQuery query, CancellationToken token);
}
public sealed class BddsDrillUnavailableException : Exception;
public sealed class BddsDrillChangedException : Exception;
public sealed class BddsDrillControlException : Exception;

public sealed class BddsDrillService(BddsReportService reports, IBddsDrillStore store)
{
    public static bool IsValidSort(string? column, string? direction) => column is null
        ? direction is null
        : direction is "asc" or "desc" && column is
            "entryDateLocal" or "company" or "documentType" or "document" or
            "counterparty" or "notes" or "comment" or "amountKzt" or "lineNo";
    public async Task<BddsDrillPage> ReadAsync(BddsDrillQuery query, CancellationToken token)
    {
        if (query.Year is < 2000 or > 2100 || query.SnapshotId == Guid.Empty || query.CostCenterId == Guid.Empty
            || query.VersionId == Guid.Empty || query.FromMonth.Year != query.Year || query.ToMonth.Year != query.Year
            || query.FromMonth.Day != 1 || query.ToMonth.Day != 1 || query.FromMonth > query.ToMonth
            || query.Skip is < 0 or > 1_000_000 || query.Take is not (50 or 100)
            || !IsValidSort(query.SortBy, query.SortDirection)
            || query.ArticleIds is null || query.ArticleIds.Count is < 1 or > 2000
            || query.ArticleIds.Any(id => id == Guid.Empty) || query.ArticleIds.Distinct().Count() != query.ArticleIds.Count)
            throw new DomainRuleViolationException("Invalid BDDS detail scope.");
        // The report is the single authority for current roles, CFO scope, catalogue
        // classification and version permissions. No client-supplied row title is trusted.
        var report = await reports.ReadPeriodAsync(query.Year, query.CostCenterId,
            new(query.FromMonth, query.ToMonth, true, query.VersionId, null, query.Year), token);
        if (report.ActualsYear != query.Year || report.ActualsSnapshotId != query.SnapshotId)
            throw new BddsDrillChangedException();
        var allowed = report.Rows.Where(row => row.Kind == "Article" && row.CashFlowItemId.HasValue)
            .GroupBy(row => row.CashFlowItemId.GetValueOrDefault()).ToDictionary(group => group.Key, group => group.Single());
        if (query.ArticleIds.Any(id => !allowed.ContainsKey(id))) throw new ActorForbiddenException();
        var expected = query.ArticleIds.Sum(id => allowed[id].ActualTotal ?? 0m);
        var page = await store.ReadAsync(query, token);
        if (page.SnapshotId != query.SnapshotId || page.TotalKzt != expected
            || page.Total < page.Items.Count || page.Items.Count > query.Take
            || page.Items.Any(row => row.LineNo <= 0))
            throw new BddsDrillControlException();
        return page;
    }
}
