using System.Data;
using Bms.Application.Abstractions;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class ExpenseRequestPaymentDetailsReader(
    BmsDbContext dbContext, IExpenseRequestRepository requests) : IExpenseRequestPaymentDetailsReader
{
    public async Task<ExpenseRequestPaymentDetailsSnapshot?> ReadVisibleAsync(
        Guid requestId, string actorExternalId, bool includeAll, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new ExpenseRequestPaymentDetailsUnavailableException();
        var request = await requests.FindVisibleAsync(requestId, actorExternalId, includeAll, cancellationToken);
        if (request is null) return null;
        var currencyId = await dbContext.Currencies.AsNoTracking()
            .Where(currency => currency.Code == request.CurrencyCode).Select(currency => (Guid?)currency.Id)
            .SingleOrDefaultAsync(cancellationToken) ?? throw new ExpenseRequestPaymentDetailsUnavailableException();
        // Do not restrict by currency: the service must detect a contradictory
        // currency anywhere in this request, including outside the requested page.
        var rows = await dbContext.ExpenseRequestPaymentFacts.AsNoTracking()
            .Where(fact => fact.ExpenseRequestId == requestId).ToArrayAsync(cancellationToken);
        var events = rows.Select(row => new ConfirmedPaymentEvent(row.SourceSystem, row.ExternalId,
            row.EventId, row.ExpenseRequestId, row.CurrencyId, row.Amount, row.AmountKzt,
            row.PaidAt, row.PaymentNumber, row.IsReversal, row.ReversedEventId)).ToArray();
        return new(request.Id, currencyId, request.CurrencyCode, Array.AsReadOnly(events));
    }
}
