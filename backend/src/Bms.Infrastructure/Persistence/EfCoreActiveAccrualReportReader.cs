using System.Data;
using Bms.Application.Accruals;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Reads the current active accrual balances under the report's financial-role
/// and organization scope, not treasury card visibility. Summary and page use one
/// Serializable transaction so they cannot describe different closing states.
/// </summary>
public sealed class EfCoreActiveAccrualReportReader(
    BmsDbContext dbContext,
    ExpenseRequestAccessMoment? accessMoment = null)
    : IActiveAccrualReportReader
{
    private const string MissingInitiatorName = "Сотрудник не найден";

    public async Task<ActiveAccrualReadResult> ReadAsync(
        string actorExternalId,
        bool includeAll,
        ActiveAccrualFilter filter,
        int skip,
        int take,
        int? maximumScopeRows,
        AccrualAgingContext aging,
        CancellationToken cancellationToken)
    {
        // Constructor compatibility only; current role/CFO authority is read
        // from SQL, never inferred from an externally supplied access moment.
        _ = accessMoment;
        ArgumentException.ThrowIfNullOrWhiteSpace(actorExternalId);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(aging);
        if (skip < 0 || take <= 0)
        {
            throw new ArgumentOutOfRangeException(skip < 0 ? nameof(skip) : nameof(take));
        }

        if (maximumScopeRows is { } declared && declared <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumScopeRows));
        }

        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(
            strategyCancellationToken => ReadAttemptAsync(
                actorExternalId,
                includeAll,
                filter,
                skip,
                take,
                maximumScopeRows,
                aging,
                strategyCancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Reads the scope, and then answers from it.
    ///
    /// Everything the answer depends on is captured inside one Serializable
    /// transaction; the confirmed payment ledger is replayed afterwards, from
    /// that capture. The replay is arithmetic over rows that have already been
    /// read, so running it inside the transaction bought no coherence - it only
    /// held a Serializable transaction open, over every request in scope, while
    /// the process did work the database was not part of.
    /// </summary>
    private async Task<ActiveAccrualReadResult> ReadAttemptAsync(
        string actorExternalId,
        bool includeAll,
        ActiveAccrualFilter filter,
        int skip,
        int take,
        int? maximumScopeRows,
        AccrualAgingContext aging,
        CancellationToken cancellationToken)
    {
        var captured = await CaptureAsync(
            actorExternalId,
            includeAll,
            filter,
            skip,
            take,
            maximumScopeRows,
            aging,
            cancellationToken);
        if (captured is null)
        {
            return InvariantViolation();
        }

        // The last point at which abandoning this answer is free. Past it the
        // work is bounded arithmetic over rows already read, and it is allowed
        // to finish rather than be torn in half.
        cancellationToken.ThrowIfCancellationRequested();

        // The transaction is closed. What follows is the whole-scope ledger
        // proof, replayed over the captured evidence and nothing else.
        var paymentTimes = ResolvePaymentTimes(captured.Evidence);
        if (paymentTimes is null)
        {
            return InvariantViolation();
        }

        try
        {
            // The same business-date rule covers every request, including
            // requests outside this page. Future origins are not clamped.
            foreach (var time in paymentTimes.Values)
            {
                aging.GetPaymentBusinessDate(time);
            }

            foreach (var item in captured.Rows)
            {
                if (!paymentTimes.TryGetValue(item.ExpenseRequestId, out var time)
                    || item.OrderingPaidAtUtc != time
                    || item.OrderingBusinessDate != aging.GetPaymentBusinessDate(time).ToDateTime(TimeOnly.MinValue))
                {
                    return InvariantViolation();
                }
            }
        }
        catch (ArgumentException)
        {
            return InvariantViolation();
        }

        var rows = captured.Rows
            .Select(item => item.ToDomain(paymentTimes[item.ExpenseRequestId]))
            .ToArray();
        return new ActiveAccrualReadResult(
            rows,
            captured.Total,
            captured.Totals,
            HasInvariantViolation: false);
    }

    private static ActiveAccrualReadResult InvariantViolation() => new(
        [],
        0,
        new ActiveAccrualTotals(0m, 0m, 0m),
        HasInvariantViolation: true);

    /// <summary>
    /// Captures the whole answer from one Serializable transaction: whether the
    /// scope is well formed, the confirmed payment evidence of every active
    /// request in it, the totals of the scope and the requested page. A null
    /// capture is a scope this reader refuses to describe.
    /// </summary>
    private async Task<CapturedRead?> CaptureAsync(
        string actorExternalId,
        bool includeAll,
        ActiveAccrualFilter filter,
        int skip,
        int take,
        int? maximumScopeRows,
        AccrualAgingContext aging,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            // Resolve current mapping, selected role and CFO assignments once,
            // under the same Serializable locks as the financial capture.
            // Repeating that canonical authority subquery in each line join,
            // aggregate and ledger statement caused millions of logical reads
            // and 30-second SQL timeouts at the accepted 10,000-request tier.
            var capturedScope = await BusinessDataReadScopeQuery.ResolveAsync(
                dbContext, actorExternalId, cancellationToken);
            var requests = ActiveAccrualVisibilityQuery.Apply(
                dbContext,
                dbContext.ExpenseRequests
                    .AsNoTracking()
                    .Where(request => request.Status == ExpenseRequestStatus.Paid),
                capturedScope);
            var candidates =
                from request in requests
                join line in dbContext.ExpenseRequestLines.AsNoTracking()
                    on request.Id equals line.ExpenseRequestId
                join organizationCandidate in dbContext.Organizations.AsNoTracking()
                    on request.OrganizationId equals (Guid?)organizationCandidate.Id
                    into organizationCandidates
                from organization in organizationCandidates.DefaultIfEmpty()
                join costCenterCandidate in dbContext.CostCenters.AsNoTracking()
                    on (line.AccrualCostCenterId ?? line.CostCenterId) equals (Guid?)costCenterCandidate.Id
                    into costCenterCandidates
                from costCenter in costCenterCandidates.DefaultIfEmpty()
                join cashFlowItemCandidate in dbContext.CashFlowItems.AsNoTracking()
                    on line.CashFlowItemId equals (Guid?)cashFlowItemCandidate.Id
                    into cashFlowItemCandidates
                from cashFlowItem in cashFlowItemCandidates.DefaultIfEmpty()
                join expenseItemCandidate in dbContext.ExpenseItems.AsNoTracking()
                    on line.AccrualSnapshotExpenseItemId equals (Guid?)expenseItemCandidate.Id
                    into expenseItemCandidates
                from expenseItem in expenseItemCandidates.DefaultIfEmpty()
                join counterpartyCandidate in dbContext.Counterparties.AsNoTracking()
                    on request.CounterpartyId equals (Guid?)counterpartyCandidate.Id
                    into counterpartyCandidates
                from counterparty in counterpartyCandidates.DefaultIfEmpty()
                join contractCandidate in dbContext.Contracts.AsNoTracking()
                    on request.ContractId equals (Guid?)contractCandidate.Id
                    into contractCandidates
                from contract in contractCandidates.DefaultIfEmpty()
                join mappingCandidate in dbContext.EmployeeActorMappings.AsNoTracking()
                    on request.InitiatorExternalId equals mappingCandidate.ActorExternalId
                    into mappingCandidates
                from mapping in mappingCandidates.DefaultIfEmpty()
                join employeeCandidate in dbContext.Employees.AsNoTracking()
                    on (mapping == null ? (Guid?)null : mapping.EmployeeId)
                    equals (Guid?)employeeCandidate.Id
                    into employeeCandidates
                from employee in employeeCandidates.DefaultIfEmpty()
                where line.AccrualSnapshotFormsManagerialAccrual == true
                select new
                {
                    ExpenseRequestId = request.Id,
                    LineId = line.Id,
                    StoredFullyPaidAtUtc = request.FullyPaidAtUtc,
                    request.OrganizationId,
                    request.CounterpartyId,
                    request.ContractId,
                    CostCenterId = line.AccrualCostCenterId ?? line.CostCenterId,
                    line.CashFlowItemId,
                    line.AccrualSnapshotExpenseItemId,
                    LineSortOrder = line.SortOrder,
                    line.AccrualPeriod,
                    request.CurrencyCode,
                    OrganizationName = organization == null ? null : organization.Name,
                    CostCenterName = costCenter == null ? null : costCenter.Name,
                    CashFlowItemName = cashFlowItem == null ? null : cashFlowItem.Name,
                    ExpenseItemName = expenseItem == null ? null : expenseItem.Name,
                    CounterpartyWasSpecified = request.CounterpartyId != null,
                    CounterpartyName = counterparty == null ? null : counterparty.Name,
                    ContractWasSpecified = request.ContractId != null,
                    ContractName = contract == null ? null : contract.Name,
                    ContractCounterpartyId = contract == null ? (Guid?)null : contract.CounterpartyId,
                    ContractOrganizationId = contract == null ? (Guid?)null : contract.OrganizationId,
                    // Older requests can predate the actor-to-employee projection.
                    // Keep the financial row visible without publishing the raw actor
                    // identifier; the fixed label is public and cannot carry diagnostics.
                    InitiatorName = employee == null ? MissingInitiatorName : employee.Name,
                    request.Purpose,
                    line.Amount,
                    line.ConfirmedClosedAmount,
                    line.AmountKzt,
                    line.AmountExcludingVatKzt,
                    line.VatAmountKzt,
                    line.ConfirmedClosedAmountKzt
                };

            if (filter.OrganizationId is { } organizationId)
            {
                candidates = candidates.Where(item => item.OrganizationId == organizationId);
            }

            if (filter.CostCenterId is { } costCenterId)
            {
                candidates = candidates.Where(item => item.CostCenterId == costCenterId);
            }

            if (filter.CounterpartyId is { } counterpartyId)
            {
                candidates = candidates.Where(item => item.CounterpartyId == counterpartyId);
            }

            if (filter.ContractId is { } contractId)
            {
                candidates = candidates.Where(item => item.ContractId == contractId);
            }

            if (filter.AccrualPeriodFrom is { } periodFrom)
            {
                candidates = candidates.Where(item => item.AccrualPeriod >= periodFrom);
            }

            if (filter.AccrualPeriodThrough is { } periodThrough)
            {
                candidates = candidates.Where(item => item.AccrualPeriod <= periodThrough);
            }

            // EXISTS gives SQL Server a one-row goal. A healthy scope has no
            // invalid row, so that plan repeatedly probes every reference for
            // every candidate and can time out under concurrent reads. COUNT_BIG
            // preserves the fail-closed validation without that false row goal.
            var invalidCandidateCount = await candidates.LongCountAsync(item =>
                item.OrganizationId == null
                || item.CurrencyCode.Length != 3
                || item.LineSortOrder <= 0
                || item.AccrualPeriod == null
                || item.AccrualPeriod.Value.Day != 1
                || item.CashFlowItemId == null
                || item.CostCenterId == null
                || item.AccrualSnapshotExpenseItemId == null
                || item.OrganizationName == null
                || item.CostCenterName == null
                || item.CashFlowItemName == null
                || item.ExpenseItemName == null
                || (item.CounterpartyWasSpecified && item.CounterpartyName == null)
                || (!item.CounterpartyWasSpecified && item.CounterpartyName != null)
                || (item.ContractWasSpecified
                    && (item.ContractName == null
                        || item.ContractCounterpartyId != item.CounterpartyId
                        || item.ContractOrganizationId != item.OrganizationId))
                || (!item.ContractWasSpecified && item.ContractName != null)
                || item.Amount <= 0m
                || item.AmountKzt == null
                || item.AmountKzt < 0m
                || item.ConfirmedClosedAmount < 0m
                || item.ConfirmedClosedAmount > item.Amount
                || item.ConfirmedClosedAmountKzt < 0m
                || item.ConfirmedClosedAmountKzt > item.AmountKzt
                || (item.ConfirmedClosedAmount == item.Amount
                    && item.ConfirmedClosedAmountKzt != item.AmountKzt),
                cancellationToken);
            if (invalidCandidateCount != 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            var active = candidates
                .Where(item => item.Amount - item.ConfirmedClosedAmount > 0m);

            // The size of the scope is one aggregate over the same capture. A
            // caller with a ceiling learns here that it has been exceeded -
            // before the payment ledger of that scope is pulled into memory and
            // before a page is read that nobody will be shown.
            var summary = await active
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    LineTotal = group.Count(),
                    RequestTotal = group.Select(item => item.ExpenseRequestId).Distinct().Count(),
                    OriginalAmountKzt = group.Sum(item => item.AmountKzt.GetValueOrDefault()),
                    ConfirmedClosedAmountKzt = group.Sum(item => item.ConfirmedClosedAmountKzt),
                    RemainingAmountKzt = group.Sum(item =>
                        item.AmountKzt.GetValueOrDefault() - item.ConfirmedClosedAmountKzt)
                })
                .SingleOrDefaultAsync(cancellationToken);
            var totals = new ActiveAccrualTotals(
                summary?.OriginalAmountKzt ?? 0m,
                summary?.ConfirmedClosedAmountKzt ?? 0m,
                summary?.RemainingAmountKzt ?? 0m);
            var total = filter.DetailLevel == ActiveAccrualDetailLevel.Request
                ? summary?.RequestTotal ?? 0
                : summary?.LineTotal ?? 0;
            if (maximumScopeRows is { } maximum && total > maximum)
            {
                await transaction.CommitAsync(cancellationToken);
                return new CapturedRead(PaymentEvidenceCapture.Empty, [], total, totals);
            }

            // Keep the complete request scope as a server-side subquery.  The
            // previous implementation materialized every id into a CLR array
            // and EF sent that array back to SQL for each evidence read.  The
            // scope is still the exact same filtered/distinct set; it simply
            // remains an IQueryable so SQL Server can evaluate it in place.
            var activeRequestIdQuery = active
                .Select(item => item.ExpenseRequestId)
                .Distinct();
            var evidence = await CapturePaymentEvidenceAsync(
                activeRequestIdQuery,
                cancellationToken);
            if (evidence is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            // Keep the ordering projection on the same complete active scope,
            // but only for requests that still need the ledger-derived date.
            // A persisted FullyPaidAtUtc is the authoritative fast path for
            // newer requests; re-running the payment window for those rows
            // only makes SQL Server rescan facts that cannot affect the page.
            // The ledger evidence above remains the authoritative whole-scope
            // proof; this projection is only the ordering aid that must agree
            // with it after replay.
            IQueryable<ActiveAccrualPaymentTimeQuery.FirstFullPaymentTime> activePaymentTimeQuery;
            if (evidence.RequiresDerivedPaymentTime)
            {
                var paymentRequestIdQuery = active
                    .Where(item => item.StoredFullyPaidAtUtc == null)
                    .Select(item => item.ExpenseRequestId)
                    .Distinct();
                activePaymentTimeQuery = ActiveAccrualPaymentTimeQuery
                    .Read(dbContext)
                    .Where(payment => paymentRequestIdQuery.Contains(payment.ExpenseRequestId));
            }
            else
            {
                // Avoid even composing the raw payment window when every
                // captured request already has its stored date. This produces
                // a constant-empty relational source for the left join while
                // preserving the same projection shape and fallback contract.
                activePaymentTimeQuery = dbContext.ExpenseRequests
                    .AsNoTracking()
                    .Where(_ => false)
                    .Select(request => new ActiveAccrualPaymentTimeQuery.FirstFullPaymentTime
                    {
                        ExpenseRequestId = request.Id,
                        FirstFullyPaidAtUtc = null
                    });
            }

            var sqlTimeZone = ActiveAccrualPaymentTimeQuery.SqlServerTimeZone(aging.BusinessTimeZone);
            ActiveAccrualDatabaseRow[] databaseRows;
            if (filter.DetailLevel == ActiveAccrualDetailLevel.Request)
            {
                var requestSummaries = active
                    .GroupBy(item => new
                    {
                        item.ExpenseRequestId,
                        item.StoredFullyPaidAtUtc,
                        item.CurrencyCode,
                        item.OrganizationName,
                        item.CounterpartyId,
                        item.CounterpartyWasSpecified,
                        item.CounterpartyName,
                        item.ContractId,
                        item.ContractWasSpecified,
                        item.ContractName,
                        item.InitiatorName,
                        item.Purpose
                    })
                    .Select(group => new
                    {
                        group.Key.ExpenseRequestId,
                        group.Key.StoredFullyPaidAtUtc,
                        group.Key.CurrencyCode,
                        group.Key.OrganizationName,
                        group.Key.CounterpartyId,
                        group.Key.CounterpartyWasSpecified,
                        group.Key.CounterpartyName,
                        group.Key.ContractId,
                        group.Key.ContractWasSpecified,
                        group.Key.ContractName,
                        group.Key.InitiatorName,
                        group.Key.Purpose,
                        LineSortOrder = group.Min(item => item.LineSortOrder),
                        LineCount = group.Count(),
                        AccrualPeriod = group.Min(item => item.AccrualPeriod),
                        AccrualPeriodThrough = group.Max(item => item.AccrualPeriod),
                        CostCenterValueCount = group
                            .Select(item => item.CostCenterId)
                            .Distinct()
                            .Count(),
                        CostCenterName = group.Min(item => item.CostCenterName),
                        CashFlowItemValueCount = group
                            .Select(item => item.CashFlowItemId)
                            .Distinct()
                            .Count(),
                        CashFlowItemName = group.Min(item => item.CashFlowItemName),
                        ExpenseItemValueCount = group
                            .Select(item => item.AccrualSnapshotExpenseItemId)
                            .Distinct()
                            .Count(),
                        ExpenseItemName = group.Min(item => item.ExpenseItemName),
                        OriginalAmount = group.Sum(item => item.Amount),
                        ConfirmedClosedAmount = group.Sum(item => item.ConfirmedClosedAmount),
                        RemainingAmount = group.Sum(item =>
                            item.Amount - item.ConfirmedClosedAmount),
                        OriginalAmountKzt = group.Sum(item => item.AmountKzt.GetValueOrDefault()),
                        ConfirmedClosedAmountKzt = group.Sum(item => item.ConfirmedClosedAmountKzt),
                        RemainingAmountKzt = group.Sum(item =>
                            item.AmountKzt.GetValueOrDefault() - item.ConfirmedClosedAmountKzt)
                    });
                var datedSummaries =
                    from item in requestSummaries
                    join paymentCandidate in activePaymentTimeQuery
                        on item.ExpenseRequestId equals paymentCandidate.ExpenseRequestId
                        into paymentCandidates
                    from payment in paymentCandidates.DefaultIfEmpty()
                    let firstPaidAtUtc = item.StoredFullyPaidAtUtc ?? payment.FirstFullyPaidAtUtc
                    select new
                    {
                        Item = item,
                        FirstPaidAtUtc = firstPaidAtUtc,
                        BusinessDate = firstPaidAtUtc.HasValue
                            ? (DateTime?)EF.Functions.AtTimeZone(firstPaidAtUtc.Value, sqlTimeZone).Date
                            : null
                    };
                // Both identities are request-header values: including them in
                // the request key above cannot split a request or change sums.
                var orderedSummaries = filter.Grouping switch
                {
                    ActiveAccrualGrouping.Counterparty => datedSummaries
                        .OrderBy(entry => entry.Item.CounterpartyName)
                        .ThenBy(entry => entry.Item.CounterpartyId)
                        .ThenBy(entry => entry.BusinessDate)
                        .ThenBy(entry => entry.Item.ExpenseRequestId),
                    ActiveAccrualGrouping.Contract => datedSummaries
                        .OrderBy(entry => entry.Item.ContractName)
                        .ThenBy(entry => entry.Item.ContractId)
                        .ThenBy(entry => entry.BusinessDate)
                        .ThenBy(entry => entry.Item.ExpenseRequestId),
                    _ => datedSummaries
                        .OrderBy(entry => entry.BusinessDate)
                        .ThenBy(entry => entry.Item.ExpenseRequestId)
                };
                databaseRows = await orderedSummaries
                    .Skip(skip)
                    .Take(take)
                    .Select(entry => new ActiveAccrualDatabaseRow
                    {
                        ExpenseRequestId = entry.Item.ExpenseRequestId,
                        OrderingPaidAtUtc = entry.FirstPaidAtUtc,
                        OrderingBusinessDate = entry.BusinessDate,
                        LineSortOrder = entry.Item.LineSortOrder,
                        LineCount = entry.Item.LineCount,
                        IsRequestSummary = true,
                        GroupIdentity = filter.Grouping == ActiveAccrualGrouping.Counterparty
                            ? entry.Item.CounterpartyId
                            : filter.Grouping == ActiveAccrualGrouping.Contract ? entry.Item.ContractId : null,
                        AccrualPeriod = entry.Item.AccrualPeriod.GetValueOrDefault(),
                        AccrualPeriodThrough = entry.Item.AccrualPeriodThrough.GetValueOrDefault(),
                        CurrencyCode = entry.Item.CurrencyCode,
                        OrganizationName = entry.Item.OrganizationName,
                        CostCenterName = entry.Item.CostCenterName,
                        CostCenterValueCount = entry.Item.CostCenterValueCount,
                        CashFlowItemName = entry.Item.CashFlowItemName,
                        CashFlowItemValueCount = entry.Item.CashFlowItemValueCount,
                        ExpenseItemName = entry.Item.ExpenseItemName,
                        ExpenseItemValueCount = entry.Item.ExpenseItemValueCount,
                        CounterpartyWasSpecified = entry.Item.CounterpartyWasSpecified,
                        CounterpartyName = entry.Item.CounterpartyName,
                        ContractWasSpecified = entry.Item.ContractWasSpecified,
                        ContractName = entry.Item.ContractName,
                        InitiatorName = entry.Item.InitiatorName,
                        Purpose = entry.Item.Purpose,
                        OriginalAmount = entry.Item.OriginalAmount,
                        ConfirmedClosedAmount = entry.Item.ConfirmedClosedAmount,
                        RemainingAmount = entry.Item.RemainingAmount,
                        OriginalAmountKzt = entry.Item.OriginalAmountKzt,
                        ConfirmedClosedAmountKzt = entry.Item.ConfirmedClosedAmountKzt,
                        RemainingAmountKzt = entry.Item.RemainingAmountKzt
                    })
                    .ToArrayAsync(cancellationToken);
            }
            else
            {
                var dated =
                    from item in active
                    join paymentCandidate in activePaymentTimeQuery
                        on item.ExpenseRequestId equals paymentCandidate.ExpenseRequestId
                        into paymentCandidates
                    from payment in paymentCandidates.DefaultIfEmpty()
                    let firstPaidAtUtc = item.StoredFullyPaidAtUtc ?? payment.FirstFullyPaidAtUtc
                    select new
                    {
                        Item = item,
                        FirstPaidAtUtc = firstPaidAtUtc,
                        BusinessDate = firstPaidAtUtc.HasValue
                            ? (DateTime?)EF.Functions.AtTimeZone(firstPaidAtUtc.Value, sqlTimeZone).Date
                            : null
                    };

                // Strictly increasing thresholds make oldest business date first
                // exactly Zone DESC, AgeDays DESC. Request numbering is not yet a
                // defined field; equal ages use the stable, server-only line key.
                // Ordering is performed over the whole scope BEFORE pagination.
                var ordered = filter.Grouping switch
                {
                    ActiveAccrualGrouping.Counterparty => dated
                        .OrderBy(entry => entry.Item.CounterpartyName)
                        .ThenBy(entry => entry.Item.CounterpartyId)
                        .ThenBy(entry => entry.BusinessDate)
                        .ThenBy(entry => entry.Item.LineId),
                    ActiveAccrualGrouping.Contract => dated
                        .OrderBy(entry => entry.Item.ContractName)
                        .ThenBy(entry => entry.Item.ContractId)
                        .ThenBy(entry => entry.BusinessDate)
                        .ThenBy(entry => entry.Item.LineId),
                    _ => dated
                        .OrderBy(entry => entry.BusinessDate)
                        .ThenBy(entry => entry.Item.LineId)
                };
                databaseRows = await ordered
                    .Skip(skip)
                    .Take(take)
                    .Select(entry => new ActiveAccrualDatabaseRow
                    {
                        ExpenseRequestId = entry.Item.ExpenseRequestId,
                        OrderingPaidAtUtc = entry.FirstPaidAtUtc,
                        OrderingBusinessDate = entry.BusinessDate,
                        LineSortOrder = entry.Item.LineSortOrder,
                        LineCount = 1,
                        IsRequestSummary = false,
                        CostCenterIdentity = entry.Item.CostCenterId,
                        ExpenseItemIdentity = entry.Item.AccrualSnapshotExpenseItemId,
                        OriginalNetAmountKzt = entry.Item.AmountExcludingVatKzt,
                        OriginalVatAmountKzt = entry.Item.VatAmountKzt,
                        GroupIdentity = filter.Grouping == ActiveAccrualGrouping.Counterparty
                            ? entry.Item.CounterpartyId
                            : filter.Grouping == ActiveAccrualGrouping.Contract ? entry.Item.ContractId : null,
                        AccrualPeriod = entry.Item.AccrualPeriod.GetValueOrDefault(),
                        AccrualPeriodThrough = entry.Item.AccrualPeriod.GetValueOrDefault(),
                        CurrencyCode = entry.Item.CurrencyCode,
                        OrganizationName = entry.Item.OrganizationName,
                        CostCenterName = entry.Item.CostCenterName,
                        CostCenterValueCount = 1,
                        CashFlowItemName = entry.Item.CashFlowItemName,
                        CashFlowItemValueCount = 1,
                        ExpenseItemName = entry.Item.ExpenseItemName,
                        ExpenseItemValueCount = 1,
                        CounterpartyWasSpecified = entry.Item.CounterpartyWasSpecified,
                        CounterpartyName = entry.Item.CounterpartyName,
                        ContractWasSpecified = entry.Item.ContractWasSpecified,
                        ContractName = entry.Item.ContractName,
                        InitiatorName = entry.Item.InitiatorName,
                        Purpose = entry.Item.Purpose,
                        OriginalAmount = entry.Item.Amount,
                        ConfirmedClosedAmount = entry.Item.ConfirmedClosedAmount,
                        RemainingAmount = entry.Item.Amount - entry.Item.ConfirmedClosedAmount,
                        OriginalAmountKzt = entry.Item.AmountKzt.GetValueOrDefault(),
                        ConfirmedClosedAmountKzt = entry.Item.ConfirmedClosedAmountKzt,
                        RemainingAmountKzt = entry.Item.AmountKzt.GetValueOrDefault()
                            - entry.Item.ConfirmedClosedAmountKzt
                    })
                    .ToArrayAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new CapturedRead(evidence, databaseRows, total, totals);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the read failure. Disposing the transaction below is
                // the final cleanup attempt and must not mask the root cause.
            }

            throw;
        }
    }

    /// <summary>
    /// Reads the confirmed payment evidence of every active request in scope.
    /// This is the database half of the ledger proof and stays inside the
    /// transaction; the arithmetic half is <see cref="ResolvePaymentTimes"/>.
    /// </summary>
    private async Task<PaymentEvidenceCapture?> CapturePaymentEvidenceAsync(
        IQueryable<Guid> requestIds,
        CancellationToken cancellationToken)
    {
        var requestCount = await requestIds.CountAsync(cancellationToken);
        if (requestCount == 0)
        {
            return PaymentEvidenceCapture.Empty;
        }

        var scopes = await dbContext.ExpenseRequests
            .AsNoTracking()
            .Where(request => requestIds.Contains(request.Id))
            .Select(request => new
            {
                request.Id,
                request.CurrencyCode,
                request.FullyPaidAtUtc,
                Obligation = request.Lines.Sum(line => line.Amount)
            })
            .ToArrayAsync(cancellationToken);
        if (scopes.Length != requestCount)
        {
            return null;
        }

        var currencyCodes = scopes
            .Select(item => item.CurrencyCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var currencies = await dbContext.Currencies
            .AsNoTracking()
            .Where(currency => currencyCodes.Contains(currency.Code))
            .Select(currency => new { currency.Code, currency.Id })
            .ToArrayAsync(cancellationToken);
        if (currencies.Length != currencyCodes.Length)
        {
            return null;
        }

        var paymentFacts = await dbContext.ExpenseRequestPaymentFacts
            .AsNoTracking()
            .Where(fact => requestIds.Contains(fact.ExpenseRequestId))
            .OrderBy(fact => fact.ExpenseRequestId)
            .ThenBy(fact => fact.PaidAt)
            .ThenBy(fact => fact.EventId)
            .ToArrayAsync(cancellationToken);

        // Three arrays, exactly as they were read. Projecting, grouping and
        // indexing them is work the database is not part of, and it waits until
        // the transaction has ended.
        return new PaymentEvidenceCapture(
            scopes
                .Select(item => new CapturedRequestObligation(
                    item.Id,
                    item.CurrencyCode,
                    item.FullyPaidAtUtc,
                    item.Obligation))
                .ToArray(),
            currencies
                .Select(item => new CapturedCurrency(item.Code, item.Id))
                .ToArray(),
            paymentFacts,
            scopes.Any(item => item.FullyPaidAtUtc is null),
            (await ApprovedAccountingEvidence400.ReadAsync(dbContext,cancellationToken))
                .SelectMany(g=>g.Allocations.Where(a=>a.RoundingAmount>0)
                    .Select(a=>new ApprovedPaymentRounding400(g.ApprovalId,a.ExpenseRequestId,a.CurrencyId,
                        a.EventId,a.RoundingAmount,g.PaidAtUtc,g.ApprovedAtUtc))).ToArray());
    }

    /// <summary>
    /// Replays the captured confirmed ledger of every active request in scope
    /// and derives the instant at which each request was first fully paid. Pure
    /// arithmetic over a capture that is already read: no database, no
    /// cancellation, and therefore no reason to hold a transaction open for it.
    ///
    /// A request the ledger cannot account for makes the whole answer refuse -
    /// the proof covers the scope, not the page.
    /// </summary>
    private static IReadOnlyDictionary<Guid, DateTimeOffset>? ResolvePaymentTimes(
        PaymentEvidenceCapture capture)
    {
        var currencyIdsByCode = capture.Currencies.ToDictionary(
            item => item.Code,
            item => item.Id,
            StringComparer.Ordinal);
        var eventsByRequest = capture.PaymentFacts
            .Select(fact => new ConfirmedPaymentEvent(
                fact.SourceSystem,
                fact.ExternalId,
                fact.EventId,
                fact.ExpenseRequestId,
                fact.CurrencyId,
                fact.Amount,
                fact.AmountKzt,
                fact.PaidAt,
                fact.PaymentNumber,
                fact.IsReversal,
                fact.ReversedEventId))
            .GroupBy(item => item.ExpenseRequestId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ConfirmedPaymentEvent>)group.ToArray());

        var result = new Dictionary<Guid, DateTimeOffset>();
        try
        {
            foreach (var scope in capture.Obligations)
            {
                if (!currencyIdsByCode.TryGetValue(scope.CurrencyCode, out var currencyId)
                    || !eventsByRequest.TryGetValue(scope.Id, out var events))
                {
                    return null;
                }

                result.Add(
                    scope.Id,
                    ExpenseRequestFullPaymentEvidenceResolver.ResolveFirstFullyPaidAtUtc(
                        scope.Id,
                        currencyId,
                        scope.Obligation,
                        events,
                        scope.FullyPaidAtUtc,
                        (capture.Rounding??[]).Where(r=>r.ExpenseRequestId==scope.Id).ToArray()));
            }
        }
        catch (Exception exception) when (exception is DomainRuleViolationException
            or ExpenseRequestClosingBlockedException)
        {
            return null;
        }

        return result;
    }

    /// <summary>One request obligation as it was read inside the transaction.</summary>
    private sealed record CapturedRequestObligation(
        Guid Id,
        string CurrencyCode,
        DateTimeOffset? FullyPaidAtUtc,
        decimal Obligation);

    /// <summary>One settlement currency as it was read inside the transaction.</summary>
    private sealed record CapturedCurrency(string Code, Guid Id);

    /// <summary>
    /// The confirmed payment evidence of the whole active scope, as read: rows,
    /// not indexes. Building the indexes is arithmetic and belongs outside the
    /// transaction that produced them.
    /// </summary>
    private sealed record PaymentEvidenceCapture(
        IReadOnlyList<CapturedRequestObligation> Obligations,
        IReadOnlyList<CapturedCurrency> Currencies,
        IReadOnlyList<ExpenseRequestPaymentFact> PaymentFacts,
        bool RequiresDerivedPaymentTime,
        IReadOnlyList<ApprovedPaymentRounding400>? Rounding = null)
    {
        internal static PaymentEvidenceCapture Empty { get; } = new([], [], [], false);
    }

    /// <summary>
    /// One coherent answer, read from one Serializable transaction and complete
    /// before that transaction ends.
    /// </summary>
    private sealed record CapturedRead(
        PaymentEvidenceCapture Evidence,
        IReadOnlyList<ActiveAccrualDatabaseRow> Rows,
        int Total,
        ActiveAccrualTotals Totals);

    private sealed class ActiveAccrualDatabaseRow
    {
        public Guid? CostCenterIdentity { get; init; }
        public Guid? ExpenseItemIdentity { get; init; }
        public decimal? OriginalNetAmountKzt { get; init; }
        public decimal? OriginalVatAmountKzt { get; init; }
        public Guid ExpenseRequestId { get; init; }

        public Guid? GroupIdentity { get; init; }

        public DateTimeOffset? OrderingPaidAtUtc { get; init; }

        public DateTime? OrderingBusinessDate { get; init; }

        public int LineSortOrder { get; init; }

        public int LineCount { get; init; }

        public bool IsRequestSummary { get; init; }

        public DateOnly AccrualPeriod { get; init; }

        public DateOnly AccrualPeriodThrough { get; init; }

        public string CurrencyCode { get; init; } = string.Empty;

        public string? OrganizationName { get; init; }

        public string? CostCenterName { get; init; }

        public int CostCenterValueCount { get; init; }

        public string? CashFlowItemName { get; init; }

        public int CashFlowItemValueCount { get; init; }

        public string? ExpenseItemName { get; init; }

        public int ExpenseItemValueCount { get; init; }

        public bool CounterpartyWasSpecified { get; init; }

        public string? CounterpartyName { get; init; }

        public bool ContractWasSpecified { get; init; }

        public string? ContractName { get; init; }

        public string? InitiatorName { get; init; }

        public string? Purpose { get; init; }

        public decimal OriginalAmount { get; init; }

        public decimal ConfirmedClosedAmount { get; init; }

        public decimal RemainingAmount { get; init; }

        public decimal OriginalAmountKzt { get; init; }

        public decimal ConfirmedClosedAmountKzt { get; init; }

        public decimal RemainingAmountKzt { get; init; }

        public ActiveAccrualRow ToDomain(DateTimeOffset fullyPaidAtUtc) => new(
            ExpenseRequestId,
            LineSortOrder,
            fullyPaidAtUtc,
            AccrualPeriod,
            CurrencyCode,
            OrganizationName,
            DisplayDimension(CostCenterName, CostCenterValueCount, "Несколько ЦФО"),
            DisplayDimension(CashFlowItemName, CashFlowItemValueCount, "Несколько статей ДДС"),
            DisplayDimension(ExpenseItemName, ExpenseItemValueCount, "Несколько статей расходов"),
            CounterpartyWasSpecified,
            CounterpartyName,
            Purpose,
            OriginalAmount,
            ConfirmedClosedAmount,
            RemainingAmount,
            OriginalAmountKzt,
            ConfirmedClosedAmountKzt,
            RemainingAmountKzt,
            AccrualPeriodThrough,
            ContractWasSpecified,
            ContractName,
            InitiatorName,
            LineCount,
            IsRequestSummary,
            GroupIdentity,
            CostCenterIdentity,
            ExpenseItemIdentity,
            OriginalNetAmountKzt,
            OriginalVatAmountKzt);

        private static string? DisplayDimension(
            string? value,
            int distinctCount,
            string multipleValuesLabel) =>
            distinctCount == 1 ? value : distinctCount > 1 ? multipleValuesLabel : null;
    }
}
