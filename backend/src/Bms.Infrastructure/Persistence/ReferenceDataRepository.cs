using System.Data;
using Bms.Application.Abstractions;
using Bms.Application.ReferenceData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class ReferenceDataRepository(
    BmsDbContext dbContext,
    ICashFlowCatalogOrderStore cashFlowOrder) : IReferenceDataRepository
{
    public ReferenceDataRepository(BmsDbContext dbContext)
        : this(dbContext, new CashFlowCatalogOrderStore(dbContext))
    {
    }

    public async Task<IReadOnlyList<ReferenceDataItemDto>> ListOrganizationsAsync(
        string actorExternalId,
        bool bypassActorScope,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        ValidateActor(actorExternalId);

        var organizations = dbContext.Organizations.AsNoTracking();

        if (!includeInactive)
        {
            organizations = organizations.Where(organization => organization.IsActive);
        }

        // Organizations are a shared group reference catalog, not document
        // visibility or write authority. An active employee with a current
        // assigned BMS role sees every active organization without individual
        // ActorOrganizationGrants. Keep identity/role revocation fail-closed;
        // CFO, NSI queue, document and mutation scopes are unchanged.
        if (!bypassActorScope)
        {
            var employees = BusinessDataReadScopeQuery.EmployeeIds(dbContext, actorExternalId);
            var roles = BusinessDataReadScopeQuery.RoleCodes(dbContext, employees);
            if (!await employees.AnyAsync(_ => roles.Any(), cancellationToken)) return [];
        }

        return await organizations
            .OrderBy(organization => organization.Code)
            .ThenBy(organization => organization.Name)
            .Select(organization => new ReferenceDataItemDto(
                organization.Id,
                organization.Code,
                organization.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceDataItemDto>> ListCostCentersAsync(
        string actorExternalId,
        bool bypassActorScope,
        bool includeInactive,
        bool includeGroups,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        ValidateActor(actorExternalId);

        var costCenters = dbContext.CostCenters.AsNoTracking();

        if (!includeInactive)
        {
            costCenters = costCenters.Where(costCenter => costCenter.IsActive);
        }

        if (!includeGroups)
        {
            costCenters = costCenters.Where(costCenter => !costCenter.IsGroup);
        }

        // Inactive/group catalog maintenance is separately authorized by the
        // application. Ordinary business pickers use the same canonical scope
        // as documents, never legacy dated grants or administrator flags.
        if (!includeInactive && !includeGroups)
        {
            costCenters = costCenters.Where(center => !dbContext.CostCenterPurposes457
                .Any(s => s.CostCenterId == center.Id && s.OnlyCostDepartment));
            var visible = BusinessDataReadScopeQuery.VisibleCostCenterIds(dbContext, actorExternalId);
            var nsiReferences = NsiQueuedCostCenterReferenceIds373(actorExternalId);
            costCenters = costCenters.Where(center => visible.Contains(center.Id) || nsiReferences.Contains(center.Id));
        }
        else if (!bypassActorScope)
        {
            var visible = BusinessDataReadScopeQuery.VisibleCostCenterIds(dbContext, actorExternalId);
            costCenters = costCenters.Where(center => visible.Contains(center.Id));
        }

        var rows = await costCenters
            .OrderBy(costCenter => costCenter.Code)
            .ThenBy(costCenter => costCenter.Name)
            .Select(costCenter => new ReferenceDataItemDto(
                costCenter.Id,
                costCenter.Code,
                costCenter.Name,
                costCenter.ParentId, null, null, null, null, null, costCenter.GroupingName, null))
            .ToListAsync(cancellationToken);
        var visibleIds = rows.Select(c => c.Id).ToArray();
        var owners = await (from a in dbContext.BudgetOwnerCostCenterAssignments.AsNoTracking()
            join e in dbContext.Employees.AsNoTracking() on a.EmployeeId equals e.Id
            where a.IsActive && e.IsActive && visibleIds.Contains(a.CostCenterId)
                && dbContext.EmployeeFunctionalRoles.Any(er => er.EmployeeId == e.Id && er.IsActive
                    && dbContext.FunctionalRoles.Any(r => r.Id == er.FunctionalRoleId && r.IsActive
                        && r.Code == Bms.Domain.Workflow.ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode))
            orderby e.Name, e.Id
            select new { a.CostCenterId, e.Name }).ToArrayAsync(cancellationToken);
        return rows.Select(c => c with { OwnerNames = owners.Where(a => a.CostCenterId == c.Id)
            .Select(a => a.Name).ToArray() }).ToArray();
    }

    // Reference labels are not budget/document authority. NSI must be able to
    // display the header and line CFOs of its organization-bound active queue,
    // even though that role deliberately owns no budget CFOs. Keep this union
    // local to the ordinary catalogue; never add it to VisibleCostCenterIds or
    // grant access to budgets, other stages/organizations, or document edits.
    internal IQueryable<Guid> NsiQueuedCostCenterReferenceIds373(string actorExternalId)
    {
        var organizations = BusinessDataReadScopeQuery.NsiOrganizationIds(dbContext, actorExternalId);
        var queue = dbContext.ExpenseRequests.AsNoTracking().Where(request =>
            request.Status == Bms.Domain.ExpenseRequests.ExpenseRequestStatus.AwaitingNsi
            && request.OrganizationId.HasValue && organizations.Contains(request.OrganizationId.Value));
        var headers = queue.Where(request => request.DefaultCostCenterId.HasValue)
            .Select(request => request.DefaultCostCenterId!.Value);
        var lines = queue.SelectMany(request => request.Lines)
            .Where(line => line.CostCenterId.HasValue).Select(line => line.CostCenterId!.Value);
        return headers.Union(lines);
    }

    public async Task<IReadOnlyList<ReferenceDataItemDto>> ListCashFlowItemsAsync(
        bool includeInactive,
        bool includeGroups,
        CancellationToken cancellationToken)
    {
        var items = dbContext.CashFlowItems.AsNoTracking();

        if (!includeInactive)
        {
            items = items.Where(item => item.IsActive);
        }

        if (!includeGroups)
        {
            items = items.Where(item => !item.IsGroup);
        }

        var rows = await WithPublicParentNames(items).ToListAsync(cancellationToken);
        return await AttachDisplayOrdersAsync(rows, cancellationToken);
    }

    // Related public labels belong to the ordinary leaf read contract.
    // Group maintenance, internal codes, and financial amounts remain separate.
    internal IQueryable<ReferenceDataItemDto> WithPublicParentNames(IQueryable<CashFlowItemReference> items) =>
        from item in items
        join parent in dbContext.CashFlowItems.AsNoTracking().Where(parentGroup => parentGroup.IsGroup)
            on item.ParentId equals (Guid?)parent.Id into parents
        from parent in parents.DefaultIfEmpty()
        orderby item.Code, item.Name, item.Id
        select new ReferenceDataItemDto(item.Id, item.Code, item.Name, item.ParentId,
            null, null, item.ActivityType, item.CostCenterId, parent == null ? null : parent.Name,
            null, null, item.FlowDirection);

    public async Task<IReadOnlyList<ReferenceDataItemDto>> ListCashFlowItemGroupsAsync(
        CancellationToken cancellationToken)
    {
        return await dbContext.CashFlowItems
            .AsNoTracking()
            .Where(item => item.IsActive && item.IsGroup)
            .OrderBy(item => item.Code)
            .ThenBy(item => item.Name)
            .Select(item => new ReferenceDataItemDto(
                item.Id, item.Code, item.Name, item.ParentId, null, null, item.ActivityType))
            .ToListAsync(cancellationToken);
    }

    public async Task<ReferenceDataItemWindow> QueryCashFlowItemsAsync(
        string? search,
        int skip,
        int take,
        bool includeInactive,
        bool includeGroups,
        CancellationToken cancellationToken)
    {
        return await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            // Count and page form one public envelope. Serializable keeps a
            // concurrent catalogue import from inserting or removing a match
            // between those two commands. The execution strategy owns the
            // whole transaction so a transient provider failure retries a
            // fresh snapshot rather than one command in isolation.
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                token);
            var items = dbContext.CashFlowItems.AsNoTracking();

            if (!includeInactive)
            {
                items = items.Where(item => item.IsActive);
            }

            if (!includeGroups)
            {
                items = items.Where(item => !item.IsGroup);
            }

            if (!string.IsNullOrEmpty(search))
            {
                // The term is a value. EF sends it as a parameter, so a caller
                // cannot make it part of the command, and the comparison it drives
                // treats a wildcard as one more character to look for.
                items = items.Where(item =>
                    item.Code.Contains(search) || item.Name.Contains(search));
            }

            // The count is asked of the database over the same filter. Counting
            // the page would answer a different question, and counting a fetched
            // list would defeat the point of the window.
            var total = await items.CountAsync(token);

            // A group parent is normally outside the filtered page because groups
            // are excluded by default. Join it before applying the window so each
            // child still carries a usable public label without a second catalogue
            // download from the browser.
            var parents = dbContext.CashFlowItems.AsNoTracking();
            var orderedIds = await ReadOrderedCashFlowItemIdsAsync(
                search, skip, take, includeInactive, includeGroups, token);
            // Apply predicates/window to mapped entities before constructing the
            // public record; SQL Server cannot translate a predicate on its ctor.
            var pageIds = orderedIds?.ToArray();
            if (pageIds is null)
                items = items.OrderBy(item => item.Code).ThenBy(item => item.Name).ThenBy(item => item.Id)
                    .Skip(skip).Take(take);
            else
                items = items.Where(item => pageIds.Contains(item.Id));
            var projection =
                from item in items
                join candidateParent in parents
                    on item.ParentId equals (Guid?)candidateParent.Id into parentRows
                from parent in parentRows.DefaultIfEmpty()
                join candidateExpense in dbContext.ExpenseItems.AsNoTracking()
                    on item.AccrualExpenseItemId equals (Guid?)candidateExpense.Id into expenseRows
                from expense in expenseRows.DefaultIfEmpty()
                join candidateCenter in dbContext.CostCenters.AsNoTracking()
                    on item.CostCenterId equals (Guid?)candidateCenter.Id into centerRows
                from center in centerRows.DefaultIfEmpty()
                select new ReferenceDataPageItemDto(
                    item.Id,
                    item.Code,
                    item.Name,
                    item.ParentId,
                    parent == null ? null : parent.Code,
                    parent == null ? null : parent.Name,
                    item.ActivityType,
                    item.FormsManagerialAccrual,
                    expense == null ? null : expense.Name,
                    item.CostCenterId,
                    center == null ? null : center.Name,
                    item.FlowDirection);
            List<ReferenceDataPageItemDto> page;
            if (orderedIds is null)
            {
                page = await projection.ToListAsync(token);
            }
            else
            {
                var unordered = await projection.ToListAsync(token);
                var byId = unordered.ToDictionary(item => item.Id);
                if (byId.Count != pageIds!.Length)
                    throw new CashFlowCatalogOrderUnavailableException();
                var ranks = await cashFlowOrder.ReadDisplayOrdersAsync(pageIds, token);
                page = pageIds.Select(id => byId[id] with { DisplayOrder = ranks[id] }).ToList();
            }

            await transaction.CommitAsync(token);
            return new ReferenceDataItemWindow(page, total);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceDataItemDto>> ResolveCashFlowItemsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        var requestedIds = ids.ToArray();
        var items = dbContext.CashFlowItems.AsNoTracking()
            .Where(item => requestedIds.Contains(item.Id) && item.IsActive && !item.IsGroup);
        var rows = await WithPublicParentNames(items).ToListAsync(cancellationToken);
        return await AttachDisplayOrdersAsync(rows, cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceDataItemDto>> ListExpenseItemsAsync(
        CancellationToken cancellationToken)
    {
        return await WithRelatedCashFlowItems().ToListAsync(cancellationToken);
    }

    // Reverse the stored ID relationship in one SQL read, not one request per
    // expense article. Match the ordinary DDS catalogue's active leaf scope;
    // historical snapshots and disabled accrual settings are not reassigned.
    internal IQueryable<ReferenceDataItemDto> WithRelatedCashFlowItems() =>
        dbContext.ExpenseItems.AsNoTracking().AsSingleQuery()
            .OrderBy(item => item.Code).ThenBy(item => item.Name).ThenBy(item => item.Id)
            .Select(item => new ReferenceDataItemDto(item.Id, item.Code, item.Name,
                null, null, null, null, null, null, null, null, null, null, null, null,
                dbContext.CashFlowItems.AsNoTracking()
                    .Where(cashFlow => cashFlow.IsActive && !cashFlow.IsGroup
                        && cashFlow.FormsManagerialAccrual && cashFlow.AccrualExpenseItemId == item.Id)
                    .OrderBy(cashFlow => cashFlow.Code).ThenBy(cashFlow => cashFlow.Name).ThenBy(cashFlow => cashFlow.Id)
                    .Select(cashFlow => new ReferenceCodeItemDto(cashFlow.Code, cashFlow.Name)).ToList()));

    public async Task<ReferenceDataItemDto> CreateExpenseItemAsync(
        string code,
        string name,
        CancellationToken cancellationToken)
    {
        var transaction = dbContext.Database.CurrentTransaction;
        if (transaction is null
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
        {
            throw new InvalidOperationException(
                "An expense-item creation requires a Serializable business transaction.");
        }

        // Ignore client code, including a code sent by a previously cached UI.
        code = await ExpenseItemCodeAllocator.NextAsync(dbContext, cancellationToken);

        var item = new ExpenseItemReference
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
        };
        dbContext.ExpenseItems.Add(item);
        return new ReferenceDataItemDto(item.Id, item.Code, item.Name);
    }

    public async Task<IReadOnlyList<ReferenceDataItemDto>> ListCounterpartiesAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var counterparties = dbContext.Counterparties.AsNoTracking();

        if (!includeInactive)
        {
            counterparties = counterparties.Where(counterparty => counterparty.IsActive);
        }

        var items = await counterparties
            .OrderBy(counterparty => counterparty.Code)
            .ThenBy(counterparty => counterparty.Name)
            .Select(counterparty => new ReferenceDataItemDto(
                counterparty.Id,
                counterparty.Code,
                counterparty.Name,
                null,
                counterparty.Bin))
            .ToListAsync(cancellationToken);
        // Provenance is a local SQL projection of verified import evidence.
        // Never contact 1C or infer a company from a name/BIN in a page read.
        var origins = await dbContext.BusinessAuditEntries.AsNoTracking()
            .Where(a => a.ObjectType == "CounterpartySourceOrigin" && a.Action == "CounterpartyOriginsCaptured")
            .OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id)
            .Select(a => new { a.ObjectId, a.AfterJson }).ToArrayAsync(cancellationToken);
        var byId = origins.Where(a => Guid.TryParse(a.ObjectId, out _))
            .GroupBy(a => Guid.Parse(a.ObjectId)).ToDictionary(g => g.Key,
                g => CounterpartySourceNames341.Read(g.First().AfterJson));
        return items.Select(item => item with { SourceNames = byId.GetValueOrDefault(item.Id) }).ToArray();
    }

    public async Task<IReadOnlyList<ReferenceDataItemDto>> ListContractsAsync(
        Guid counterpartyId,
        Guid organizationId,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        if (counterpartyId == Guid.Empty)
        {
            throw new ArgumentException("Counterparty id is required.", nameof(counterpartyId));
        }
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization id is required.", nameof(organizationId));
        }

        var contracts = dbContext.Contracts
            .AsNoTracking()
            .Where(contract =>
                contract.CounterpartyId == counterpartyId &&
                contract.OrganizationId == organizationId);

        if (!includeInactive)
        {
            contracts = contracts.Where(contract => contract.IsActive);
        }

        return await contracts
            .OrderBy(contract => contract.Code)
            .ThenBy(contract => contract.Name)
            .Select(contract => new ReferenceDataItemDto(
                contract.Id,
                contract.Code,
                contract.Name, null, null, null, null, null, null, null, null, null, null,
                dbContext.Currencies.Where(currency => currency.Id == contract.CurrencyId)
                    .Select(currency => currency.Code).FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceDataItemDto>> ListCurrenciesAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var currencies = dbContext.Currencies.AsNoTracking();

        if (!includeInactive)
        {
            currencies = currencies.Where(currency => currency.IsActive);
        }

        var rows = await currencies
            .OrderBy(currency => currency.Code)
            .ThenBy(currency => currency.Name)
            .Select(currency => new
            {
                currency.Id,
                currency.Code,
                currency.Name,
                currency.NumericCode,
            })
            .ToListAsync(cancellationToken);

        // A legacy import used the internal numeric value in Code and put the
        // ISO alpha code in Name. Publish the canonical alpha code expected by
        // expense requests while the source row is being corrected.
        return rows
            .Select(currency => new ReferenceDataItemDto(
                currency.Id,
                CurrencyCodeResolver.TryResolveImported(
                    currency.Code,
                    currency.Name,
                    currency.NumericCode) ?? currency.Code,
                currency.Name,
                NumericCode: currency.NumericCode))
            .ToList();
    }

    private async Task<IReadOnlyList<ReferenceDataItemDto>> AttachDisplayOrdersAsync(
        IReadOnlyList<ReferenceDataItemDto> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return rows;
        var ranks = await cashFlowOrder.ReadDisplayOrdersAsync(
            rows.Select(row => row.Id).ToArray(), cancellationToken);
        return rows.Select(row => row with { DisplayOrder = ranks[row.Id] }).ToArray();
    }

    /// <summary>
    /// Returns null on schema 51, where the existing Code/Name/Id LINQ order is
    /// the compatibility fallback. On schema 52 the complete current catalogue
    /// is put into the shared hierarchy-block order before the window is taken;
    /// historical null-rank fallbacks remain deterministic and last.
    /// </summary>
    private async Task<Guid[]?> ReadOrderedCashFlowItemIdsAsync(
        string? search,
        int skip,
        int take,
        bool includeInactive,
        bool includeGroups,
        CancellationToken cancellationToken)
    {
        var transaction = dbContext.Database.CurrentTransaction
            ?? throw new CashFlowCatalogOrderUnavailableException();
        var connection = dbContext.Database.GetDbConnection();
        await using (var capability = connection.CreateCommand())
        {
            capability.Transaction = transaction.GetDbTransaction();
            capability.CommandText = """
                SELECT CONVERT(bit, CASE WHEN COL_LENGTH(
                    N'bms_web.CashFlowItems', N'DisplayOrder') = 4 THEN 1 ELSE 0 END)
                """;
            var available = await capability.ExecuteScalarAsync(cancellationToken);
            if (available is not bool supported || !supported) return null;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        if (!includeInactive && !includeGroups)
        {
            var matching = dbContext.CashFlowItems.AsNoTracking()
                .Where(item => item.IsActive && !item.IsGroup);
            if (!string.IsNullOrEmpty(search))
                matching = matching.Where(item => item.Code.Contains(search) || item.Name.Contains(search));
            var matchingRows = await matching.Select(item => new
            {
                item.Id,
                item.Code,
                item.Name
            }).ToArrayAsync(cancellationToken);
            var matchingIds = matchingRows.Select(item => item.Id).ToHashSet();
            var full = await cashFlowOrder.ReadAsync(false, cancellationToken);
            var orderedCurrent = CashFlowCatalogOrderService.OrderRowsForDisplay(full.Rows)
                .Where(row => matchingIds.Contains(row.Id))
                .Select(row => row.Id)
                .ToArray();
            var orderedCurrentIds = orderedCurrent.ToHashSet();
            // Historical-for-actuals leaves remain part of the long-standing
            // reference-data query but are deliberately outside the editable
            // current-catalogue snapshot. Keep them as deterministic null-rank
            // fallbacks after every current hierarchy block.
            var fallback = matchingRows
                .Where(item => !orderedCurrentIds.Contains(item.Id))
                .OrderBy(item => item.Code, StringComparer.Ordinal)
                .ThenBy(item => item.Name, StringComparer.Ordinal)
                .ThenBy(item => item.Id)
                .Select(item => item.Id);
            var ordered = orderedCurrent.Concat(fallback).ToArray();
            if (ordered.Length != matchingIds.Count)
                throw new CashFlowCatalogOrderUnavailableException();
            return ordered.Skip(skip).Take(take).ToArray();
        }

        command.CommandText = """
            SELECT item.[Id]
            FROM [bms_web].[CashFlowItems] AS item WITH (HOLDLOCK)
            WHERE (@includeInactive = 1 OR item.[IsActive] = 1)
              AND (@includeGroups = 1 OR item.[IsGroup] = 0)
              AND (@search IS NULL OR CHARINDEX(@search, item.[Code]) > 0
                   OR CHARINDEX(@search, item.[Name]) > 0)
            ORDER BY CASE WHEN item.[DisplayOrder] IS NULL THEN 1 ELSE 0 END,
                item.[DisplayOrder], item.[Code], item.[Name], item.[Id]
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
            """;
        AddParameter(command, "@includeInactive", DbType.Boolean, includeInactive);
        AddParameter(command, "@includeGroups", DbType.Boolean, includeGroups);
        AddParameter(command, "@search", DbType.String, search is null ? DBNull.Value : search);
        AddParameter(command, "@skip", DbType.Int32, skip);
        AddParameter(command, "@take", DbType.Int32, take);
        var ids = new List<Guid>(take);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));
        return ids.ToArray();
    }

    private static void AddParameter(
        System.Data.Common.DbCommand command,
        string name,
        DbType type,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static void ValidateActor(string actorExternalId)
    {
        if (string.IsNullOrWhiteSpace(actorExternalId))
        {
            throw new ArgumentException("Actor external id is required.", nameof(actorExternalId));
        }
    }
}
