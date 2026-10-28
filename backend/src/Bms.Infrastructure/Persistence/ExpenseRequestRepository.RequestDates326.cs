using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed partial class ExpenseRequestRepository
{
    // These are the durable, transactional author submission events. Approval,
    // editing, copying and treasury route restarts must never move this clock.
    private static IQueryable<RequestDateRow> RequestDates(IQueryable<ExpenseRequest> requests,
        IQueryable<BusinessAuditEntry> audit)
    {
        var submissions = audit.Where(a => a.ObjectType == "ExpenseRequest"
            && (a.Action == "SubmittedForApproval" || a.Action == "SubmittedApproved"
                || a.Action == "SubmittedToNsi" || a.Action == "SubmittedToPayment"))
            .GroupBy(a => a.ObjectId)
            .Select(g => new { ObjectId = g.Key, SubmittedAtUtc = g.Min(a => (DateTimeOffset?)a.OccurredAtUtc) });
        return from request in requests
            join submission in submissions on request.Id.ToString() equals submission.ObjectId into dates
            from submission in dates.DefaultIfEmpty()
            select new RequestDateRow { Request = request, Date = submission.SubmittedAtUtc ?? request.CreatedAtUtc };
    }

    private static IQueryable<ExpenseRequest> OrderRequestDates(IQueryable<ExpenseRequest> requests,
        IQueryable<BusinessAuditEntry> audit, bool descending)
    {
        var rows = RequestDates(requests, audit);
        return (descending
            ? rows.OrderByDescending(r => r.Date).ThenByDescending(r => r.Request.Id)
            : rows.OrderBy(r => r.Date).ThenBy(r => r.Request.Id)).Select(r => r.Request);
    }

    public async Task<IReadOnlyDictionary<Guid, DateTimeOffset>> ReadRequestDatesAsync(
        IReadOnlyCollection<Guid> visibleRequestIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visibleRequestIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (visibleRequestIds.Count == 0) return new Dictionary<Guid, DateTimeOffset>();
        if (visibleRequestIds.Count > 100) throw new ArgumentOutOfRangeException(nameof(visibleRequestIds));
        return await RequestDates(dbContext.ExpenseRequests.AsNoTracking().Where(r => visibleRequestIds.Contains(r.Id)),
                dbContext.BusinessAuditEntries.AsNoTracking())
            .ToDictionaryAsync(r => r.Request.Id, r => r.Date, cancellationToken);
    }

    private sealed class RequestDateRow
    {
        public required ExpenseRequest Request { get; init; }
        public DateTimeOffset Date { get; set; }
    }
}
