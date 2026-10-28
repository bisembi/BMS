using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.Treasury;

public interface IGoodsCalendarArticles458
{
    Task<IReadOnlyDictionary<Guid, string>> ReadAsync(CancellationToken token);
}

public sealed record GoodsCalendarCategory458(string Code, PaymentCalendarGridDto Calendar);
public sealed record GoodsCalendarDto458(PaymentCalendarGridDto Calendar, IReadOnlyList<GoodsCalendarCategory458> Categories)
{
    public Guid? ActualsSnapshotId { get; init; }
    public DateTimeOffset? ActualsCapturedAtUtc { get; init; }
    public string ActualsSource { get; init; } = "ODDS";
}

public sealed record GoodsCalendarActual469(Guid CashFlowItemId, PaymentCalendarGridEntry Entry);
public sealed record GoodsCalendarActuals469(Guid? SnapshotId, DateTimeOffset? CapturedAtUtc,
    IReadOnlyList<GoodsCalendarActual469> Entries);
public interface IGoodsCalendarActuals469
{
    Task<GoodsCalendarActuals469> ReadAsync(PaymentCalendarGridQuery query,
        IReadOnlyDictionary<Guid,string> articles, IReadOnlyList<DatedPaymentCalendarRequestCapture> captures,
        CancellationToken token);
}

/// Read-only analytical attribution. The complete authorized ledger and existing
/// forecast projector are reused; no request, payment, allocation or reserve is posted.
public static class GoodsCalendarComposer458
{
    public static readonly IReadOnlyList<string> Codes = Array.AsReadOnly(new[] { "PREPAYMENT", "CONSIGNMENT", "REALIZATION" });

    public static GoodsCalendarDto458 Compose(PaymentCalendarGridQuery query, TimeZoneInfo zone,
        IReadOnlyList<DatedPaymentCalendarRequestCapture> captures, IReadOnlyDictionary<Guid, string> articles,
        CancellationToken token, DateOnly forecastDate)
    {
        var full = PaymentCalendarGridComposer434.Compose(query, zone, captures, token, forecastDate);
        if (articles.Count != 3 || articles.Keys.Any(id => id == Guid.Empty)
            || !articles.Values.Order().SequenceEqual(Codes.Order())) throw new DatedPaymentCalendarProjectionException();
        var requests = captures.ToDictionary(c => c.Request.Id, c => c.Request);
        var buckets = Codes.ToDictionary(code => code, _ => new List<PaymentCalendarGridEntry>(), StringComparer.Ordinal);
        foreach (var entry in full.Entries)
        {
            token.ThrowIfCancellationRequested();
            var request = requests[entry.ExpenseRequestId ?? throw new DatedPaymentCalendarProjectionException()];
            // Preserve stable line order and include NON-goods lines in the
            // denominator. Never classify an entire mixed request as goods.
            var shares = request.Lines.OrderBy(l => l.SortOrder).ThenBy(l => l.Id.ToString("N"), StringComparer.Ordinal)
                .GroupBy(l => l.CashFlowItemId is { } id && articles.TryGetValue(id, out var code) ? code : "OTHER")
                .Select(g => (Code: g.Key, Weight: g.Sum(l => l.Amount))).ToArray();
            if (!shares.Any(s => s.Code != "OTHER")) continue;
            if (shares.Sum(s => s.Weight) != request.TotalAmount || shares.Any(s => s.Weight <= 0m))
                throw new DatedPaymentCalendarProjectionException();
            var original = Split(entry.Amount, shares.Select(s => s.Weight).ToArray());
            var kzt = entry.AmountKzt is { } amount ? Split(amount, shares.Select(s => s.Weight).ToArray()) : null;
            for (var i = 0; i < shares.Length; i++)
            {
                if (shares[i].Code == "OTHER" || original[i] == 0m && kzt?[i] == 0m) continue;
                buckets[shares[i].Code].Add(entry with { Amount = original[i], AmountKzt = kzt?[i] });
            }
        }
        var categories = Codes.Select(code => new GoodsCalendarCategory458(code, Build(full, buckets[code], token))).ToArray();
        var entries = categories.SelectMany(c => c.Calendar.Entries).OrderBy(e => e.BusinessDate)
            .ThenBy(e => e.CurrencyCode, StringComparer.Ordinal).ThenBy(e => e.ExpenseRequestId).ToArray();
        if (entries.Length > PaymentCalendarExportPolicy.MaximumRows) throw new DatedPaymentCalendarScopeTooLargeException();
        return new(Build(full, entries, token), categories);
    }

    public static GoodsCalendarDto458 ComposeFromOdds469(PaymentCalendarGridQuery query, TimeZoneInfo zone,
        IReadOnlyList<DatedPaymentCalendarRequestCapture> captures, IReadOnlyDictionary<Guid,string> articles,
        GoodsCalendarActuals469 actuals, CancellationToken token, DateOnly forecastDate)
    {
        // Validate the complete request ledger and retain only remaining plans.
        // Its payments are REPLACED by ODDS, never added to ODDS a second time.
        var legacy = Compose(query, zone, captures, articles, token, forecastDate);
        var requests = captures.ToDictionary(c => c.Request.Id, c => c.Request);
        var buckets = legacy.Categories.ToDictionary(c => c.Code,
            c => c.Calendar.Entries.Where(e => e.Kind == "PLAN").ToList(), StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if(actuals.Entries.Count>PaymentCalendarExportPolicy.MaximumRows)throw new DatedPaymentCalendarScopeTooLargeException();
        foreach(var row in actuals.Entries)
        {
            token.ThrowIfCancellationRequested();var e=row.Entry;
            if(!articles.TryGetValue(row.CashFlowItemId,out var code)||e.Source!="ODDS"
                ||e.SourceEntryId is not{Length:64} key||key.Any(c=>!Uri.IsHexDigit(c))||!keys.Add(key)
                ||e.BusinessDate<query.From||e.BusinessDate>query.Through||e.CurrencyCode!="KZT"
                ||e.AmountKzt!=e.Amount||e.Amount==0||e.Kind!=(e.Amount<0?"BANK_REVERSAL":"BANK_PAYMENT")
                ||e.CanOpen&&(e.ExpenseRequestId is not{} id||!requests.ContainsKey(id)))
                throw new DatedPaymentCalendarProjectionException();
            MoneyAmountPolicy.EnsureRepresentable(e.Amount,"ODDS goods movement");
            // All/paid include independent cash. Other workflow statuses have
            // meaning only for an explicitly linked, currently visible request.
            if(query.StatusCode is not(null or "PAID")
                &&(e.ExpenseRequestId is not{} requestId||!requests.TryGetValue(requestId,out var request)
                    ||request.Status.ToCode()!=query.StatusCode))continue;
            buckets[code].Add(e);
        }
        var categories=Codes.Select(code=>new GoodsCalendarCategory458(code,Build(legacy.Calendar,buckets[code],token))).ToArray();
        var entries=categories.SelectMany(c=>c.Calendar.Entries).OrderBy(e=>e.BusinessDate)
            .ThenBy(e=>e.SourceEntryId,StringComparer.Ordinal).ThenBy(e=>e.ExpenseRequestId).ToArray();
        return new(Build(legacy.Calendar,entries,token),categories)
        {ActualsSnapshotId=actuals.SnapshotId,ActualsCapturedAtUtc=actuals.CapturedAtUtc};
    }

    // Same ratio/tiyn/remainder convention as BudgetPaymentAttribution343.
    // This is report-only attribution, not evidence of a new bank movement.
    private static decimal[] Split(decimal signed, decimal[] weights)
    {
        var total = weights.Sum(); var absolute = Math.Abs(signed); var remaining = absolute;
        MoneyAmountPolicy.EnsureNonNegative(absolute, "Goods calendar amount");
        var result = new decimal[weights.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var part = i == result.Length - 1 ? remaining : Math.Min(remaining,
                decimal.Round(absolute * (weights[i] / total), 2, MidpointRounding.AwayFromZero));
            remaining -= part; result[i] = signed < 0m ? -part : part;
        }
        if (remaining != 0m || result.Sum() != signed) throw new DatedPaymentCalendarProjectionException();
        return result;
    }

    private static PaymentCalendarGridDto Build(PaymentCalendarGridDto full,
        IReadOnlyList<PaymentCalendarGridEntry> entries, CancellationToken token)
    {
        if (entries.Count > PaymentCalendarExportPolicy.MaximumRows) throw new DatedPaymentCalendarScopeTooLargeException();
        var byDay = entries.ToLookup(e => e.BusinessDate);
        PaymentCalendarGridAmounts Sum(IEnumerable<PaymentCalendarGridEntry> rows)
        {
            decimal plan = 0m, actual = 0m; var unknown = 0;
            foreach (var row in rows)
            {
                token.ThrowIfCancellationRequested();
                if (row.Kind == "PLAN") { if (row.AmountKzt is { } known) plan = checked(plan + known); else unknown++; }
                else actual = checked(actual + (row.AmountKzt ?? throw new DatedPaymentCalendarProjectionException()));
            }
            return new(plan, unknown, actual);
        }
        var days = full.Days.Select(d => new PaymentCalendarGridDay(d.Date, Sum(byDay[d.Date]), byDay[d.Date].Count())).ToArray();
        return new(full.From, full.Through, full.BusinessTimeZoneId, days, Sum(entries), entries);
    }
}

public sealed class GoodsCalendarService458(IDatedPaymentCalendarReader reader, IGoodsCalendarArticles458 articles, IGoodsCalendarActuals469 actuals,
    ICurrentActor actor, IBusinessTransactionRunner transactions, IDatedPaymentCalendarTimeZoneProvider timeZones, IClock clock)
{
    public async Task<GoodsCalendarDto458> GetAsync(PaymentCalendarGridQuery query, CancellationToken cancellationToken)
    {
        var expected = ActorIdentity.RequireBusinessDataAccess(actor);
        query.Validate(); cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await transactions.ExecuteAsync(async token =>
            {
                var current = ActorIdentity.RequireBusinessDataAccess(actor);
                if (!string.Equals(current, expected, StringComparison.Ordinal)) throw new ActorForbiddenException();
                var zone = timeZones.Resolve() ?? throw new PaymentCalendarUnavailableException();
                var forecast = PaymentCalendarForecast443.NextBusinessDay(clock.UtcNow, zone);
                var capture = await reader.ReadVisibleScopeAsync(current, actor.HasFullBusinessDataAccess,
                    new(query.From, query.Through, zone) { ForecastPlanDate = forecast }, token);
                var mapping = await articles.ReadAsync(token);
                var cash=await actuals.ReadAsync(query,mapping,capture,token);
                return GoodsCalendarComposer458.ComposeFromOdds469(query, zone, capture, mapping, cash, token, forecast);
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); return result;
        }
        catch (Exception error) when (!ProcessFatalExceptionClassifier.IsFatal(error)
            && error is not (OperationCanceledException or ActorNotAuthenticatedException or ActorForbiddenException
                or InvalidPaymentCalendarQueryException or DatedPaymentCalendarScopeTooLargeException or PaymentCalendarUnavailableException))
        { throw new PaymentCalendarUnavailableException(error); }
    }
}
