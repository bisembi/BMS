using System.Data;
using Bms.Application.Abstractions;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Budgeting;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class ExpenseRequestExportRepository(
    BmsDbContext dbContext,
    IBudgetRepository budgetRepository,
    ExpenseRequestAccessMoment? accessMoment = null)
    : IExpenseRequestExportRepository
{
    internal const int BudgetReadBatchSize = 128;

    public async Task<IReadOnlyList<ExpenseRequestExportRow>> ListVisibleAsync(
        string actorExternalId,
        bool includeAll,
        ExpenseRequestFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = ExpenseRequestVisibilityQuery.ApplyListScope(
            dbContext,
            dbContext.ExpenseRequests.AsNoTracking(),
            actorExternalId,
            includeAll,
            accessMoment?.UtcNow ?? DateTimeOffset.UtcNow,
            filter.OnlyMyTasks);
        // The production export service owns a retryable Serializable attempt.
        // Capture fresh canonical authority once under that attempt's locks,
        // rather than expanding it repeatedly in the large header projection.
        // Standalone/non-Serializable callers retain the existing authority
        // path. The task-specific predicate is still intersected with data scope.
        if (dbContext.Database.IsSqlServer()
            && dbContext.Database.CurrentTransaction is { } transaction
            && transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable)
        {
            var capturedScope = await BusinessDataReadScopeQuery.ResolveAsync(
                dbContext, actorExternalId, cancellationToken);
            query = ExpenseRequestVisibilityQuery.ApplyCapturedListScope(
                dbContext, dbContext.ExpenseRequests.AsNoTracking(),
                actorExternalId, capturedScope, filter.OnlyMyTasks);
        }
        query = ExpenseRequestSearchQuery.Apply(query, filter.Search);
        if (filter.HideDrafts)
            query = query.Where(request => request.Status != ExpenseRequestStatus.Draft
                && request.Status != ExpenseRequestStatus.Cancelled);
        query = ExpenseRequestInitiatorFilterQuery.Apply(dbContext, query, filter.InitiatorEmployeeId);
        if (filter.IsUrgent.HasValue)
        {
            query = query.Where(request => request.IsUrgent == filter.IsUrgent.Value);
        }
        if (filter.PlannedPaymentDate.HasValue)
        {
            query = query.Where(
                request => request.PlannedPaymentDate == filter.PlannedPaymentDate.Value);
        }
        if (filter.OrganizationId.HasValue)
        {
            query = query.Where(request => request.OrganizationId == filter.OrganizationId.Value);
        }
        if (filter.Status.HasValue)
        {
            query = query.Where(request => request.Status == filter.Status.Value);
        }
        if (filter.CostCenterId.HasValue)
        {
            query = query.Where(request => request.DefaultCostCenterId == filter.CostCenterId.Value);
        }
        if (filter.CounterpartyId.HasValue)
        {
            query = query.Where(request => request.CounterpartyId == filter.CounterpartyId.Value);
        }
        if (filter.InitiatorExternalId is not null)
        {
            query = query.Where(
                request => request.InitiatorExternalId == filter.InitiatorExternalId);
        }
        if (filter.CreatedFromUtc.HasValue)
        {
            query = query.Where(request => request.CreatedAtUtc >= filter.CreatedFromUtc.Value);
        }
        if (filter.CreatedBeforeUtc.HasValue)
        {
            query = query.Where(request => request.CreatedAtUtc < filter.CreatedBeforeUtc.Value);
        }

        var records = await ExpenseRequestRepository.ApplyDefaultPriorityOrder(query, dbContext.BusinessAuditEntries)
            .Select(request => new
            {
                request.Id,
                request.CreatedAtUtc,
                request.PlannedPaymentDate,
                request.Purpose,
                OrganizationName = dbContext.Organizations
                    .Where(organization => organization.Id == request.OrganizationId)
                    .Select(organization => organization.Name)
                    .SingleOrDefault(),
                CostCenterName = dbContext.CostCenters
                    .Where(costCenter => costCenter.Id == request.DefaultCostCenterId)
                    .Select(costCenter => costCenter.Name)
                    .SingleOrDefault(),
                CounterpartyName = request.IsNewCounterparty
                    ? request.CandidateCounterpartyName
                    : dbContext.Counterparties
                        .Where(counterparty =>
                            counterparty.Id == request.CounterpartyId &&
                            counterparty.IsActive)
                        .Select(counterparty => counterparty.Name)
                        .SingleOrDefault(),
                request.InitiatorExternalId,
                request.Status,
                request.PaymentOperation,
                request.IsUrgent,
                TotalAmount = request.Lines.Sum(line => line.Amount),
                request.CurrencyCode,
                TotalAmountKzt = request.Lines.All(line => line.AmountKzt.HasValue)
                    && (request.Lines.Any() || request.CurrencyCode == "KZT")
                        ? request.Lines.Sum(line => line.AmountKzt)
                        : null
            })
            .ToArrayAsync(cancellationToken);

        // The active register is the authoritative indicator below. Do not
        // build and discard a legacy projection (and another full register
        // frame for every 128 budget keys) when that result cannot be used.
        var registerDecisions343=await budgetRepository.ReadFifoDecisions343Async(records.Select(r=>r.Id).ToArray(),cancellationToken);
        var currentRecords = records.Where(record => registerDecisions343 is null
            && record.Status is not (ExpenseRequestStatus.Paid or ExpenseRequestStatus.Closed)).ToArray();
        var requestIds = currentRecords.Select(record => record.Id).ToArray();
        var requestLineIdentities = new List<ExpenseRequestLineIdentity>();
        var projections = new List<BudgetRequestProjection>();
        foreach (var requestIdBatch in requestIds.Chunk(BudgetReadBatchSize))
        {
            requestLineIdentities.AddRange(await dbContext.ExpenseRequestLines
                .AsNoTracking()
                .Where(line => requestIdBatch.Contains(line.ExpenseRequestId))
                .Select(line => new ExpenseRequestLineIdentity(
                    line.ExpenseRequestId,
                    line.Id, line.CostCenterId, line.CashFlowItemId))
                .ToArrayAsync(cancellationToken));
            projections.AddRange(await budgetRepository.ListRequestProjectionsAsync(
                requestIdBatch,
                cancellationToken));
        }

        var lineIdsByRequest = requestLineIdentities
            .GroupBy(line => line.ExpenseRequestId)
            .ToDictionary(group => group.Key, group =>
                (IReadOnlyCollection<Guid>)group.Select(line => line.Id).ToArray());
        var positions = new List<BudgetPosition>();
        var linesByRequest = requestLineIdentities.ToLookup(line => line.ExpenseRequestId);
        var scopes = currentRecords.Select(record => new ExpenseRequestBudgetIndicatorScope(
            record.Id, lineIdsByRequest.GetValueOrDefault(record.Id, []),
            record.PlannedPaymentDate is { } date
                ? linesByRequest[record.Id].Where(line => line.CostCenterId.HasValue && line.CostCenterId != Guid.Empty
                        && line.CashFlowItemId.HasValue && line.CashFlowItemId != Guid.Empty)
                    .Select(line => BudgetKey.Create(line.CostCenterId.GetValueOrDefault(), line.CashFlowItemId.GetValueOrDefault(), date))
                    .Distinct().ToArray()
                : [])).ToArray();
        var budgetKeys = projections
            .Select(projection => projection.Key)
            .Concat(scopes.SelectMany(scope => scope.BudgetKeys ?? []))
            .Distinct()
            .ToArray();
        foreach (var budgetKeyBatch in budgetKeys.Chunk(BudgetReadBatchSize))
        {
            positions.AddRange(await budgetRepository.ListPositionsAsync(
                budgetKeyBatch,
                cancellationToken));
        }

        var indicators = ExpenseRequestBudgetIndicatorEvaluator.Evaluate(
            scopes,
            projections,
            positions);
        var cancelledIndicators = new Dictionary<Guid, bool>();
        foreach (var batch in records.Where(record => registerDecisions343 is null
            && record.Status is (ExpenseRequestStatus.Cancelled or ExpenseRequestStatus.Paid or ExpenseRequestStatus.Closed))
            .Select(record => record.Id).Chunk(BudgetReadBatchSize))
        {
            var historical = await budgetRepository.ReadCancelledSubmissionBudgetIndicatorsAsync(batch, cancellationToken);
            if (historical.Keys.Any(id => !batch.Contains(id)))
                throw new InvalidOperationException("Cancellation export budget scope is inconsistent.");
            foreach (var pair in historical) cancelledIndicators.Add(pair.Key, pair.Value);
        }

        return records.Select(record => new ExpenseRequestExportRow(
            record.CreatedAtUtc,
            record.PlannedPaymentDate,
            record.Purpose,
            record.OrganizationName,
            record.CostCenterName,
            record.PaymentOperation is { IsBusinessTrip: true, EmployeeNames: { Count: > 0 } employees }
                ? string.Join("; ", employees) : record.CounterpartyName,
            record.InitiatorExternalId,
            record.Status.ToCode(),
            record.IsUrgent,
            record.TotalAmount,
            record.CurrencyCode,
            record.TotalAmountKzt,
            record.PaymentOperation?.IsPayrollAdvance == true ? null : registerDecisions343 is not null
                ?registerDecisions343.TryGetValue(record.Id,out var decision343)?decision343.IsOverBudget:(bool?)null
                :cancelledIndicators.TryGetValue(record.Id, out var historicalOverBudget)
                ? historicalOverBudget : record.Status is ExpenseRequestStatus.Paid or ExpenseRequestStatus.Closed
                    ? null : indicators[record.Id])).ToArray();
    }

    private sealed record ExpenseRequestLineIdentity(
        Guid ExpenseRequestId,
        Guid Id,
        Guid? CostCenterId,
        Guid? CashFlowItemId);
}
