using System.Linq.Expressions;
using Bms.Application.ExpenseRequests;
using Bms.Application.Abstractions;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Budgeting;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed partial class ExpenseRequestRepository
{
    private async Task<IReadOnlyList<ExpenseRequest>> ListByEffectiveBudgetAsync(IQueryable<ExpenseRequest> query,
        ExpenseRequestFilter filter,int skip,int take,CancellationToken token)
    {
        if(budgetRepository is null)throw new ExpenseRequestListBusinessColumnsUnavailableException();
        // Classification includes ALL filtered, already authorization-scoped
        // requests before pagination. Never decorate/sort just the current page.
        if(await query.Select(r=>r.Lines.Count).SumAsync(token)>100000)
            throw new ExpenseRequestListBusinessColumnsUnavailableException();
        var rows=await query.AsNoTracking().Include(r=>r.Lines).Take(20001).ToArrayAsync(token);
        if(rows.Length>20000)throw new ExpenseRequestListBusinessColumnsUnavailableException();
        var ids=rows.Select(r=>r.Id).ToArray();
        var currentRows=rows.Where(r=>r.Status is not (ExpenseRequestStatus.Paid or ExpenseRequestStatus.Closed)).ToArray();
        var projections=await budgetRepository.ListRequestProjectionsAsync(currentRows.Select(r=>r.Id).ToArray(),token);
        var scopes=currentRows.Select(ExpenseRequestBudgetIndicatorScope.FromRequest).ToArray();
        var keys=projections.Select(p=>p.Key).Concat(scopes.SelectMany(s=>s.BudgetKeys??[])).Distinct().ToArray();
        var positions=await budgetRepository.ListPositionsAsync(keys,token);
        var values=ExpenseRequestBudgetIndicatorEvaluator.Evaluate(scopes,projections,positions);
        var registerDecisions343=await budgetRepository.ReadFifoDecisions343Async(ids,token);
        var cancelled=new Dictionary<Guid,bool>();
        foreach(var batch in rows.Where(r=>r.Status is ExpenseRequestStatus.Cancelled or ExpenseRequestStatus.Paid or ExpenseRequestStatus.Closed)
            .Select(r=>r.Id).Chunk(128))
            foreach(var pair in await budgetRepository.ReadCancelledSubmissionBudgetIndicatorsAsync(batch,token))
                cancelled.Add(pair.Key,pair.Value);
        string Label(ExpenseRequest r)=>(registerDecisions343 is not null
            ?registerDecisions343.TryGetValue(r.Id,out var decision343)?decision343.IsOverBudget:(bool?)null
            :cancelled.TryGetValue(r.Id,out var historical)?historical:
            r.Status is ExpenseRequestStatus.Paid or ExpenseRequestStatus.Closed ? (bool?)null : values[r.Id]) switch
            {true=>"Сверх бюджета",false=>"В рамках бюджета",null=>"—"};
        var comparer=StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"),false);
        return (filter.SortDirection=="desc"
            ?rows.OrderByDescending(Label,comparer).ThenByDescending(r=>r.Id)
            :rows.OrderBy(Label,comparer).ThenBy(r=>r.Id)).Skip(skip).Take(take).ToArray();
    }

    // The input is already authorization-scoped and filtered. Sort in SQL BEFORE
    // Skip/Take, using only a closed set of expressions, never SQL from the URL.
    internal IQueryable<ExpenseRequest> ApplyListOrder(IQueryable<ExpenseRequest> query, ExpenseRequestFilter filter)
    {
        filter.Validate();
        var descending = filter.SortDirection == "desc";
        return filter.SortBy switch
        {
            null => ApplyDefaultPriorityOrder(query, dbContext.BusinessAuditEntries),
            "requestDate" => OrderRequestDates(query, dbContext.BusinessAuditEntries, descending),
            "plannedPaymentDate" => Order(query, r => r.PlannedPaymentDate, descending),
            "amount" => Order(query, r => r.Lines.Sum(l => l.Amount), descending),
            "amountKzt" => Order(query, r => !r.Lines.Any()
                ? r.CurrencyCode == "KZT" ? (decimal?)0m : null
                : r.Lines.Any(l => l.AmountKzt == null) ? null : r.Lines.Sum(l => l.AmountKzt), descending),
            "urgency" => TextOrder(query, r => r.IsUrgent ? "Срочная" : "Обычная", descending),
            "requestNumber" => TextOrder(query, r => r.RequestNumber ?? "Расходная заявка", descending),
            "organization" => TextOrder(query, r => dbContext.Organizations.Where(o => o.Id == r.OrganizationId && o.IsActive)
                .Select(o => o.Code.Trim() == "" ? "—" : o.Code.Trim()).FirstOrDefault() ?? "—", descending),
            "costCenter" => TextOrder(query, r => dbContext.CostCenters.Where(c => c.Id == r.DefaultCostCenterId && c.IsActive && !c.IsGroup)
                .Select(c => c.Name).FirstOrDefault() ?? "—", descending),
            "counterparty" => TextOrder(query, r => r.IsNewCounterparty
                ? r.CandidateCounterpartyName == null || r.CandidateCounterpartyName == "" ? "Новый контрагент" : r.CandidateCounterpartyName
                : dbContext.Counterparties.Where(c => c.Id == r.CounterpartyId && c.IsActive).Select(c => c.Name).FirstOrDefault() ?? "—", descending),
            "initiator" => OrderInitiator(query, descending),
            "status" => OrderStatus(query, descending),
            "budget" => OrderBudget(query, descending),
            _ => throw new ArgumentException("Invalid expense-request sort.")
        };
    }

    private static IQueryable<ExpenseRequest> Order<T>(IQueryable<ExpenseRequest> query,
        Expression<Func<ExpenseRequest, T>> key, bool descending) => descending
            ? query.OrderByDescending(key).ThenByDescending(r => r.Id)
            : query.OrderBy(key).ThenBy(r => r.Id);

    private static IQueryable<ExpenseRequest> TextOrder(IQueryable<ExpenseRequest> query,
        Expression<Func<ExpenseRequest, string>> key, bool descending)
    {
        // Russian alphabetical order, not enum values, UUIDs or SID identifiers.
        var collate = Expression.Call(typeof(RelationalDbFunctionsExtensions), nameof(RelationalDbFunctionsExtensions.Collate),
            [typeof(string)], Expression.Property(null, typeof(EF), nameof(EF.Functions)), key.Body,
            Expression.Constant("Cyrillic_General_100_CI_AS"));
        return Order(query, Expression.Lambda<Func<ExpenseRequest, string>>(collate, key.Parameters), descending);
    }

    private IQueryable<ExpenseRequest> OrderInitiator(IQueryable<ExpenseRequest> query, bool descending) =>
        TextOrder(query, r => (from m in dbContext.EmployeeActorMappings
            join e in dbContext.Employees on m.EmployeeId equals e.Id
            where EF.Functions.Collate(m.ActorExternalId, "Latin1_General_100_BIN2") ==
                EF.Functions.Collate(r.InitiatorExternalId, "Latin1_General_100_BIN2")
                && m.IsActive && e.IsActive && e.Name.Trim() != "" && e.Name.Trim().Length <= 256
                && !e.Name.Trim().StartsWith("S-1-")
                && !EF.Functions.Like(EF.Functions.Collate(e.Name, "Latin1_General_100_BIN2"), "%[\u0001-\u001f\u007f-\u009f]%")
            select e.Name.Trim()).FirstOrDefault()
            ?? (r.InitiatorExternalId.StartsWith("S-1-") ? "Инициатор недоступен" : r.InitiatorExternalId), descending);

    private IQueryable<ExpenseRequest> OrderStatus(IQueryable<ExpenseRequest> query, bool descending)
    {
        // Build the SQL CASE from the same canonical labels used by the DTO.
        var r = Expression.Parameter(typeof(ExpenseRequest), "r");
        var status = Expression.Property(r, nameof(ExpenseRequest.Status));
        Expression label = Expression.Constant("Статус недоступен");
        foreach (var value in Enum.GetValues<ExpenseRequestStatus>())
            label = Expression.Condition(Expression.Equal(status, Expression.Constant(value)),
                Expression.Constant(value.ToDisplayName()), label);
        var labels = query.Select(Expression.Lambda<Func<ExpenseRequest, StatusSortRow>>(
            Expression.MemberInit(Expression.New(typeof(StatusSortRow)),
                Expression.Bind(typeof(StatusSortRow).GetProperty(nameof(StatusSortRow.Request))!, r),
                Expression.Bind(typeof(StatusSortRow).GetProperty(nameof(StatusSortRow.Label))!, label)), r));
        var keys = labels.Select(x => new {
            x.Request,
            Key = x.Label + (x.Request.Status == ExpenseRequestStatus.Approved ? " — Казначей"
                : x.Request.Status == ExpenseRequestStatus.AwaitingNsi ? " — Специалист НСИ"
                : x.Request.Status != ExpenseRequestStatus.InApproval ? ""
                : " — " + dbContext.ApprovalRouteStages.Where(s => s.ExpenseRequestId == x.Request.Id
                    && s.SnapshotNumber == x.Request.ActiveApprovalRouteSnapshotNumber
                    && s.StageNumber == x.Request.ActiveApprovalRouteStageNumber)
                    .Select(s => s.RoleCode == "BUDGET_OWNER" ? "Владелец ЦФО"
                        : s.RoleCode == "FIN_CONTROLLER" ? "Финансовый контролёр"
                        : s.RoleCode == "FIN_DIRECTOR" ? "Финансовый директор"
                        : s.RoleCode == "GENERAL_DIRECTOR" ? "Генеральный директор" : "").FirstOrDefault())
        });
        return (descending
            ? keys.OrderByDescending(x => EF.Functions.Collate(x.Key, "Cyrillic_General_100_CI_AS")).ThenByDescending(x => x.Request.Id)
            : keys.OrderBy(x => EF.Functions.Collate(x.Key, "Cyrillic_General_100_CI_AS")).ThenBy(x => x.Request.Id))
            .Select(x => x.Request);
    }

    private IQueryable<ExpenseRequest> OrderBudget(IQueryable<ExpenseRequest> query, bool descending)
    {
        // Sort BEFORE paging against the same active published plan used by
        // BudgetRepository, never the obsolete legacy BudgetPositions limit.
        var actualKeys = from r in query
            from l in r.Lines
            where r.PlannedPaymentDate != null && l.CostCenterId != null && l.CashFlowItemId != null
                && l.CostCenterId != Guid.Empty && l.CashFlowItemId != Guid.Empty
            select new {
                RequestId = r.Id, CostCenterId = l.CostCenterId ?? Guid.Empty, CashFlowItemId = l.CashFlowItemId ?? Guid.Empty,
                Year = (r.TreasuryBudgetMonth ?? r.PlannedPaymentDate ?? DateOnly.MinValue).Year,
                Month = (r.TreasuryBudgetMonth ?? r.PlannedPaymentDate ?? DateOnly.MinValue).Month, Projected = false
            };
        var reservedKeys = from r in query
            from p in dbContext.BudgetRequestProjections
            where p.ExpenseRequestId == r.Id && r.Lines.Any(l => l.Id == p.ExpenseRequestLineId)
            select new {
                RequestId = r.Id, p.CostCenterId, p.CashFlowItemId,
                Year = p.BudgetMonth.Year, Month = p.BudgetMonth.Month, Projected = true
            };
        var keys = actualKeys.Concat(reservedKeys);
        var limits = keys.Select(k => new {
            k.RequestId, k.CostCenterId, k.CashFlowItemId, k.Year, k.Month, k.Projected,
            Limit = dbContext.BudgetInstances.Where(b => b.CostCenterId == k.CostCenterId
                    && b.ValidFrom.Year * 12 + b.ValidFrom.Month <= k.Year * 12 + k.Month
                    && b.ValidTo.Year * 12 + b.ValidTo.Month >= k.Year * 12 + k.Month
                    && dbContext.CompanyBudgetVersionInstances.Any(m => m.BudgetInstanceId == b.Id
                        && dbContext.CompanyBudgetActiveVersions.Any(a => a.Id == 1 && a.VersionId == m.VersionId)))
                .OrderByDescending(b => b.ApprovalOrdinal).ThenByDescending(b => b.ApprovedAtUtc).ThenByDescending(b => b.Id)
                .Select(b => (decimal?)b.Lines.Where(l => l.CashFlowItemId == k.CashFlowItemId
                    && l.BudgetMonth.Year == k.Year && l.BudgetMonth.Month == k.Month
                    && l.FlowDirection == BudgetFlowDirection.Payment).Sum(l => l.LimitAmountKzt))
                .FirstOrDefault() ?? 0m
        });
        var overIds = limits.Where(k => k.Limit <= 0m || (k.Projected && k.Limit <
            (dbContext.BudgetPositions.Where(b => b.CostCenterId == k.CostCenterId
                && b.CashFlowItemId == k.CashFlowItemId && b.BudgetMonth.Year == k.Year && b.BudgetMonth.Month == k.Month)
                .Select(b => (decimal?)(b.ReservedAmountKzt + b.PaidAmountKzt)).FirstOrDefault() ?? 0m)))
            .Select(k => k.RequestId);
        var cancelledSnapshots = CancelledRequestBudgetIndicatorReader.LatestSnapshots(dbContext);
        return TextOrder(query, r => cancelledSnapshots.Any(s => s.ExpenseRequestId == r.Id)
            ? cancelledSnapshots.Where(s => s.ExpenseRequestId == r.Id).Select(s => s.PositiveDeficitKzt).First() > 0m
                ? "Сверх бюджета" : "В рамках бюджета"
            : r.Status == ExpenseRequestStatus.Paid || r.Status == ExpenseRequestStatus.Closed ? "—"
            : overIds.Contains(r.Id) ? "Сверх бюджета"
            : !r.Lines.Any() || r.Lines.Any(l => !dbContext.BudgetRequestProjections.Any(p =>
                p.ExpenseRequestId == r.Id && p.ExpenseRequestLineId == l.Id))
                || dbContext.BudgetRequestProjections.Any(p => p.ExpenseRequestId == r.Id
                    && !r.Lines.Any(l => l.Id == p.ExpenseRequestLineId))
                ? "—" : "В рамках бюджета", descending);
    }

    private sealed class StatusSortRow
    {
        public required ExpenseRequest Request { get; init; }
        public required string Label { get; init; }
    }
}
