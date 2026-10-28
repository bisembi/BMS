using Bms.Application.Abstractions;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Read-only access to the confirmed payment ledger of one expense request.
/// The payment summary uses this reader; it exposes no write operation.
/// </summary>
public sealed class ExpenseRequestPaymentFactReader(BmsDbContext dbContext)
    : IExpenseRequestPaymentFactReader
{
    public Task<IReadOnlyList<ApprovedPaymentRounding400>> ReadApprovedRoundingAsync(
        Guid expenseRequestId, CancellationToken cancellationToken) =>
        ApprovedAccountingEvidence400.ReadRoundingAsync(dbContext, expenseRequestId, cancellationToken);

    public async Task<IReadOnlyList<ConfirmedPaymentEvent>> ReadForRequestAsync(
        Guid expenseRequestId,
        Guid currencyId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.ExpenseRequestPaymentFacts
            .AsNoTracking()
            .Where(fact => fact.ExpenseRequestId == expenseRequestId
                && fact.CurrencyId == currencyId)
            .OrderBy(fact => fact.PaidAt)
            .ThenBy(fact => fact.EventId)
            .ToArrayAsync(cancellationToken);
        return rows
            .Select(row => new ConfirmedPaymentEvent(
                row.SourceSystem,
                row.ExternalId,
                row.EventId,
                row.ExpenseRequestId,
                row.CurrencyId,
                row.Amount,
                row.AmountKzt,
                row.PaidAt,
                row.PaymentNumber,
                row.IsReversal,
                row.ReversedEventId))
            .ToArray();
    }

    public async Task<Guid?> FindCurrencyIdAsync(
        string currencyCode,
        CancellationToken cancellationToken)
    {
        var id = await dbContext.Currencies
            .AsNoTracking()
            .Where(currency => currency.Code == currencyCode)
            .Select(currency => (Guid?)currency.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return id;
    }

    public async Task<IReadOnlyList<ExpenseRequestAppliedOverpaymentSnapshot>>
        ReadOverpaymentAllocationsForRequestAsync(
            Guid expenseRequestId,
            CancellationToken cancellationToken)
    {
        var rows = await (
            from allocation in dbContext.OverpaymentAllocations.AsNoTracking()
            where allocation.TargetExpenseRequestId == expenseRequestId
            join source in dbContext.OverpaymentSources.AsNoTracking()
                on allocation.SourceId equals source.SourceId into sources
            from source in sources.DefaultIfEmpty()
            select new
            {
                allocation.AllocationId,
                allocation.TargetExpenseRequestId,
                OrganizationId = (Guid?)source.OrganizationId,
                CounterpartyId = (Guid?)source.CounterpartyId,
                CurrencyId = (Guid?)source.CurrencyId,
                allocation.Amount,
                allocation.AmountKzt,
                allocation.Status,
                allocation.ReservedAt,
                allocation.AppliedOrReleasedAt
            }).ToArrayAsync(cancellationToken);

        return rows.Select(row => new ExpenseRequestAppliedOverpaymentSnapshot(
                row.AllocationId,
                row.TargetExpenseRequestId,
                row.OrganizationId.GetValueOrDefault(),
                row.CounterpartyId.GetValueOrDefault(),
                row.CurrencyId.GetValueOrDefault(),
                row.Amount,
                row.AmountKzt,
                row.Status,
                row.ReservedAt,
                row.AppliedOrReleasedAt))
            .ToArray();
    }
}
