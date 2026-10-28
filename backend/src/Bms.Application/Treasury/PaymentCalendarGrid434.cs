using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Common;

namespace Bms.Application.Treasury;

public sealed record PaymentCalendarGridQuery(DateOnly From, DateOnly Through, string? StatusCode = null)
{
    public void Validate()
    {
        new DatedPaymentCalendarQuery(From, Through, 0, 1).Validate();
        if(StatusCode is not null&&!PaymentCalendarForecast443.IsAllowedCode(StatusCode))throw new InvalidPaymentCalendarQueryException();
        // A bounded presentation, not a new financial/business limit.
        if (Through.DayNumber - From.DayNumber > 365) throw new DatedPaymentCalendarScopeTooLargeException();
    }
}

public sealed record PaymentCalendarGridEntry(DateOnly BusinessDate, string Kind, Guid? ExpenseRequestId,
    string CurrencyCode, decimal Amount, decimal? AmountKzt, string? CounterpartyName, bool CanOpen)
{
    public string? Source { get; init; }
    public string? SourceEntryId { get; init; }
    public string? DocumentNumber { get; init; }
}

public sealed record PaymentCalendarGridAmounts(decimal KnownPlanAmountKzt, int UnknownPlanCount,
    decimal ActualAmountKzt)
{
    public decimal? PlanAmountKzt => UnknownPlanCount == 0 ? KnownPlanAmountKzt : null;
    public decimal? TotalAmountKzt => PlanAmountKzt is { } plan ? checked(plan + ActualAmountKzt) : null;
}

public sealed record PaymentCalendarGridDay(DateOnly Date, PaymentCalendarGridAmounts Amounts, int EntryCount);
public sealed record PaymentCalendarGridDto(DateOnly From, DateOnly Through, string BusinessTimeZoneId,
    IReadOnlyList<PaymentCalendarGridDay> Days, PaymentCalendarGridAmounts Totals,
    IReadOnlyList<PaymentCalendarGridEntry> Entries);

/// <summary>
/// The matrix is a cash forecast: actual bank cash at its source date plus the
/// remaining obligation at the plan date. Applied credit reduces the obligation
/// but is not a second bank payment. Currency differences never become new debt.
/// Shared dated metadata and the complete ledger projector validate all source
/// evidence before filtering. Only the forecast plan date differs from history.
/// </summary>
public static class PaymentCalendarGridComposer434
{
    public static PaymentCalendarGridDto Compose(PaymentCalendarGridQuery query, TimeZoneInfo zone,
        IReadOnlyList<DatedPaymentCalendarRequestCapture> captures, CancellationToken token, DateOnly? forecastDate = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        ArgumentNullException.ThrowIfNull(zone);ArgumentNullException.ThrowIfNull(captures);
        if(captures.Count>PaymentCalendarExportPolicy.MaximumRows)throw new DatedPaymentCalendarScopeTooLargeException();
        var period=new DatedPaymentCalendarPeriod(query.From,query.Through,zone){ForecastPlanDate=forecastDate};
        var ids=new HashSet<Guid>();var organizations=new Dictionary<Guid,(string? Code,string? Name)>();
        var currencies=new Dictionary<Guid,string>();var currencyIds=new Dictionary<string,Guid>(StringComparer.Ordinal);
        var entries = new List<PaymentCalendarGridEntry>();
        void Add(PaymentCalendarGridEntry entry){if(entries.Count>=PaymentCalendarExportPolicy.MaximumRows)throw new DatedPaymentCalendarScopeTooLargeException();entries.Add(entry);}
        foreach (var capture in captures)
        {
            token.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(capture);
            ArgumentNullException.ThrowIfNull(capture.Request);
            var request=capture.Request;
            if(!ids.Add(request.Id)||!(forecastDate.HasValue?PaymentCalendarForecast443.IsAllowed(request.Status)
                :PaymentCalendarStatusCodes.AllowedStatuses.Contains(request.Status)))throw new DatedPaymentCalendarProjectionException();
            DatedPaymentCalendarComposer.ValidateMetadata(capture,organizations,currencies,currencyIds);
            // Full ledger/credit validation is still performed before filtering.
            var projection=DatedPaymentCalendarProjector.Project(request,capture.CurrencyId,period,capture.Events,capture.Allocations,token);
            if(query.StatusCode is not null&&request.Status.ToCode()!=query.StatusCode)continue;
            foreach(var movement in projection.Movements)
            {
                token.ThrowIfCancellationRequested();
                if(movement.Kind==DatedPaymentCalendarMovementKind.AppliedAllocation)continue;
                Add(new(movement.BusinessDate,movement.Kind==DatedPaymentCalendarMovementKind.BankReversal
                    ?DatedPaymentCalendarRowKindCodes.BankReversal:DatedPaymentCalendarRowKindCodes.BankPayment,
                    request.Id,request.CurrencyCode,movement.SignedAmount,movement.SignedAmountKzt,capture.CounterpartyName,capture.CanOpen));
            }
            if (request.Status is ExpenseRequestStatus.Paid or ExpenseRequestStatus.Closed) continue;
            var planDate=PaymentCalendarForecast443.PlanDate(request.Status,request.PlannedPaymentDate,forecastDate);
            if(planDate is not{} displayDate||displayDate<query.From||displayDate>query.Through)continue;
            MoneyAmountPolicy.EnsureNonNegative(request.TotalAmount,"Forecast obligation");
            MoneyAmountPolicy.EnsureNonNegative(request.TotalAmountKzt,"Forecast KZT obligation");
            if(request.CurrencyCode=="KZT"&&request.TotalAmountKzt!=request.TotalAmount)throw new DatedPaymentCalendarProjectionException();
            // The reader captured the COMPLETE ledger, including dates outside
            // the selected period. Never subtract only the visible month's cash.
            decimal settled = 0m;
            foreach (var fact in capture.Events)
            {
                token.ThrowIfCancellationRequested();
                settled = checked(settled + (fact.IsReversal ? -fact.Amount : fact.Amount));
            }
            foreach (var credit in capture.Allocations)
            {
                token.ThrowIfCancellationRequested();
                if (credit.Status == "Applied") settled = checked(settled + credit.Amount);
            }
            var remaining = Math.Max(0m, checked(request.TotalAmount - settled));
            if (remaining == 0m) continue;
            decimal? kzt = request.CurrencyCode == "KZT" ? remaining
                : remaining == request.TotalAmount ? request.TotalAmountKzt
                : request.CurrencyRate is > 0m && request.CurrencyMultiplicity is > 0m
                    ? decimal.Round(checked(remaining * request.CurrencyRate.Value / request.CurrencyMultiplicity.Value),
                        2, MidpointRounding.ToEven) : null;
            Add(new(displayDate,DatedPaymentCalendarRowKindCodes.Plan,request.Id,request.CurrencyCode,
                remaining,kzt,capture.CounterpartyName,capture.CanOpen));
        }
        entries=entries.OrderBy(entry=>entry.BusinessDate).ThenBy(entry=>entry.CurrencyCode,StringComparer.Ordinal)
            .ThenBy(entry=>entry.ExpenseRequestId?.ToString("D"),StringComparer.Ordinal)
            .ThenBy(entry=>entry.Kind==DatedPaymentCalendarRowKindCodes.Plan?0:1).ToList();
        var dayEntries = entries.GroupBy(entry => entry.BusinessDate).ToDictionary(group => group.Key, group => group.ToArray());
        var days = new List<PaymentCalendarGridDay>();
        for (var date = query.From; ; date = date.AddDays(1))
        {
            token.ThrowIfCancellationRequested();
            var rows = dayEntries.GetValueOrDefault(date) ?? [];
            days.Add(new(date, Sum(rows, token), rows.Length));
            if (date == query.Through) break;
        }
        return new(query.From, query.Through, zone.Id, days.AsReadOnly(), Sum(entries, token), entries.AsReadOnly());
    }

    private static PaymentCalendarGridAmounts Sum(IEnumerable<PaymentCalendarGridEntry> entries, CancellationToken token)
    {
        decimal plan = 0m, actual = 0m;
        var unknown = 0;
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            if (entry.Kind == DatedPaymentCalendarRowKindCodes.Plan)
            {
                if (entry.AmountKzt is { } known) plan = checked(plan + known); else unknown = checked(unknown + 1);
            }
            else if (entry.AmountKzt is { } knownActual) actual = checked(actual + knownActual);
            else throw new DatedPaymentCalendarProjectionException();
        }
        return new(plan, unknown, actual);
    }
}

public sealed partial class DatedPaymentCalendarService
{
    public async Task<PaymentCalendarGridDto> GetGridAsync(PaymentCalendarGridQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var expectedActor = ActorIdentity.RequireBusinessDataAccess(actor);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await transactions.ExecuteAsync(async token =>
            {
                token.ThrowIfCancellationRequested();
                var current = ActorIdentity.RequireBusinessDataAccess(actor);
                if (!string.Equals(current, expectedActor, StringComparison.Ordinal)) throw new ActorForbiddenException();
                var zone = timeZones.Resolve() ?? throw new PaymentCalendarUnavailableException();
                var forecastDate=PaymentCalendarForecast443.NextBusinessDay(clock?.UtcNow??DateTimeOffset.UtcNow,zone);
                var capture = await reader.ReadVisibleScopeAsync(current, actor.HasFullBusinessDataAccess,
                    new(query.From, query.Through, zone){ForecastPlanDate=forecastDate}, token);
                return PaymentCalendarGridComposer434.Compose(query, zone, capture, token,forecastDate);
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception exception) when (!ProcessFatalExceptionClassifier.IsFatal(exception)
            && exception is not (OperationCanceledException or ActorNotAuthenticatedException or ActorForbiddenException
                or InvalidPaymentCalendarQueryException or DatedPaymentCalendarScopeTooLargeException
                or PaymentCalendarUnavailableException))
        { throw new PaymentCalendarUnavailableException(exception); }
    }
}
