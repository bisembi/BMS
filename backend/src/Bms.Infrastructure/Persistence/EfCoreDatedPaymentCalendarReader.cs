using System.Data;
using Bms.Application.ExpenseRequests;
using Bms.Application.Treasury;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Captures the visible dated calendar scope inside the caller's Serializable
/// transaction with a fixed number of batched EF Core commands: one count
/// statement, then requests with lines, the full payment ledger, every
/// allocation with its possibly missing source, labels with openability, and
/// the currency directory. A request is a candidate when its plan date or a
/// bank/Applied instant falls inside a UTC superset of the period. Exact
/// business-date membership is decided in the explicit business zone BEFORE any
/// ledger row is converted, so a merely adjacent candidate cannot fail this
/// period; a selected request keeps all of its evidence. Raw provider failures
/// are deliberately not caught: the service owns retry and redaction.
/// </summary>
public sealed class EfCoreDatedPaymentCalendarReader(
    BmsDbContext dbContext,
    ExpenseRequestAccessMoment? accessMoment = null) : IDatedPaymentCalendarReader
{
    internal const string AppliedStatus = "Applied";

    /// <summary>
    /// The latest instant datetimeoffset(3) stores. A whole millisecond cannot be
    /// rounded past the type range when it is sent as a scale-3 parameter.
    /// </summary>
    internal static readonly DateTimeOffset LatestStoredInstant =
        new(9999, 12, 31, 23, 59, 59, 999, TimeSpan.Zero);

    public async Task<IReadOnlyList<DatedPaymentCalendarRequestCapture>> ReadVisibleScopeAsync(
        string actorExternalId,
        bool includeAll,
        DatedPaymentCalendarPeriod period,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(period);
        if (string.IsNullOrWhiteSpace(actorExternalId))
        {
            throw new ArgumentException("Actor external id is required.", nameof(actorExternalId));
        }
        if (period.BusinessTimeZone is null || period.From > period.Through)
        {
            throw new ArgumentException(
                "An explicit business time zone and an ordered period are required.", nameof(period));
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Refused before a query is composed or a connection is opened.
        if (dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
        {
            throw new InvalidOperationException(
                "The dated payment calendar capture requires the caller's Serializable transaction.");
        }

        var authorizationAtUtc = accessMoment?.UtcNow ?? DateTimeOffset.UtcNow;
        var candidates = CandidateQuery(dbContext, actorExternalId, includeAll, period, authorizationAtUtc);
        var counts = await CountQuery(dbContext, candidates).FirstOrDefaultAsync(cancellationToken)
            ?? new CaptureCounts(0, 0, 0, 0);
        EnsureWithinCeilings(counts);
        if (counts.Requests == 0)
        {
            if (counts.Lines != 0 || counts.PaymentFacts != 0 || counts.Allocations != 0)
            {
                throw new PaymentCalendarUnavailableException();
            }
            return [];
        }

        var candidateIds = candidates.Select(request => request.Id);
        var requests = await RequestQuery(candidates).ToArrayAsync(cancellationToken);
        var facts = await LedgerQuery(dbContext, candidateIds).ToArrayAsync(cancellationToken);
        var allocations = await AllocationQuery(dbContext, candidateIds).ToArrayAsync(cancellationToken);
        var metadata = await MetadataQuery(dbContext, candidates, actorExternalId, includeAll, authorizationAtUtc)
            .ToArrayAsync(cancellationToken);
        EnsureCaptured(counts, requests, facts, allocations, metadata, cancellationToken);

        var members = SelectPeriodMembers(period, requests, facts, allocations, cancellationToken);
        if (members.Count == 0)
        {
            return [];
        }

        var codes = requests.Where(request => members.Contains(request.Id))
            .Select(request => request.CurrencyCode)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var currencies = await CurrencyQuery(dbContext, codes).ToArrayAsync(cancellationToken);
        if (currencies.Length > codes.Length)
        {
            // Only a duplicate or a non-ordinal collation match can exceed the codes.
            throw new PaymentCalendarUnavailableException();
        }

        return Assemble(members, requests, facts, allocations, metadata, currencies, cancellationToken);
    }

    internal sealed record CaptureCounts(int Requests, int Lines, int PaymentFacts, int Allocations);

    /// <summary>Null source columns are a lost source, kept visible to the projector.</summary>
    internal sealed record AllocationRow(
        Guid AllocationId,
        Guid TargetExpenseRequestId,
        Guid? OrganizationId,
        Guid? CounterpartyId,
        Guid? CurrencyId,
        decimal Amount,
        decimal AmountKzt,
        string Status,
        DateTimeOffset ReservedAt,
        DateTimeOffset? AppliedOrReleasedAt);

    internal sealed record MetadataRow(
        Guid ExpenseRequestId,
        int CounterpartyMatches,
        string? CounterpartyName,
        int OrganizationMatches,
        string? OrganizationCode,
        string? OrganizationName,
        bool CanOpen);

    internal sealed record CurrencyRow(Guid Id, string Code);

    /// <summary>
    /// Inclusive UTC superset of every instant whose business date can fall in
    /// the period. TimeZoneInfo offsets are bounded by 14 hours; one whole day on
    /// each side covers them. DateOnly limits clamp to the stored range.
    /// </summary>
    internal static (DateTimeOffset Lower, DateTimeOffset Upper) CandidateWindow(DatedPaymentCalendarPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);
        var lower = period.From > DateOnly.MinValue
            ? new DateTimeOffset(period.From.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : DateTimeOffset.MinValue;
        var upper = period.Through < DateOnly.MaxValue.AddDays(-1)
            ? new DateTimeOffset(period.Through.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : LatestStoredInstant;
        return (lower, upper);
    }

    /// <summary>
    /// The four payment statuses, a plan/bank/Applied candidate predicate and the
    /// named calendar list boundary, which does not add substitution visibility.
    /// </summary>
    internal static IQueryable<ExpenseRequest> CandidateQuery(
        BmsDbContext dbContext,
        string actorExternalId,
        bool includeAll,
        DatedPaymentCalendarPeriod period,
        DateTimeOffset authorizationAtUtc)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(period);
        var from = period.From;
        var through = period.Through;
        var (lower, upper) = CandidateWindow(period);
        var forecast=period.ForecastPlanDate.HasValue;
        var rollInside=period.ForecastPlanDate>=from&&period.ForecastPlanDate<=through;
        var source = dbContext.ExpenseRequests
            .AsNoTracking()
            .Where(request =>
                (request.Status == ExpenseRequestStatus.ToPay
                    || request.Status == ExpenseRequestStatus.PartiallyPaid
                    || request.Status == ExpenseRequestStatus.Paid
                    || request.Status == ExpenseRequestStatus.Closed
                    || forecast&&(request.Status==ExpenseRequestStatus.InApproval||request.Status==ExpenseRequestStatus.Returned
                        ||request.Status==ExpenseRequestStatus.AwaitingNsi||request.Status==ExpenseRequestStatus.Approved))
                && (((!forecast||(request.Status!=ExpenseRequestStatus.PartiallyPaid&&request.Status!=ExpenseRequestStatus.InApproval
                            &&request.Status!=ExpenseRequestStatus.Returned&&request.Status!=ExpenseRequestStatus.AwaitingNsi&&request.Status!=ExpenseRequestStatus.Approved))
                        && request.PlannedPaymentDate != null
                        && request.PlannedPaymentDate >= from
                        && request.PlannedPaymentDate <= through)
                    || rollInside&&(request.Status==ExpenseRequestStatus.PartiallyPaid||request.Status==ExpenseRequestStatus.InApproval
                        ||request.Status==ExpenseRequestStatus.Returned||request.Status==ExpenseRequestStatus.AwaitingNsi||request.Status==ExpenseRequestStatus.Approved)
                    || dbContext.ExpenseRequestPaymentFacts.Any(fact =>
                        fact.ExpenseRequestId == request.Id
                        && fact.PaidAt >= lower
                        && fact.PaidAt <= upper)
                    || dbContext.OverpaymentAllocations.Any(allocation =>
                        allocation.TargetExpenseRequestId == request.Id
                        && allocation.Status == AppliedStatus
                        && allocation.AppliedOrReleasedAt >= lower
                        && allocation.AppliedOrReleasedAt <= upper)));
        return ExpenseRequestVisibilityQuery.ApplyPaymentCalendarScope(
            dbContext,
            source,
            actorExternalId,
            includeAll,
            authorizationAtUtc);
    }

    /// <summary>One statement; nothing is materialized before every ceiling is checked.</summary>
    internal static IQueryable<CaptureCounts> CountQuery(
        BmsDbContext dbContext,
        IQueryable<ExpenseRequest> candidates)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(candidates);
        var ids = candidates.Select(request => request.Id);
        return candidates
            .GroupBy(request => 1)
            .Select(group => new CaptureCounts(
                group.Count(),
                dbContext.ExpenseRequestLines.Count(line => ids.Contains(line.ExpenseRequestId)),
                dbContext.ExpenseRequestPaymentFacts.Count(fact => ids.Contains(fact.ExpenseRequestId)),
                dbContext.OverpaymentAllocations.Count(allocation =>
                    ids.Contains(allocation.TargetExpenseRequestId))));
    }

    internal static IQueryable<ExpenseRequest> RequestQuery(IQueryable<ExpenseRequest> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates
            .OrderBy(request => request.Id)
            .Take(DatedPaymentCalendarCapturePolicy.MaximumRequests + 1)
            .Include(request => request.Lines);
    }

    /// <summary>Every event of each request: no currency, date or reversal filter.</summary>
    internal static IQueryable<ExpenseRequestPaymentFact> LedgerQuery(
        BmsDbContext dbContext,
        IQueryable<Guid> requestIds)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(requestIds);
        return dbContext.ExpenseRequestPaymentFacts
            .AsNoTracking()
            .Where(fact => requestIds.Contains(fact.ExpenseRequestId))
            .OrderBy(fact => fact.ExpenseRequestId)
            .ThenBy(fact => fact.EventId)
            .Take(DatedPaymentCalendarCapturePolicy.MaximumPaymentFacts + 1);
    }

    /// <summary>
    /// Every allocation state. The target controls visibility; a left join keeps
    /// an allocation whose source is missing instead of dropping its amount.
    /// </summary>
    internal static IQueryable<AllocationRow> AllocationQuery(
        BmsDbContext dbContext,
        IQueryable<Guid> requestIds)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(requestIds);
        return (
            from allocation in dbContext.OverpaymentAllocations.AsNoTracking()
            where requestIds.Contains(allocation.TargetExpenseRequestId)
            join source in dbContext.OverpaymentSources.AsNoTracking()
                on allocation.SourceId equals source.SourceId into sources
            from source in sources.DefaultIfEmpty()
            orderby allocation.TargetExpenseRequestId, allocation.AllocationId
            select new AllocationRow(
                allocation.AllocationId,
                allocation.TargetExpenseRequestId,
                (Guid?)source.OrganizationId,
                (Guid?)source.CounterpartyId,
                (Guid?)source.CurrencyId,
                allocation.Amount,
                allocation.AmountKzt,
                allocation.Status,
                allocation.ReservedAt,
                allocation.AppliedOrReleasedAt))
            .Take(DatedPaymentCalendarCapturePolicy.MaximumAllocations + 1);
    }

    /// <summary>
    /// Labels and card openability from the same transaction. Directory labels
    /// are not filtered by IsActive; an unresolved reference stays unlabelled.
    /// </summary>
    internal static IQueryable<MetadataRow> MetadataQuery(
        BmsDbContext dbContext,
        IQueryable<ExpenseRequest> candidates,
        string actorExternalId,
        bool includeAll,
        DateTimeOffset authorizationAtUtc)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(candidates);
        var openable = ExpenseRequestVisibilityQuery.ApplyPaymentCalendarDetail(
            dbContext,
            dbContext.ExpenseRequests.AsNoTracking(),
            actorExternalId,
            includeAll,
            authorizationAtUtc);
        return candidates
            .OrderBy(request => request.Id)
            .Take(DatedPaymentCalendarCapturePolicy.MaximumRequests + 1)
            .Select(request => new MetadataRow(
                request.Id,
                dbContext.Counterparties.Count(counterparty => counterparty.Id == request.CounterpartyId),
                dbContext.Counterparties
                    .Where(counterparty => counterparty.Id == request.CounterpartyId)
                    .Select(counterparty => counterparty.Name)
                    .FirstOrDefault(),
                dbContext.Organizations.Count(organization => organization.Id == request.OrganizationId),
                dbContext.Organizations
                    .Where(organization => organization.Id == request.OrganizationId)
                    .Select(organization => organization.Code)
                    .FirstOrDefault(),
                dbContext.Organizations
                    .Where(organization => organization.Id == request.OrganizationId)
                    .Select(organization => organization.Name)
                    .FirstOrDefault(),
                openable.Any(visible => visible.Id == request.Id)));
    }

    internal static IQueryable<CurrencyRow> CurrencyQuery(BmsDbContext dbContext, IReadOnlyCollection<string> codes)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(codes);
        var values = codes.ToArray();
        return dbContext.Currencies
            .AsNoTracking()
            .Where(currency => values.Contains(currency.Code))
            .OrderBy(currency => currency.Code)
            .ThenBy(currency => currency.Id)
            .Select(currency => new CurrencyRow(currency.Id, currency.Code))
            .Take(values.Length + 1);
    }

    /// <summary>
    /// Exact membership: plan date, bank PaidAt business date or Applied
    /// allocation business date inside the inclusive period. Reads only raw ids,
    /// dates and statuses; no ledger row is converted or validated here.
    /// </summary>
    internal static IReadOnlySet<Guid> SelectPeriodMembers(
        DatedPaymentCalendarPeriod period,
        IReadOnlyList<ExpenseRequest> requests,
        IReadOnlyList<ExpenseRequestPaymentFact> facts,
        IReadOnlyList<AllocationRow> allocations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(period.BusinessTimeZone);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(allocations);
        cancellationToken.ThrowIfCancellationRequested();

        var captured = new HashSet<Guid>();
        var members = new HashSet<Guid>();
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request is null || !captured.Add(request.Id))
            {
                throw new PaymentCalendarUnavailableException();
            }
            if (PaymentCalendarForecast443.PlanDate(request.Status,request.PlannedPaymentDate,period.ForecastPlanDate) is { } planned
                && planned >= period.From && planned <= period.Through)
            {
                members.Add(request.Id);
            }
        }

        foreach (var fact in facts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fact is null || !captured.Contains(fact.ExpenseRequestId))
            {
                throw new PaymentCalendarUnavailableException();
            }
            if (IsInsidePeriod(period, fact.PaidAt))
            {
                members.Add(fact.ExpenseRequestId);
            }
        }

        foreach (var allocation in allocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (allocation is null || !captured.Contains(allocation.TargetExpenseRequestId))
            {
                throw new PaymentCalendarUnavailableException();
            }
            if (string.Equals(allocation.Status, AppliedStatus, StringComparison.Ordinal)
                && allocation.AppliedOrReleasedAt is { } appliedAt
                && IsInsidePeriod(period, appliedAt))
            {
                members.Add(allocation.TargetExpenseRequestId);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return members;
    }

    /// <summary>
    /// Converts only the evidence of selected requests. Lost or duplicate labels,
    /// currency identity or captured rows are unavailable, never a partial scope.
    /// </summary>
    internal static IReadOnlyList<DatedPaymentCalendarRequestCapture> Assemble(
        IReadOnlySet<Guid> members,
        IReadOnlyList<ExpenseRequest> requests,
        IReadOnlyList<ExpenseRequestPaymentFact> facts,
        IReadOnlyList<AllocationRow> allocations,
        IReadOnlyList<MetadataRow> metadata,
        IReadOnlyList<CurrencyRow> currencies,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(allocations);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(currencies);
        cancellationToken.ThrowIfCancellationRequested();

        var captured = new HashSet<Guid>();
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request is null || !captured.Add(request.Id))
            {
                throw new PaymentCalendarUnavailableException();
            }
        }

        var labels = new Dictionary<Guid, MetadataRow>();
        foreach (var row in metadata)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row is null || !captured.Contains(row.ExpenseRequestId) || !labels.TryAdd(row.ExpenseRequestId, row))
            {
                throw new PaymentCalendarUnavailableException();
            }
        }

        var currencyIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var ambiguousCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in currencies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row is null)
            {
                throw new PaymentCalendarUnavailableException();
            }
            if (!currencyIds.TryAdd(row.Code, row.Id))
            {
                ambiguousCodes.Add(row.Code);
            }
        }

        var events = new Dictionary<Guid, List<ConfirmedPaymentEvent>>();
        foreach (var fact in facts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fact is null)
            {
                throw new PaymentCalendarUnavailableException();
            }
            if (members.Contains(fact.ExpenseRequestId))
            {
                ListFor(events, fact.ExpenseRequestId).Add(new ConfirmedPaymentEvent(
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
                    fact.ReversedEventId));
            }
        }

        var slices = new Dictionary<Guid, List<ExpenseRequestAppliedOverpaymentSnapshot>>();
        foreach (var row in allocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row is null)
            {
                throw new PaymentCalendarUnavailableException();
            }
            if (members.Contains(row.TargetExpenseRequestId))
            {
                // Guid.Empty is the observable form of a missing source.
                ListFor(slices, row.TargetExpenseRequestId).Add(new ExpenseRequestAppliedOverpaymentSnapshot(
                    row.AllocationId,
                    row.TargetExpenseRequestId,
                    row.OrganizationId.GetValueOrDefault(),
                    row.CounterpartyId.GetValueOrDefault(),
                    row.CurrencyId.GetValueOrDefault(),
                    row.Amount,
                    row.AmountKzt,
                    row.Status,
                    row.ReservedAt,
                    row.AppliedOrReleasedAt));
            }
        }

        var captures = new List<DatedPaymentCalendarRequestCapture>(members.Count);
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!members.Contains(request.Id))
            {
                continue;
            }
            if (!labels.TryGetValue(request.Id, out var label)
                || label.CounterpartyMatches is < 0 or > 1
                || label.OrganizationMatches is < 0 or > 1
                || ambiguousCodes.Contains(request.CurrencyCode)
                || !currencyIds.TryGetValue(request.CurrencyCode, out var currencyId))
            {
                throw new PaymentCalendarUnavailableException();
            }

            captures.Add(new DatedPaymentCalendarRequestCapture(
                request,
                currencyId,
                label.CounterpartyName,
                label.OrganizationCode,
                label.OrganizationName,
                label.CanOpen,
                events.TryGetValue(request.Id, out var requestEvents) ? requestEvents.AsReadOnly() : [],
                slices.TryGetValue(request.Id, out var requestSlices) ? requestSlices.AsReadOnly() : []));
        }

        if (captures.Count != members.Count)
        {
            throw new PaymentCalendarUnavailableException();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return captures.AsReadOnly();
    }

    private static void EnsureWithinCeilings(CaptureCounts counts)
    {
        if (counts.Requests < 0 || counts.Lines < 0 || counts.PaymentFacts < 0 || counts.Allocations < 0)
        {
            throw new PaymentCalendarUnavailableException();
        }
        if (counts.Requests > DatedPaymentCalendarCapturePolicy.MaximumRequests
            || counts.Lines > DatedPaymentCalendarCapturePolicy.MaximumLines
            || counts.PaymentFacts > DatedPaymentCalendarCapturePolicy.MaximumPaymentFacts
            || counts.Allocations > DatedPaymentCalendarCapturePolicy.MaximumAllocations)
        {
            throw new DatedPaymentCalendarScopeTooLargeException();
        }
    }

    /// <summary>
    /// Each bounded read must return exactly what the count promised inside the
    /// same transaction. A difference is lost or extra evidence, not a smaller period.
    /// </summary>
    private static void EnsureCaptured(
        CaptureCounts counts,
        IReadOnlyList<ExpenseRequest> requests,
        IReadOnlyList<ExpenseRequestPaymentFact> facts,
        IReadOnlyList<AllocationRow> allocations,
        IReadOnlyList<MetadataRow> metadata,
        CancellationToken cancellationToken)
    {
        var lines = 0;
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lines = checked(lines + request.Lines.Count);
        }

        if (requests.Count != counts.Requests
            || lines != counts.Lines
            || facts.Count != counts.PaymentFacts
            || allocations.Count != counts.Allocations
            || metadata.Count != counts.Requests)
        {
            throw new PaymentCalendarUnavailableException();
        }
    }

    /// <summary>
    /// The projector's own conversion. ConvertTime saturates at the
    /// DateTimeOffset range, so DateOnly limits do not overflow.
    /// </summary>
    private static bool IsInsidePeriod(DatedPaymentCalendarPeriod period, DateTimeOffset instant)
    {
        var businessDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(instant, period.BusinessTimeZone).DateTime);
        return businessDate >= period.From && businessDate <= period.Through;
    }

    private static List<T> ListFor<T>(Dictionary<Guid, List<T>> source, Guid requestId)
    {
        if (!source.TryGetValue(requestId, out var items))
        {
            items = [];
            source.Add(requestId, items);
        }
        return items;
    }
}
