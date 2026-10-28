using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Application.Treasury;

/// <summary>
/// The dated read path shares the transaction runner, not the capped settlement
/// calculation. Every retry reauthorizes and captures all rows, labels, facts
/// and card visibility afresh; a failed attempt cannot contribute partial totals.
/// </summary>
public sealed partial class DatedPaymentCalendarService(
    IDatedPaymentCalendarReader reader,
    ICurrentActor actor,
    IBusinessTransactionRunner transactions,
    IDatedPaymentCalendarTimeZoneProvider timeZones,
    IClock? clock = null)
{
    public async Task<DatedPaymentCalendarPageDto> GetAsync(
        DatedPaymentCalendarQuery query,
        CancellationToken cancellationToken)
    {
        var report = await ReadReportAsync(query, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return report.ToPage();
    }

    // Export orchestration in this assembly may consume the complete report.
    // HTTP handlers must return a page or a workbook, never serialize captures
    // or this unpaged report (which contains internal query/scope information).
    internal async Task<DatedPaymentCalendarReport> ReadReportAsync(
        DatedPaymentCalendarQuery query,
        CancellationToken cancellationToken)
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
                if (!string.Equals(current, expectedActor, StringComparison.Ordinal))
                {
                    throw new ActorForbiddenException();
                }
                var includeAll = actor.HasFullBusinessDataAccess;
                var zone = timeZones.Resolve() ?? throw new PaymentCalendarUnavailableException();
                var period = new DatedPaymentCalendarPeriod(query.From, query.Through, zone);
                // Reader sees the entire visible business-date scope. Passing
                // status/currency/page here could conceal inconsistent evidence.
                var capture = await reader.ReadVisibleScopeAsync(current, includeAll, period, token);
                token.ThrowIfCancellationRequested();
                return DatedPaymentCalendarComposer.Compose(query, zone, capture, token);
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception exception) when (!ProcessFatalExceptionClassifier.IsFatal(exception)
            && exception is not (OperationCanceledException or ActorNotAuthenticatedException
                or ActorForbiddenException or InvalidPaymentCalendarQueryException
                or DatedPaymentCalendarScopeTooLargeException or PaymentCalendarUnavailableException))
        {
            // Provider/reconciliation details are retained as inner evidence,
            // not exposed through the public fixed unavailability contract.
            throw new PaymentCalendarUnavailableException(exception);
        }
    }
}
