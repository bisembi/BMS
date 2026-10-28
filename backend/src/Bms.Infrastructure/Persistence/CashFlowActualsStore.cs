using System.Data;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Bms.Application.Abstractions;
using Bms.Application.Budgeting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class CashFlowActualsStore(BmsDbContext db, IDirectDdsReplicaReader? directReplica = null) : ICashFlowActualsStore
{
    public async Task<CashFlowActualsImportResultDto?> FindImportAsync(string sourceHash, CancellationToken cancellationToken)
    {
        EnsureTransaction();
        var snapshot = await db.CashFlowActualsSnapshots.SingleOrDefaultAsync(row => row.SourceHash == sourceHash, cancellationToken);
        if (snapshot is null) return null;
        if (!snapshot.IsSealed) throw new CashFlowActualsSourceInvalidException();
        return await ResultAsync(snapshot, true, cancellationToken);
    }

    public async Task<CashFlowActualsImportResultDto> ImportAsync(ImportCashFlowActualsCommand command,
        Guid employeeId, string externalId, DateTimeOffset importedAtUtc, CancellationToken cancellationToken)
    {
        EnsureTransaction();
        var sourceYear = CashFlowActualsSource.ImportYear(command.AvailableMonths);
        var targets = await db.CashFlowItems.AsNoTracking().Where(ImportTargets(sourceYear))
            .Select(row => new { row.Id, row.Name }).ToArrayAsync(cancellationToken);
        var history = await (
            from article in db.CashFlowActualsArticles.AsNoTracking()
            join previous in db.CashFlowActualsSnapshots.AsNoTracking() on article.SnapshotId equals previous.Id
            where previous.IsSealed && (previous.SourceYear == 2025 || previous.SourceYear == 2026)
                && article.CashFlowItemId != null
            select new { article.ArticleName, article.CashFlowItemId })
            .Distinct().ToArrayAsync(cancellationToken);
        // Validate the durable bindings before the first financial write.
        var resolvedMappings = ResolveImportMappings(command.ArticleNames,
            targets.Select(row => (row.Id, row.Name)).ToArray(),
            history.Select(row => (row.ArticleName, row.CashFlowItemId)).ToArray());
        var snapshot = new CashFlowActualsSnapshot
        {
            Id = Guid.NewGuid(),
            SourceYear = sourceYear,
            SourceHash = command.SourceHash,
            SourceGeneration = command.SourceGeneration,
            CapturedAtUtc = command.CapturedAtUtc,
            ImportedAtUtc = importedAtUtc,
            ImportedByEmployeeId = employeeId,
            ImportedByExternalId = externalId,
            RowCount = command.Rows.Count,
            ArticleCount = command.ArticleNames.Count,
            ReceiptsAmountKzt = command.ReceiptsAmountKzt,
            PaymentsAmountKzt = command.PaymentsAmountKzt,
            NetCashFlowAmountKzt = command.NetCashFlowAmountKzt,
        };
        db.CashFlowActualsSnapshots.Add(snapshot);
        await db.SaveChangesAsync(cancellationToken);
        var articles = command.ArticleNames.Order(StringComparer.Ordinal).Select((name, index) =>
            new CashFlowActualsArticle
            {
                SnapshotId = snapshot.Id,
                ArticleOrdinal = index + 1,
                ArticleName = name,
                // Preserve unmatched source data; do not guess by a nearby code or description.
                CashFlowItemId = resolvedMappings[name],
            }).ToArray();
        var ordinalByName = articles.ToDictionary(row => row.ArticleName, row => row.ArticleOrdinal, StringComparer.Ordinal);
        db.CashFlowActualsMonths.AddRange(command.AvailableMonths.Select(month => new CashFlowActualsMonth { SnapshotId = snapshot.Id, BudgetMonth = month }));
        db.CashFlowActualsArticles.AddRange(articles);
        db.CashFlowActualsRows.AddRange(command.Rows.Select(row => new CashFlowActualsRow
        {
            SnapshotId = snapshot.Id,
            SourceOrdinal = row.SourceOrdinal,
            ArticleOrdinal = ordinalByName[row.ArticleName],
            BudgetMonth = row.BudgetMonth,
            SignedAmountKzt = row.SignedAmountKzt,
        }));
        await db.SaveChangesAsync(cancellationToken);
        snapshot.IsSealed = true;
        await db.SaveChangesAsync(cancellationToken);
        return await ResultAsync(snapshot, false, cancellationToken);
    }

    internal static Expression<Func<CashFlowItemReference, bool>> ImportTargets(int sourceYear)
    {
        if (sourceYear is not (2025 or 2026)) throw new CashFlowActualsSourceInvalidException();
        return row => !row.IsGroup && ((row.IsActive && !row.IsHistoricalForActuals)
            || (sourceYear == 2025 && row.IsHistoricalForActuals && !row.IsActive && row.CostCenterId == null));
    }

    internal static IReadOnlyDictionary<string, Guid?> ResolveImportMappings(
        IReadOnlyList<string> articleNames,
        IReadOnlyList<(Guid Id, string Name)> activeLeafTargets,
        IReadOnlyList<(string ArticleName, Guid? CashFlowItemId)> sealedSupportedYearHistory)
    {
        if (articleNames.Select(Normalize).Distinct(StringComparer.Ordinal).Count() != articleNames.Count)
            throw new CashFlowActualsMappingUnavailableException();
        var eligibleIds = activeLeafTargets.Select(row => row.Id).ToHashSet();
        var byCurrentName = activeLeafTargets.GroupBy(row => Normalize(row.Name), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(row => row.Id).Distinct().ToArray(), StringComparer.Ordinal);
        var bySourceName = sealedSupportedYearHistory.Where(row => row.CashFlowItemId.HasValue)
            .GroupBy(row => Normalize(row.ArticleName), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(row => row.CashFlowItemId.GetValueOrDefault()).Distinct().ToArray(), StringComparer.Ordinal);
        var result = new Dictionary<string, Guid?>(StringComparer.Ordinal);
        var usedTargetIds = new HashSet<Guid>();
        foreach (var name in articleNames)
        {
            var key = Normalize(name);
            Guid? targetId;
            if (bySourceName.TryGetValue(key, out var confirmed))
            {
                if (confirmed.Length != 1 || !eligibleIds.Contains(confirmed[0]))
                    throw new CashFlowActualsMappingUnavailableException();
                targetId = confirmed[0];
            }
            else targetId = byCurrentName.TryGetValue(key, out var candidates) && candidates.Length == 1 ? candidates[0] : null;
            if (targetId is Guid id && !usedTargetIds.Add(id))
                throw new CashFlowActualsMappingUnavailableException();
            result.Add(name, targetId);
        }
        return result;
    }

    public async Task<BudgetActualsPreviewDto> PreviewAsync(PreviewBudgetActualsCommand command, CancellationToken cancellationToken)
    {
        EnsureTransaction();
        var current = await CurrentDdsActualsSource.ReadRequiredAsync(
            directReplica ?? new DirectDdsReplicaReader(db), cancellationToken);
        var snapshot = ActualsSnapshotReadMetadata.FromDirect(current, command.FromMonth.Year);
        var available = current.Months.Where(m => m.Year == command.FromMonth.Year).ToArray();
        var months = CashFlowActualsSource.Months(command.FromMonth, command.ToMonth);
        if (months.Except(available).Any()) throw new CashFlowActualsPeriodUnavailableException();
        var active = await db.CashFlowItems.AsNoTracking().Where(row => command.CashFlowItemIds.Contains(row.Id)
            && row.IsActive && !row.IsGroup && row.CostCenterId == command.CostCenterId)
            .Select(row => row.Id).ToArrayAsync(cancellationToken);
        if (active.Length != command.CashFlowItemIds.Count) throw new CashFlowActualsMappingUnavailableException();
        var mappings = current.NetArticles()
            .Where(row => row.CashFlowItemId != null && command.CashFlowItemIds.Contains(row.CashFlowItemId.Value))
            .Select(row => new { row.ArticleOrdinal, CashFlowItemId = row.CashFlowItemId.GetValueOrDefault() }).ToArray();
        var articleIds = mappings.Select(row => row.ArticleOrdinal).ToArray();
        var values = current.NetRows(command.FromMonth.Year)
            .Where(row => articleIds.Contains(row.ArticleOrdinal)
                && row.BudgetMonth >= command.FromMonth && row.BudgetMonth <= command.ToMonth)
            .ToArray();
        var lines = CreatePreviewLines(command.CashFlowItemIds, months,
            mappings.Select(row => (row.ArticleOrdinal, row.CashFlowItemId)).ToArray(), values);
        var receipts = lines.Where(row => row.FlowDirection == "Receipt").Sum(row => row.LimitAmountKzt);
        var payments = lines.Where(row => row.FlowDirection == "Payment").Sum(row => row.LimitAmountKzt);
        if (receipts > CashFlowActualsSource.MaximumSafeMoney || payments > CashFlowActualsSource.MaximumSafeMoney)
            throw new CashFlowActualsSourceInvalidException();
        return new BudgetActualsPreviewDto(snapshot.Id, snapshot.SourceHash, snapshot.CapturedAtUtc,
            command.CostCenterId, command.BudgetInstanceId, command.FromMonth, command.ToMonth,
            command.CashFlowItemIds.ToArray(), available, lines, receipts, payments, receipts - payments);
    }

    internal static IReadOnlyList<BudgetActualsLineDto> CreatePreviewLines(IReadOnlyList<Guid> selectedIds,
        IReadOnlyList<DateOnly> months, IReadOnlyList<(int ArticleOrdinal, Guid CashFlowItemId)> mappings,
        IReadOnlyList<CashFlowActualsRow> values)
    {
        // Several source labels resolving to one target is ambiguous, not permission to merge them.
        if (mappings.Select(row => row.CashFlowItemId).Distinct().Count() != mappings.Count)
            throw new CashFlowActualsMappingUnavailableException();
        var mappingByItem = mappings.ToDictionary(row => row.CashFlowItemId, row => row.ArticleOrdinal);
        var amounts = values.ToDictionary(row => (row.ArticleOrdinal, row.BudgetMonth), row => row.SignedAmountKzt);
        var lines = new List<BudgetActualsLineDto>();
        foreach (var itemId in selectedIds.Order())
            foreach (var month in months)
            {
                // No fact article for this selected UUID means an explicit zero in the draft.
                // An existing mapped article must still have a complete sealed source matrix.
                var amount = 0m;
                if (mappingByItem.TryGetValue(itemId, out var articleOrdinal)
                    && !amounts.TryGetValue((articleOrdinal, month), out amount))
                    throw new CashFlowActualsSourceInvalidException();
                if (Math.Abs(amount) > CashFlowActualsSource.MaximumSafeMoney) throw new CashFlowActualsSourceInvalidException();
                lines.Add(new BudgetActualsLineDto(itemId, month, Math.Abs(amount), amount > 0 ? "Receipt" : "Payment"));
            }
        return lines;
    }

    private async Task<CashFlowActualsImportResultDto> ResultAsync(CashFlowActualsSnapshot snapshot, bool alreadyImported, CancellationToken cancellationToken)
    {
        var unmapped = await db.CashFlowActualsArticles.Where(row => row.SnapshotId == snapshot.Id && row.CashFlowItemId == null)
            .OrderBy(row => row.ArticleOrdinal).Select(row => row.ArticleName).ToArrayAsync(cancellationToken);
        var months = await db.CashFlowActualsMonths.Where(row => row.SnapshotId == snapshot.Id).OrderBy(row => row.BudgetMonth)
            .Select(row => row.BudgetMonth).ToArrayAsync(cancellationToken);
        return new(snapshot.Id, snapshot.SourceHash, alreadyImported, snapshot.RowCount, snapshot.ArticleCount,
            unmapped.Length, unmapped, months, snapshot.ReceiptsAmountKzt, snapshot.PaymentsAmountKzt, snapshot.NetCashFlowAmountKzt);
    }

    private static string Normalize(string value) => Regex.Replace(value.Normalize(), @"\s+", " ").Trim().ToUpperInvariant();
    private void EnsureTransaction()
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Actuals require the business Serializable transaction.");
    }
}
