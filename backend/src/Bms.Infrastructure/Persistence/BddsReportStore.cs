using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Budgeting;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed partial class BddsReportStore(BmsDbContext db, IDirectDdsReplicaReader? directReplica = null) : IBddsReportStore
{
    private static readonly IReadOnlyDictionary<string, (string Code, string Name)> CostCenterMap =
        new Dictionary<string, (string Code, string Name)>(StringComparer.Ordinal)
        {
            ["Административный отдел"] = ("АДМ", "Администрация"),
            ["Департамент HR"] = ("HR", "HR департамент"),
            ["Департамент IT"] = ("IT", "IT департамент"),
            ["Коммерческий департамент"] = ("КД", "Коммерческий департамент"),
            ["Коммерческий департамент ВЭД"] = ("ВЭД", "Отдел внешне-экономической деятельности"),
            ["Отдел АХО"] = ("АХО", "Отдел АХО"),
            ["Отдел аналитики"] = ("ОА", "Отдел аналитики"),
            ["Отдел маркетинга"] = ("ДМ", "Департамент маркетинга"),
            ["Отдел продаж"] = ("ДП", "Департамент продаж"),
            ["Отдел строительства и реконструкции"] = ("ДР", "Департамент развития"),
            ["РЦ"] = ("РЦ", "РЦ"),
            ["Юридический отдел"] = ("ЮД", "Юридический отдел")
        };

    public async Task<BddsImportResultDto?> FindImportAsync(
        string sourceHash,
        CancellationToken cancellationToken)
    {
        EnsureSerializableTransaction();
        var snapshot = await db.BddsSnapshots.SingleOrDefaultAsync(
            row => row.SourceHash == sourceHash, cancellationToken);
        if (snapshot is null)
        {
            return null;
        }
        if (!snapshot.IsSealed)
        {
            throw new BddsImportConflictException("The existing BDDS snapshot is not sealed.");
        }

        var links = await db.BddsSnapshotBudgetInstances
            .Where(row => row.SnapshotId == snapshot.Id)
            .Select(row => row.BudgetInstanceId)
            .ToArrayAsync(cancellationToken);
        var approved = await db.BudgetInstances.CountAsync(
            row => links.Contains(row.Id) && row.Status == BudgetInstanceStatus.Approved,
            cancellationToken);
        if (links.Length != 12 || approved != links.Length)
        {
            throw new BddsImportConflictException("The existing BDDS snapshot is incomplete.");
        }

        var lineCount = await db.BudgetInstanceLines.CountAsync(
            row => links.Contains(row.BudgetInstanceId), cancellationToken);
        return new BddsImportResultDto(
            snapshot.Id,
            snapshot.SourceHash,
            AlreadyImported: true,
            snapshot.SourceRowCount,
            links.Length,
            lineCount,
            snapshot.ReceiptsAmountKzt,
            snapshot.PaymentsAmountKzt,
            snapshot.NetCashFlowAmountKzt,
            snapshot.ApprovedAtUtc);
    }

    public async Task<BddsImportResultDto> ImportAsync(
        ImportBddsSnapshotCommand command,
        Guid approvedByEmployeeId,
        string approvedByExternalId,
        DateTimeOffset approvedAtUtc,
        CancellationToken cancellationToken)
    {
        EnsureSerializableTransaction();
        if (approvedByEmployeeId == Guid.Empty || string.IsNullOrWhiteSpace(approvedByExternalId))
        {
            throw new BddsImportConflictException("The BDDS approving actor is invalid.");
        }

        var sourceCenters = command.Rows.Select(row => row.SourceCostCenterName)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!sourceCenters.SequenceEqual(CostCenterMap.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new BddsImportConflictException("The source CFO set differs from the approved mapping.");
        }

        var targetCodes = CostCenterMap.Values.Select(row => row.Code).ToArray();
        var existingCenters = await db.CostCenters
            .Where(row => targetCodes.Contains(row.Code)).ToArrayAsync(cancellationToken);
        var centersBySource = new Dictionary<string, CostCenterReference>(StringComparer.Ordinal);
        foreach (var sourceName in sourceCenters)
        {
            var mapping = CostCenterMap[sourceName];
            var matching = existingCenters.Where(row => row.Code == mapping.Code).ToArray();
            if (matching.Length > 1)
            {
                throw new BddsImportConflictException("The mapped CFO code is not unique.");
            }

            var center = matching.SingleOrDefault();
            if (center is null)
            {
                center = new CostCenterReference
                {
                    Id = Guid.NewGuid(),
                    Code = mapping.Code,
                    Name = mapping.Name,
                    ParentId = null,
                    IsGroup = false,
                    IsActive = true
                };
                db.CostCenters.Add(center);
            }
            else if (!string.Equals(center.Name, mapping.Name, StringComparison.Ordinal)
                     || center.ParentId.HasValue || center.IsGroup || !center.IsActive)
            {
                throw new BddsImportConflictException("A mapped CFO differs from the approved active standalone CFO.");
            }
            centersBySource[sourceName] = center;
        }

        var sourceArticles = command.Rows
            .Select(row => (row.ArticleBusinessCode, row.ArticleName))
            .Distinct().OrderBy(row => row.ArticleBusinessCode, StringComparer.Ordinal).ToArray();
        var cashFlowItems = await db.CashFlowItems.Where(row => row.IsActive && !row.IsGroup)
            .ToArrayAsync(cancellationToken);
        var itemsBySource = new Dictionary<(string Code, string Name), CashFlowItemReference>();
        foreach (var source in sourceArticles)
        {
            var matches = cashFlowItems.Where(item =>
                string.Equals(LeadingBusinessCode(item.Name), source.ArticleBusinessCode, StringComparison.Ordinal)
                && string.Equals(
                    NormalizeArticleName(item.Name, source.ArticleBusinessCode),
                    NormalizeArticleName(source.ArticleName, source.ArticleBusinessCode),
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
            {
                throw new BddsImportConflictException(
                    $"BDDS article mapping is not unique for business code {source.ArticleBusinessCode}.");
            }
            itemsBySource[source] = matches[0];
        }
        if (itemsBySource.Count != 157)
        {
            throw new BddsImportConflictException("The mapped BDDS article count differs from 157.");
        }

        if (db.ChangeTracker.Entries<CostCenterReference>()
            .Any(entry => entry.State == EntityState.Added))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var centerIds = centersBySource.Values.Select(row => row.Id).ToArray();
        foreach (var centerId in centerIds.Order())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [Id] FROM [bms_web].[CostCenters] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {centerId};",
                cancellationToken);
        }
        var overlapping = await db.BudgetInstances.AsNoTracking().AnyAsync(instance =>
            centerIds.Contains(instance.CostCenterId)
            && instance.ValidFrom <= new DateOnly(2026, 12, 31)
            && instance.ValidTo >= new DateOnly(2026, 1, 1)
            && !db.BddsSnapshotBudgetInstances.Any(link => link.BudgetInstanceId == instance.Id),
            cancellationToken);
        if (overlapping)
        {
            throw new BddsImportConflictException("A target CFO already has a 2026 budget instance.");
        }

        var receipts = command.Rows.Where(row => row.SignedAmountKzt > 0m)
            .Sum(row => row.SignedAmountKzt);
        var payments = -command.Rows.Where(row => row.SignedAmountKzt < 0m)
            .Sum(row => row.SignedAmountKzt);
        var nonZeroCellCount = command.Rows
            .GroupBy(row => (row.SourceCostCenterName, row.ArticleBusinessCode,
                row.ArticleName, row.BudgetMonth))
            .Count(group => group.Sum(row => row.SignedAmountKzt) != 0m);
        var snapshot = new BddsSnapshot
        {
            Id = Guid.NewGuid(),
            SourceVersionDate = command.SourceVersionDate,
            Scenario = command.Scenario,
            BudgetYear = command.BudgetYear,
            SourceSystem = "Financial reporting portal",
            SourceTable = "Data.fin_budget_cashflow_monthly",
            SourceHash = command.SourceHash.ToUpperInvariant(),
            SourceRowCount = command.Rows.Count,
            CostCenterCount = sourceCenters.Length,
            ArticleCount = sourceArticles.Length,
            NonZeroCellCount = nonZeroCellCount,
            ReceiptsAmountKzt = receipts,
            PaymentsAmountKzt = payments,
            NetCashFlowAmountKzt = receipts - payments,
            CreatedAtUtc = approvedAtUtc,
            ApprovedAtUtc = approvedAtUtc,
            ApprovedByEmployeeId = approvedByEmployeeId,
            ApprovedByExternalId = approvedByExternalId,
            IsSealed = false
        };
        db.BddsSnapshots.Add(snapshot);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var row in command.Rows.OrderBy(value => value.SourceOrdinal))
        {
            var item = itemsBySource[(row.ArticleBusinessCode, row.ArticleName)];
            db.BddsSnapshotRows.Add(new BddsSnapshotRow
            {
                SnapshotId = snapshot.Id,
                SourceOrdinal = row.SourceOrdinal,
                SourceCostCenterName = row.SourceCostCenterName,
                MappedCostCenterId = centersBySource[row.SourceCostCenterName].Id,
                ActivityName = row.ActivityName,
                SectionName = row.SectionName,
                GroupName = row.GroupName,
                ArticleBusinessCode = row.ArticleBusinessCode,
                ArticleName = NormalizeArticleName(row.ArticleName, row.ArticleBusinessCode),
                CashFlowItemId = item.Id,
                BudgetMonth = row.BudgetMonth,
                SignedAmountKzt = row.SignedAmountKzt,
                SourceFile = row.SourceFile,
                SourceSheet = row.SourceSheet,
                SourceRowNumber = row.SourceRowNumber,
                SourceLoadedAtUtc = row.SourceLoadedAtUtc,
                RowHash = BddsCanonicalHash.Compute([row])
            });
        }
        await db.SaveChangesAsync(cancellationToken);

        var aggregateLines = command.Rows.Where(row => row.SignedAmountKzt != 0m)
            .GroupBy(row => new
            {
                CostCenterId = centersBySource[row.SourceCostCenterName].Id,
                CashFlowItemId = itemsBySource[(row.ArticleBusinessCode, row.ArticleName)].Id,
                row.BudgetMonth,
                Direction = row.SignedAmountKzt > 0m
                    ? BudgetFlowDirection.Receipt
                    : BudgetFlowDirection.Payment
            })
            .Select(group => new
            {
                group.Key.CostCenterId,
                group.Key.CashFlowItemId,
                group.Key.BudgetMonth,
                group.Key.Direction,
                Amount = group.Key.Direction == BudgetFlowDirection.Receipt
                    ? group.Sum(row => row.SignedAmountKzt)
                    : -group.Sum(row => row.SignedAmountKzt)
            })
            .Where(row => row.Amount != 0m)
            .ToArray();
        if (aggregateLines.Length < 1 || aggregateLines.Any(row => row.Amount <= 0m))
        {
            throw new BddsImportConflictException("The imported BDDS budget-line set is invalid.");
        }

        var instances = new List<BudgetInstance>(12);
        var links = new List<BddsSnapshotBudgetInstance>(12);
        foreach (var centerPair in centersBySource.OrderBy(row => row.Value.Code, StringComparer.Ordinal))
        {
            var center = centerPair.Value;
            var drafts = aggregateLines.Where(row => row.CostCenterId == center.Id)
                .OrderBy(row => row.BudgetMonth)
                .ThenBy(row => row.CashFlowItemId)
                .ThenBy(row => row.Direction)
                .Select(row => new BudgetInstanceLineDraft(
                    row.CashFlowItemId,
                    row.BudgetMonth,
                    row.Amount,
                    Comment: null,
                    row.Direction))
                .ToArray();
            if (drafts.Length == 0)
            {
                throw new BddsImportConflictException("Every mapped CFO must have at least one BDDS budget line.");
            }

            // All CFO anchors were acquired in stable order before this loop.
            // The shared allocator re-enters each exact lock, but does not use
            // the single-CFO directory store or the source-version date.
            var number = await BudgetInstanceNumberAllocation.AllocateLockedAsync(db, center.Id,
                BudgetDocumentNumber.CreationDate(snapshot.CreatedAtUtc), cancellationToken);
            var instance = BudgetInstance.CreateDraft(
                number,
                center.Id,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 12, 31),
                approvedByEmployeeId,
                approvedByExternalId,
                drafts);
            var ordinal = (await db.BudgetInstances.AsNoTracking()
                .Where(row => row.CostCenterId == center.Id && row.Status == BudgetInstanceStatus.Approved)
                .MaxAsync(row => (long?)row.ApprovalOrdinal, cancellationToken) ?? 0L) + 1L;
            instance.Approve(
                instance.ConcurrencyStamp,
                ordinal,
                approvedAtUtc,
                approvedByEmployeeId,
                approvedByExternalId);
            db.BudgetInstances.Add(instance);
            db.CompanyBudgetInstanceCreations.Add(new() { BudgetInstanceId = instance.Id,
                CreatedAtUtc = snapshot.CreatedAtUtc, Source = "Imported" });
            links.Add(new BddsSnapshotBudgetInstance
            {
                SnapshotId = snapshot.Id,
                BudgetInstanceId = instance.Id,
                CostCenterId = center.Id,
                SourceCostCenterName = centerPair.Key
            });
            instances.Add(instance);
        }

        await db.SaveChangesAsync(cancellationToken);
        db.BddsSnapshotBudgetInstances.AddRange(links);
        await db.SaveChangesAsync(cancellationToken);
        snapshot.IsSealed = true;
        await db.SaveChangesAsync(cancellationToken);
        return new BddsImportResultDto(
            snapshot.Id,
            snapshot.SourceHash,
            AlreadyImported: false,
            snapshot.SourceRowCount,
            instances.Count,
            aggregateLines.Length,
            snapshot.ReceiptsAmountKzt,
            snapshot.PaymentsAmountKzt,
            snapshot.NetCashFlowAmountKzt,
            snapshot.ApprovedAtUtc);
    }

    public async Task<BddsReportDto?> ReadAsync(
        int year,
        Guid? costCenterId,
        Guid employeeId,
        bool canSeeAll,
        CancellationToken cancellationToken) => await ReadPeriodAsync(year, costCenterId, employeeId,
            canSeeAll, BddsReportOptions.Annual(year), cancellationToken);

    public async Task<BddsReportDto?> ReadPeriodAsync(int year, Guid? costCenterId,
        Guid employeeId, bool canSeeAll, BddsReportOptions options, CancellationToken cancellationToken)
    {
        EnsureSerializableTransaction();
        options.Months(year);
        var active = await CompanyBudgetPlanReader.ActiveAsync(db, cancellationToken);
        var primaryId = options.VersionId ?? active.VersionId;
        if (options.CompareVersionId == primaryId)
            throw new DomainRuleViolationException("Comparison requires two different versions.");
        var snapshot = await db.BddsSnapshots.AsNoTracking()
            .Where(row => row.IsSealed && row.BudgetYear == year
                && db.BddsSnapshotBudgetInstances.Any(link => link.SnapshotId == row.Id)
                && db.BddsSnapshotBudgetInstances.Where(link => link.SnapshotId == row.Id)
                    .All(link => db.BudgetInstances.Any(instance => instance.Id == link.BudgetInstanceId
                        && instance.Status == BudgetInstanceStatus.Approved)))
            .OrderByDescending(row => row.ApprovedAtUtc)
            .ThenByDescending(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        // Current article assignments may target a CFO without imported plan.
        // Authorize that CFO independently. Full roles also retain inactive
        // linked history and approved budgets without an imported CFO link.
        var snapshotId = snapshot?.Id ?? Guid.Empty;
        var candidateCenters = db.CostCenters.AsNoTracking().Where(center => !center.IsGroup &&
            (center.IsActive || db.BddsSnapshotBudgetInstances.Any(link =>
                link.SnapshotId == snapshotId && link.CostCenterId == center.Id)
            || (canSeeAll && db.BudgetInstances.Any(instance => instance.CostCenterId == center.Id
                && db.CompanyBudgetVersionInstances.Any(member => member.BudgetInstanceId == instance.Id
                    && (member.VersionId == primaryId || member.VersionId == options.CompareVersionId))))));
        var accessibleIds = canSeeAll
            ? candidateCenters.Select(row => row.Id)
            : candidateCenters.Where(center => BusinessDataReadScopeQuery.OwnedCostCenterIds(db, employeeId)
                .Contains(center.Id)).Select(row => row.Id);
        var ids = await accessibleIds.Distinct().ToArrayAsync(cancellationToken);
        if (ids.Length == 0)
        {
            throw new ActorForbiddenException();
        }
        if (costCenterId.HasValue && !ids.Contains(costCenterId.Value))
        {
            throw new ActorForbiddenException();
        }

        var centers = await db.CostCenters.AsNoTracking().Where(row => ids.Contains(row.Id))
            .OrderBy(row => row.Code)
            .Select(row => new BddsCostCenterDto(row.Id, row.Code, row.Name))
            .ToArrayAsync(cancellationToken);
        var selectedIds = costCenterId.HasValue ? new[] { costCenterId.Value } : ids;
        var templates = await db.BddsSnapshotRows.AsNoTracking()
            .Where(row => row.SnapshotId == snapshotId && selectedIds.Contains(row.MappedCostCenterId))
            .OrderBy(row => row.SourceOrdinal)
            .ToArrayAsync(cancellationToken);
        var primary = await CompanyBudgetPlanReader.ReadAsync(db, primaryId, selectedIds, cancellationToken);
        var comparison = options.CompareVersionId.HasValue
            ? await CompanyBudgetPlanReader.ReadAsync(db, options.CompareVersionId, selectedIds, cancellationToken) : null;
        // An explicit inaccessible composition is indistinguishable from a
        // missing one. The default active source may legitimately cover no CFO.
        if (!canSeeAll)
        {
            async Task EnsureVisibleAsync(Guid id)
            {
                if (!await db.CompanyBudgetVersionInstances.AnyAsync(member => member.VersionId == id
                    && db.BudgetInstances.Any(instance => instance.Id == member.BudgetInstanceId
                        && ids.Contains(instance.CostCenterId)), cancellationToken))
                    throw new CompanyBudgetVersionNotFoundException();
            }
            if (options.VersionId.HasValue) await EnsureVisibleAsync(primaryId);
            if (options.CompareVersionId.HasValue) await EnsureVisibleAsync(options.CompareVersionId.Value);
        }
        var projections = await ReadEffectivePlansAsync(options, selectedIds, primary, comparison, templates, cancellationToken);
        var sourceRows = projections.Primary;
        // Refresh labels by permanent identity, only for the authorized plan.
        // Inactive references still have a name; missing references must not
        // remove historical financial rows. The sealed source is never updated.
        var planArticleIds = sourceRows.Select(row => row.CashFlowItemId).Distinct().ToArray();
        var currentArticleNames = await db.CashFlowItems.AsNoTracking()
            .Where(item => planArticleIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        var rows = BuildReportRows(sourceRows);
        var receipts = sourceRows.Where(row => row.SignedAmountKzt > 0m).Sum(row => row.SignedAmountKzt);
        var payments = -sourceRows.Where(row => row.SignedAmountKzt < 0m).Sum(row => row.SignedAmountKzt);
        BddsCompanyVersionDto VersionMetadata(CompanyBudgetPlanSnapshot plan) => new(plan.Version.Id, plan.Version.Name,
            plan.Version.ConcurrencyStamp, plan.Version.Id == active.VersionId, plan.Version.PublishedAtUtc.HasValue);
        var metadata = new BddsReportDto(
            snapshot?.Id ?? primary.Version.Id,
            snapshot?.SourceVersionDate ?? DateOnly.FromDateTime(primary.Version.CreatedAtUtc.UtcDateTime),
            snapshot?.Scenario ?? "Версия бюджета",
            year,
            costCenterId,
            centers,
            rows,
            receipts,
            payments,
            receipts - payments,
            snapshot?.ApprovedAtUtc ?? primary.Version.PublishedAtUtc ?? primary.Version.CreatedAtUtc)
            { PlanSource = "COMPANY_BUDGET_VERSION", PlanVersion = VersionMetadata(primary) };
        var actuals = options.IncludeActuals
            ? await ReadActualsAsync(options.ActualsYear, sourceRows.Select(row => row.CashFlowItemId).ToHashSet(),
                canSeeAll && costCenterId is null ? null : selectedIds, cancellationToken) : null;
        // Assignment labels belong to the current reference item, not its
        // historical document CFO. Only already-visible permanent identities
        // are queried, inside the same Serializable report transaction.
        var visibleArticleIds = planArticleIds
            .Concat(projections.Comparison?.Select(row => row.CashFlowItemId) ?? [])
            .Concat(actuals?.Articles.Select(row => row.ItemId) ?? [])
            .Distinct().ToArray();
        // One fresh identity map for A, B and scoped actuals. Do not filter
        // inactive historical references or infer UUIDs from names/codes.
        var hierarchyItems = await db.CashFlowItems.AsNoTracking()
            .Where(item => visibleArticleIds.Contains(item.Id)).ToArrayAsync(cancellationToken);
        if (hierarchyItems.Length != visibleArticleIds.Length)
            throw new CashFlowActualsMappingUnavailableException();
        var hierarchyParentIds = hierarchyItems.Where(item => item.ParentId.HasValue)
            .Select(item => item.ParentId!.Value).Distinct().ToArray();
        var hierarchyGroups = await db.CashFlowItems.AsNoTracking()
            .Where(group => hierarchyParentIds.Contains(group.Id) && group.IsGroup)
            .ToDictionaryAsync(group => group.Id, cancellationToken);
        var displayOrders = await new CashFlowCatalogOrderStore(db)
            .ReadDisplayOrdersAsync(visibleArticleIds, cancellationToken);
        var catalogue = await new CashFlowCatalogOrderStore(db).ReadAsync(false, cancellationToken);
        var displayBlocks = BddsCatalogueDisplay.Blocks(catalogue.Rows);
        var cataloguePositions = Bms.Application.ReferenceData.CashFlowCatalogOrderService.OrderRowsForDisplay(catalogue.Rows)
            .Select((row, index) => (row.Id, Position: (int?)index)).ToDictionary(row => row.Id, row => row.Position);
        var hierarchy = hierarchyItems.ToDictionary(item => item.Id, item =>
        {
            var parent = item.ParentId is { } id && hierarchyGroups.TryGetValue(id, out var group) ? group : null;
            return new BddsHierarchyItem(item.ActivityType, parent?.Id, parent?.Name ?? string.Empty,
                displayOrders[item.Id], displayBlocks.GetValueOrDefault(item.Id), cataloguePositions.GetValueOrDefault(item.Id),
                item.FlowDirection);
        });
        var currentArticleCostCenterCodes = await db.CashFlowItems.AsNoTracking()
            .Where(item => visibleArticleIds.Contains(item.Id))
            .Join(db.CostCenters.AsNoTracking(), item => item.CostCenterId, center => (Guid?)center.Id,
                (item, center) => new { item.Id, center.Code })
            .ToDictionaryAsync(row => row.Id, row => row.Code, cancellationToken);
        BddsPlanCell[] Cells(BddsSnapshotRow[] source) => source.Select(row => new BddsPlanCell(row.CashFlowItemId,
                row.ActivityName, row.SectionName, row.GroupName, row.ArticleBusinessCode, row.ArticleName,
                row.BudgetMonth, row.SignedAmountKzt, row.SourceOrdinal)).ToArray();
        var report = BddsComparison.Build(metadata, Cells(sourceRows), options,
            true, actuals, currentArticleNames, currentArticleCostCenterCodes, hierarchy);
        if (comparison is null || projections.Comparison is null) return report;
        var compareReport = BddsComparison.Build(metadata with { PlanVersion = VersionMetadata(comparison) },
            Cells(projections.Comparison), options with { IncludeActuals = false }, true,
            null, currentArticleNames, currentArticleCostCenterCodes, hierarchy);
        return BddsVersionComparison.Attach(report, compareReport);
    }

    private async Task<BddsActualsData?> ReadActualsAsync(int actualsYear, IReadOnlySet<Guid> planIds,
        Guid[]? permittedCostCenterIds, CancellationToken cancellationToken)
    {
        ActualsSnapshotReadMetadata snapshot;
        DateOnly[] months;
        CashFlowActualsArticle[] articles;
        CashFlowActualsRow[] cells;
        if (AccountingPeriod467.IsSourceYear(actualsYear))
        {
            var current = await CurrentDdsActualsSource.ReadRequiredAsync(
                directReplica ?? new DirectDdsReplicaReader(db), cancellationToken);
            if (!current.Months.Any(m => m.Year == actualsYear)) return null;
            snapshot = ActualsSnapshotReadMetadata.FromDirect(current, actualsYear);
            months = current.Months.Where(m => m.Year == actualsYear).ToArray();
            articles = current.NetArticles();
            cells = current.NetRows(actualsYear);
        }
        else
        {
            var legacy = await db.CashFlowActualsSnapshots.AsNoTracking()
                .Where(row => row.IsSealed && row.SourceYear == actualsYear)
                .OrderByDescending(row => row.ImportedAtUtc).ThenByDescending(row => row.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (legacy is null) return null;
            snapshot = ActualsSnapshotReadMetadata.FromLegacy(legacy);
            months = await db.CashFlowActualsMonths.AsNoTracking().Where(row => row.SnapshotId == snapshot.Id)
                .OrderBy(row => row.BudgetMonth).Select(row => row.BudgetMonth).ToArrayAsync(cancellationToken);
            articles = await db.CashFlowActualsArticles.AsNoTracking().Where(row => row.SnapshotId == snapshot.Id)
                .OrderBy(row => row.ArticleOrdinal).ToArrayAsync(cancellationToken);
            cells = await db.CashFlowActualsRows.AsNoTracking().Where(row => row.SnapshotId == snapshot.Id)
                .OrderBy(row => row.SourceOrdinal).ToArrayAsync(cancellationToken);
        }
        if (articles.Length != snapshot.ArticleCount || cells.Length != snapshot.RowCount
            || articles.Length is < 1 or > 2000 || cells.Length > 24000
            || articles.Any(row => !row.CashFlowItemId.HasValue || CashFlowActualsSource.IsExcludedArticle(row.ArticleName))
            || articles.Select(row => row.CashFlowItemId).Distinct().Count() != articles.Length)
            throw new CashFlowActualsMappingUnavailableException();
        var byOrdinal = articles.ToDictionary(row => row.ArticleOrdinal);
        if (cells.Any(row => !byOrdinal.ContainsKey(row.ArticleOrdinal))) throw new CashFlowActualsSourceInvalidException();
        CashFlowActualsSource.Validate(new(snapshot.SourceHash, snapshot.SourceGeneration, snapshot.CapturedAtUtc,
            months, articles.Select(row => row.ArticleName).ToArray(),
            cells.Select(row => new CashFlowActualsSourceRow(row.SourceOrdinal, byOrdinal[row.ArticleOrdinal].ArticleName,
                row.BudgetMonth, row.SignedAmountKzt)).ToArray(), snapshot.ReceiptsAmountKzt,
            snapshot.PaymentsAmountKzt, snapshot.NetCashFlowAmountKzt));
        var ids = articles.Select(row => row.CashFlowItemId ?? throw new CashFlowActualsMappingUnavailableException()).ToArray();
        var items = await db.CashFlowItems.AsNoTracking().Where(row => ids.Contains(row.Id)).ToArrayAsync(cancellationToken);
        if (items.Length != ids.Length) throw new CashFlowActualsMappingUnavailableException();
        // Validate the whole sealed source before scoping. Current reference
        // classification is needed only for articles visible in this report.
        var permitted = permittedCostCenterIds?.ToHashSet();
        if (permitted is not null) items = items.Where(item => item.CostCenterId is { } center
            && permitted.Contains(center)).ToArray();
        var visibleIds = items.Select(item => item.Id).ToHashSet();
        articles = articles.Where(row => row.CashFlowItemId is { } id && visibleIds.Contains(id)).ToArray();
        cells = cells.Where(row => byOrdinal[row.ArticleOrdinal].CashFlowItemId is { } id && visibleIds.Contains(id)).ToArray();
        var parentIds = items.Where(row => row.ParentId.HasValue).Select(row => row.ParentId.GetValueOrDefault()).Distinct().ToArray();
        var groups = await db.CashFlowItems.AsNoTracking().Where(row => parentIds.Contains(row.Id) && row.IsGroup)
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        if (items.Any(row => row.IsGroup && !planIds.Contains(row.Id)))
            throw new CashFlowActualsMappingUnavailableException();
        for (var depth = 0; depth < 32; depth++)
        {
            var missing = groups.Values.Where(group => group.ParentId.HasValue && !groups.ContainsKey(group.ParentId.Value))
                .Select(group => group.ParentId.GetValueOrDefault()).Distinct().ToArray();
            if (missing.Length == 0) break;
            var ancestors = await db.CashFlowItems.AsNoTracking().Where(row => missing.Contains(row.Id) && row.IsGroup)
                .ToArrayAsync(cancellationToken);
            if (ancestors.Length != missing.Length) break;
            foreach (var group in ancestors) groups.Add(group.Id, group);
        }
        foreach (var item in items.Where(item => !planIds.Contains(item.Id)))
        {
            var parent = item.ParentId ?? throw new CashFlowActualsMappingUnavailableException();
            var visited = new HashSet<Guid> { item.Id };
            for (Guid? current = parent; current.HasValue; current = groups[current.Value].ParentId)
                if (!visited.Add(current.Value) || visited.Count > 33 || !groups.TryGetValue(current.Value, out var group)
                    || string.IsNullOrWhiteSpace(group.Name))
                    throw new CashFlowActualsMappingUnavailableException();
        }
        var byId = items.ToDictionary(row => row.Id);
        string Activity(string value) => value switch
        {
            "Operating" => "ОПЕРАЦИОННАЯ ДЕЯТЕЛЬНОСТЬ",
            "Investing" => "ИНВЕСТИЦИОННАЯ ДЕЯТЕЛЬНОСТЬ",
            "Financing" => "ФИНАНСОВАЯ ДЕЯТЕЛЬНОСТЬ",
            _ => throw new CashFlowActualsMappingUnavailableException()
        };
        var verified = new BddsActualsData(snapshot.Id, snapshot.CapturedAtUtc, months, articles.Select(row =>
        {
            var item = byId[row.CashFlowItemId ?? throw new CashFlowActualsMappingUnavailableException()];
            var knownGroup = item.ParentId.HasValue && groups.TryGetValue(item.ParentId.Value, out var group)
                ? group : null;
            // These labels are compatibility scaffolding only. The final typed
            // report resolves every article through the fresh catalogue map.
            return new BddsActualArticle(item.Id, string.IsNullOrWhiteSpace(item.Name) ? row.ArticleName : item.Name,
                planIds.Contains(item.Id) ? string.Empty : Activity(item.ActivityType),
                knownGroup?.Id ?? Guid.Empty, knownGroup?.Name ?? string.Empty, item.CostCenterId);
        }).ToArray(), cells.Select(row => new BddsActualCell(byOrdinal[row.ArticleOrdinal].CashFlowItemId
            ?? throw new CashFlowActualsMappingUnavailableException(),
            row.BudgetMonth, row.SignedAmountKzt)).ToArray(), snapshot.SourceYear);
        return BddsComparison.ScopeActuals(verified, permittedCostCenterIds?.ToHashSet());
    }

    private static IReadOnlyList<BddsReportRowDto> BuildReportRows(IReadOnlyList<BddsSnapshotRow> sourceRows)
    {
        var rows = new Dictionary<string, MutableReportRow>(StringComparer.Ordinal);
        void Add(string key, string? parentKey, string kind, string name, BddsSnapshotRow source)
        {
            if (!rows.TryGetValue(key, out var row))
            {
                row = new MutableReportRow(key, parentKey, kind, DisplayName(name), source.SourceOrdinal);
                rows.Add(key, row);
            }
            row.Amounts[source.BudgetMonth.Month - 1] += source.SignedAmountKzt;
        }

        foreach (var source in sourceRows)
        {
            var activityKey = $"activity|{source.ActivityName}";
            var sectionKey = $"{activityKey}|section|{source.SectionName}";
            var groupKey = $"{sectionKey}|group|{source.GroupName}";
            var articleKey = $"{groupKey}|article|{source.ArticleBusinessCode}|{source.ArticleName}";
            Add(activityKey, null, "Activity", source.ActivityName, source);
            Add(sectionKey, activityKey, "Section", source.SectionName, source);
            Add(groupKey, sectionKey, "Group", source.GroupName, source);
            Add(articleKey, groupKey, "Article",
                $"{source.ArticleBusinessCode} {source.ArticleName}", source);
        }

        var result = rows.Values.OrderBy(row => row.SortOrder).ThenBy(row => row.KindOrder)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Select(row => row.ToDto()).ToList();
        var net = new decimal[12];
        foreach (var source in sourceRows)
        {
            net[source.BudgetMonth.Month - 1] += source.SignedAmountKzt;
        }
        result.Add(new BddsReportRowDto("net", null, "Net", "Чистый денежный поток", net, net.Sum()));
        return result;
    }

    private static string LeadingBusinessCode(string value)
    {
        var match = BusinessCodeRegex().Match(value.Trim());
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string NormalizeArticleName(string value, string code)
    {
        var normalized = Regex.Replace(value.Trim().Normalize(NormalizationForm.FormC), @"\s+", " ");
        if (normalized.StartsWith(code, StringComparison.Ordinal))
        {
            normalized = normalized[code.Length..].TrimStart(' ', '-', '—', '.', ':');
        }
        return normalized.Trim();
    }

    private static string DisplayName(string value)
    {
        var trimmed = value.Trim();
        var match = HierarchyPrefixRegex().Match(trimmed);
        return match.Success ? match.Groups[1].Value.Trim() : trimmed;
    }

    private void EnsureSerializableTransaction()
    {
        var transaction = db.Database.CurrentTransaction;
        if (transaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
        {
            throw new InvalidOperationException("BDDS storage requires a Serializable business transaction.");
        }
    }

    private sealed class MutableReportRow(
        string key,
        string? parentKey,
        string kind,
        string name,
        int sortOrder)
    {
        public string Key { get; } = key;
        public string? ParentKey { get; } = parentKey;
        public string Kind { get; } = kind;
        public string Name { get; } = name;
        public int SortOrder { get; } = sortOrder;
        public int KindOrder => Kind switch { "Activity" => 0, "Section" => 1, "Group" => 2, _ => 3 };
        public decimal[] Amounts { get; } = new decimal[12];
        public BddsReportRowDto ToDto() => new(Key, ParentKey, Kind, Name, Amounts, Amounts.Sum());
    }

    [GeneratedRegex(@"^(\d{6})(?:\s|[-—.:])")]
    private static partial Regex BusinessCodeRegex();

    [GeneratedRegex(@"^\d+(?:\.\d+)*\.\s*(.+)$")]
    private static partial Regex HierarchyPrefixRegex();
}
