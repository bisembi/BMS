using Bms.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed partial class ExpenseRequestHistoryRepository(
    BmsDbContext dbContext,
    ExpenseRequestAccessMoment? accessMoment = null) :
    IExpenseRequestHistoryRepository
{
    private const string ExpenseRequestObjectType = "ExpenseRequest";

    public Task<VisibleExpenseRequestAuditPage?> ListVisibleAsync(
        Guid expenseRequestId,
        string actorExternalId,
        bool includeAll,
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        ListCoreAsync(expenseRequestId, actorExternalId, includeAll, skip, take, false, cancellationToken);

    public Task<VisibleExpenseRequestAuditPage?> ListCommentedWorkflowVisibleAsync(
        Guid expenseRequestId, string actorExternalId, bool includeAll,
        int skip, int take, CancellationToken cancellationToken) =>
        ListCoreAsync(expenseRequestId, actorExternalId, includeAll, skip, take, true, cancellationToken);

    private async Task<VisibleExpenseRequestAuditPage?> ListCoreAsync(
        Guid expenseRequestId, string actorExternalId, bool includeAll,
        int skip, int take, bool commentsOnly, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorExternalId))
        {
            throw new ArgumentException("Actor external id is required.", nameof(actorExternalId));
        }

        var requestQuery = dbContext.ExpenseRequests
            .AsNoTracking()
            .Where(request => request.Id == expenseRequestId);
        requestQuery = ExpenseRequestVisibilityQuery.Apply(
            dbContext,
            requestQuery,
            actorExternalId,
            includeAll,
            accessMoment?.UtcNow ?? DateTimeOffset.UtcNow);

        if (!await requestQuery.AnyAsync(cancellationToken))
        {
            return null;
        }

        var objectId = expenseRequestId.ToString("D");
        // The comment is still projected and historical snapshot visibility is
        // still checked by the service. SQL filters BEFORE pagination, so an
        // old approval note is not lost behind more recent import events.
        var entries = commentsOnly
            ? dbContext.BusinessAuditEntries.FromSqlRaw("""
                SELECT * FROM bms_web.BusinessAudit
                WHERE Action IN (N'WorkflowApproved',N'WorkflowReturned',N'WorkflowRejected',
                    N'WorkflowCancelled',N'WorkflowNsiConfirmed',N'WorkflowSentToPayment')
                AND NULLIF(LTRIM(RTRIM(JSON_VALUE(
                    CASE WHEN ISJSON(AfterJson)=1 THEN AfterJson ELSE N'[]' END, '$.reason'))),N'') IS NOT NULL
                """)
            : dbContext.BusinessAuditEntries;
        var historyQuery = entries
            .AsNoTracking()
            .Where(entry => entry.ObjectType == ExpenseRequestObjectType
                && entry.ObjectId == objectId);
        var total = await historyQuery.CountAsync(cancellationToken);
        var items = await historyQuery
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Skip(skip)
            .Take(take)
            .Select(entry => new ExpenseRequestAuditSnapshotRow(
                entry.OccurredAtUtc,
                entry.ActorExternalId,
                entry.Action,
                entry.BeforeJson,
                entry.AfterJson))
            .ToListAsync(cancellationToken);

        // ActorExternalId is the persisted author, not the request initiator or
        // the current approver. Mapping keys cannot be reassigned to another
        // employee through AuthorizationDirectoryStore. Keep inactive mappings
        // and employees so revocation does not erase a former author's name.
        var actors = items.Select(item => item.ActorExternalId).Distinct(StringComparer.Ordinal).ToArray();
        var authors = await (from mapping in dbContext.EmployeeActorMappings.AsNoTracking()
            where actors.Contains(mapping.ActorExternalId)
            join employee in dbContext.Employees.AsNoTracking() on mapping.EmployeeId equals employee.Id
            select new { mapping.ActorExternalId, mapping.EmployeeId, employee.Name })
            .ToArrayAsync(cancellationToken);
        var names = authors.GroupBy(author => author.ActorExternalId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Distinct().Take(2).ToArray(), StringComparer.Ordinal);
        var references = await ResolveReferencesAsync(items, cancellationToken);
        return new VisibleExpenseRequestAuditPage(items.Select(item => item with
        {
            ActorName = AutomaticActorName482(item.Action, item.ActorExternalId)
                ?? (names.TryGetValue(item.ActorExternalId, out var candidates) && candidates.Length == 1
                    ? candidates[0].Name : "Автор не определён"),
            References = references
        }).ToArray(), total);
    }

    // Only the exact persisted accounting worker identity and action qualify.
    // Human-approved allocations retain the actual employee; unknown accounts
    // are never guessed to be automatic from their name or a SID prefix.
    internal static string? AutomaticActorName482(string action, string actor) =>
        action == "PaymentApplied" && actor == AccountingPaymentReconciliation.ServiceActor
            ? "Автоматически BMS" : null;
}
