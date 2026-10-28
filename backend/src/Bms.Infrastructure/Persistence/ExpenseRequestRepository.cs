using System.Data;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.ExpenseRequests;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed partial class ExpenseRequestRepository(
    BmsDbContext dbContext,
    ExpenseRequestAccessMoment? accessMoment = null, IBudgetCashConsumptionReader? cashConsumption=null,
    IBudgetRepository? budgetRepository=null) : IExpenseRequestRepository
{
    private DateTimeOffset AuthorizationAtUtc => accessMoment?.UtcNow ?? DateTimeOffset.UtcNow;

    public void Add(ExpenseRequest expenseRequest) => dbContext.ExpenseRequests.Add(expenseRequest);

    public async Task<ExpenseRequest?> FindOwnedForUpdateAsync(
        Guid id,
        string actorExternalId,
        CancellationToken cancellationToken)
    {
        EnsureBusinessTransaction();
        await RequestApplicationLock.AcquireAsync(dbContext, id, exclusive: true, cancellationToken);
        return await dbContext.ExpenseRequests
            .FromSqlInterpolated($$"""
                SELECT [request].*
                FROM [bms_web].[ExpenseRequests] AS [request]
                    WITH (UPDLOCK, HOLDLOCK, INDEX([PK_bms_web_ExpenseRequests]))
                WHERE [request].[Id] = {{id}}
                  AND [request].[InitiatorExternalId] = {{actorExternalId}}
                """)
            .Include(request => request.Lines)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ExpenseRequest?> FindForWorkflowUpdateAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        EnsureBusinessTransaction();
        await RequestApplicationLock.AcquireAsync(dbContext, id, exclusive: true, cancellationToken);
        return await dbContext.ExpenseRequests
            .FromSqlInterpolated($$"""
                SELECT [request].*
                FROM [bms_web].[ExpenseRequests] AS [request]
                    WITH (UPDLOCK, HOLDLOCK, INDEX([PK_bms_web_ExpenseRequests]))
                WHERE [request].[Id] = {{id}}
                """)
            .Include(request => request.Lines)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ExpenseRequest?> FindOwnedForCopyAsync(
        Guid id,
        string actorExternalId,
        CancellationToken cancellationToken)
    {
        EnsureBusinessTransaction();
        await RequestApplicationLock.AcquireAsync(dbContext, id, exclusive: false, cancellationToken);
        return await dbContext.ExpenseRequests
            .FromSqlInterpolated($$"""
                SELECT [request].*
                FROM [bms_web].[ExpenseRequests] AS [request]
                    WITH (HOLDLOCK, INDEX([PK_bms_web_ExpenseRequests]))
                WHERE [request].[Id] = {{id}}
                  AND [request].[InitiatorExternalId] = {{actorExternalId}}
                """)
            .AsNoTracking()
            .Include(request => request.Lines)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ExpenseRequest?> FindVisibleAsync(
        Guid id,
        string actorExternalId,
        bool includeAll,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorExternalId);
        cancellationToken.ThrowIfCancellationRequested();
        if (dbContext.Database.CurrentTransaction is null)
        {
            // Discovery also calls this repository without an outer transaction.
            // Keep role/owner locks until the card is read, including every retry.
            return await new SqlServerBusinessTransactionRunner(dbContext).ExecuteAsync(
                token => FindVisibleAsync(id, actorExternalId, includeAll, token),
                cancellationToken);
        }

        EnsureBusinessTransaction();
        var scope = await BusinessDataReadScopeQuery.ResolveAsync(
            dbContext, actorExternalId, cancellationToken);
        if (!scope.HasAny)
        {
            return null;
        }

        // includeAll is a legacy hint, never an authority. Resolve the current
        await RequestApplicationLock.AcquireAsync(dbContext, id, exclusive: false, cancellationToken);
        // SQL scope once instead of repeating its identity/role joins for every
        // header and line predicate in one large detail query.
        return await VisibleDetailQuery(id, scope).SingleOrDefaultAsync(cancellationToken);
    }

    internal IQueryable<ExpenseRequest> VisibleDetailQuery(Guid id, BusinessDataReadScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var query = dbContext.ExpenseRequests.AsNoTracking().Where(request => request.Id == id);
        // Detail and list must share the same captured policy, including the
        // organization-bound AwaitingNsi queue. This is read scope only; command
        // discovery/execution independently rechecks the selected role and state.
        query = BusinessDataReadScopeQuery.ApplyCapturedRequests(query, scope);

        return query.Include(request => request.Lines);
    }

    public Task<ExpenseRequest?> FindForAttachmentAuthorizationAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        dbContext.ExpenseRequests
            .AsNoTracking()
            .Include(request => request.Lines)
            .SingleOrDefaultAsync(request => request.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ExpenseRequest>> ListVisibleAsync(
        string actorExternalId,
        bool includeAll,
        ExpenseRequestFilter filter,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var visible = await ScopedVisibleQueryAsync(
            actorExternalId, includeAll, filter, cancellationToken);
        if(filter.SortBy=="budget"&&cashConsumption is not null)
            return await ListByEffectiveBudgetAsync(visible,filter,skip,take,cancellationToken);
        var query = ApplyListOrder(
            visible, filter)
                .Include(request => request.Lines);
        return await query.Skip(skip).Take(take).ToListAsync(cancellationToken);
    }

    internal static IQueryable<ExpenseRequest> ApplyDefaultPriorityOrder(
        IQueryable<ExpenseRequest> query, IQueryable<BusinessAuditEntry>? audit = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        // Retain the helper name for existing callers, not the old urgency priority.
        return audit is null
            ? query.OrderByDescending(request => request.CreatedAtUtc).ThenByDescending(request => request.Id)
            : OrderRequestDates(query, audit, true);
    }

    public async Task<IReadOnlyList<Guid>> ListOpenableIdsAsync(
        string actorExternalId,
        bool includeAll,
        IReadOnlyCollection<Guid> requestIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestIds);
        if (requestIds.Count == 0)
        {
            return [];
        }

        var requestedIds = requestIds.Distinct().ToArray();
        var query = dbContext.ExpenseRequests
            .AsNoTracking()
            .Where(request => requestedIds.Contains(request.Id));
        query = ExpenseRequestVisibilityQuery.ApplyDetail(
            dbContext,
            query,
            actorExternalId,
            includeAll,
            AuthorizationAtUtc);
        return await query
            .OrderBy(request => request.Id)
            .Select(request => request.Id)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<int> CountVisibleAsync(
        string actorExternalId,
        bool includeAll,
        ExpenseRequestFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var visible = await ScopedVisibleQueryAsync(
            actorExternalId, includeAll, filter, cancellationToken);
        return await visible.CountAsync(cancellationToken);
    }

    private async Task<IQueryable<ExpenseRequest>> ScopedVisibleQueryAsync(
        string actorExternalId,
        bool includeAll,
        ExpenseRequestFilter filter,
        CancellationToken cancellationToken)
    {
        var query = VisibleQuery(actorExternalId, includeAll, filter);
        if (!filter.OnlyMyTasks)
        {
            return query;
        }

        EnsureBusinessTransaction();
        var employees = BusinessDataReadScopeQuery.EmployeeIds(dbContext, actorExternalId);
        var employeeIds = await employees.Take(2).ToArrayAsync(cancellationToken);
        if (employeeIds.Length != 1)
        {
            return query.Where(_ => false);
        }

        var roles = await BusinessDataReadScopeQuery.RoleCodes(dbContext, employees)
            .Distinct().ToArrayAsync(cancellationToken);
        if (roles.Contains(Bms.Domain.Workflow.ExpenseRequestWorkflowCodes.NsiSpecialistRoleCode)
            || roles.Contains(Bms.Domain.Workflow.ExpenseRequestWorkflowCodes.TreasurerRoleCode))
        {
            // Those inboxes also contain requests without an approval participant.
            return query;
        }

        // Approval-only inboxes must not look up every unrelated draft merely
        // to inspect its mutable status. At 5,000 drafts those Serializable
        // shared row locks escalated to a table S lock and starved status writers.
        // This indexed semi-join is only a candidate bound: VisibleQuery still
        // applies the complete current role/CFO/route/stage authorization.
        // Resolve identity/roles inside the same transaction, on every retry;
        // neither legacy includeAll nor a cached session creates authority.
        const int maximumCandidateParameters = 1000;
        var candidateIds = await dbContext.ApprovalRouteStageParticipants.AsNoTracking()
            .Where(participant => employeeIds.Contains(participant.EmployeeId))
            .Select(participant => participant.ExpenseRequestId).Distinct()
            .Take(maximumCandidateParameters + 1).ToArrayAsync(cancellationToken);
        if (candidateIds.Length > maximumCandidateParameters)
        {
            // A technical optimization, never a result limit. Large historical
            // participant sets retain the complete original SQL predicate.
            return query;
        }
        return query.Where(request => candidateIds.Contains(request.Id));
    }

    internal IQueryable<ExpenseRequest> VisibleQuery(
        string actorExternalId,
        bool includeAll,
        ExpenseRequestFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        // The task capture and a status writer must not acquire the mutable
        // status index before the clustered request row in opposite orders.
        // W600 reproduces retry exhaustion on that lookup cycle. Keep shared
        // Serializable reads on a stable index that a workflow status update
        // does not modify. When the caller selected an organization, W629 also
        // constrains that stable root before EF composes the task predicate;
        // otherwise the old clustered-root fallback remains exact.
        // Ordinary lists retain the optimizer's existing choice.
        var source = filter.OnlyMyTasks
            ? filter.OrganizationId.HasValue
                ? dbContext.ExpenseRequests.FromSqlInterpolated($$"""
                    SELECT [request].*
                    FROM [bms_web].[ExpenseRequests] AS [request]
                        WITH (INDEX([IX_bms_web_ExpenseRequests_OrganizationId_Id]))
                    WHERE [request].[OrganizationId] = {{filter.OrganizationId.Value}}
                    """)
                : dbContext.ExpenseRequests.FromSqlRaw("""
                    SELECT [request].*
                    FROM [bms_web].[ExpenseRequests] AS [request]
                        WITH (INDEX([PK_bms_web_ExpenseRequests]))
                    """)
            : dbContext.ExpenseRequests;
        var query = ExpenseRequestVisibilityQuery.ApplyListScope(
            dbContext,
            source.AsNoTracking(),
            actorExternalId,
            includeAll,
            AuthorizationAtUtc,
            filter.OnlyMyTasks);
        query = ExpenseRequestSearchQuery.Apply(query, filter.Search);
        if (filter.HideDrafts)
            query = query.Where(request => request.Status != ExpenseRequestStatus.Draft
                && request.Status != ExpenseRequestStatus.Cancelled);
        query = ExpenseRequestInitiatorFilterQuery.Apply(dbContext, query, filter.InitiatorEmployeeId);
        query = filter.IsUrgent.HasValue
            ? query.Where(request => request.IsUrgent == filter.IsUrgent.Value)
            : query;
        query = filter.PlannedPaymentDate.HasValue
            ? query.Where(request => request.PlannedPaymentDate == filter.PlannedPaymentDate.Value)
            : query;
        query = filter.OrganizationId.HasValue
            ? query.Where(request => request.OrganizationId == filter.OrganizationId.Value)
            : query;
        query = filter.Status.HasValue
            ? query.Where(request => request.Status == filter.Status.Value)
            : query;
        query = filter.CostCenterId.HasValue
            ? query.Where(request => request.DefaultCostCenterId == filter.CostCenterId.Value)
            : query;
        query = filter.CounterpartyId.HasValue
            ? query.Where(request => request.CounterpartyId == filter.CounterpartyId.Value)
            : query;
        query = filter.InitiatorExternalId is not null
            ? query.Where(request => request.InitiatorExternalId == filter.InitiatorExternalId)
            : query;
        query = filter.CreatedFromUtc.HasValue
            ? query.Where(request => request.CreatedAtUtc >= filter.CreatedFromUtc.Value)
            : query;
        return filter.CreatedBeforeUtc.HasValue
            ? query.Where(request => request.CreatedAtUtc < filter.CreatedBeforeUtc.Value)
            : query;
    }

    private void EnsureBusinessTransaction()
    {
        var transaction = dbContext.Database.CurrentTransaction;
        if (transaction is null ||
            transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
        {
            throw new InvalidOperationException(
                "An expense request lookup requires a Serializable business transaction.");
        }
    }
}
