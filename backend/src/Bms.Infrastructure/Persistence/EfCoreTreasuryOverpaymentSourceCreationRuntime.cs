using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

internal sealed class EfCoreTreasuryOverpaymentSourceCreationRuntime :
    ITreasuryOverpaymentSourceCreationRuntime
{
    private readonly BmsDbContext dbContext;

    internal EfCoreTreasuryOverpaymentSourceCreationRuntime(BmsDbContext dbContext)
    {
        this.dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public bool HasSerializableTransaction
    {
        get
        {
            var transaction = dbContext.Database.CurrentTransaction;
            return transaction is not null
                && transaction.GetDbTransaction().IsolationLevel
                    == IsolationLevel.Serializable;
        }
    }

    public bool HasPendingChanges => dbContext.ChangeTracker.HasChanges();

    public async Task AcquireExclusiveTransactionLockAsync(
        string resource,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @BmsOverpaymentSourceCreationLockResult int;
            EXEC @BmsOverpaymentSourceCreationLockResult = sys.sp_getapplock
                @Resource = {resource},
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 15000,
                @DbPrincipal = N'public';
            IF @BmsOverpaymentSourceCreationLockResult < 0
                THROW 60143, N'BMS overpayment source creation lock was not acquired.', 1;
            """, cancellationToken);
    }

    public async Task<StoredTreasuryPaymentFact?> FindPaymentFactAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var fact = await dbContext.ExpenseRequestPaymentFacts
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.EventId == eventId, cancellationToken);
        return fact is null ? null : ToStored(fact);
    }

    public Task<OverpaymentSource?> FindSourceByEventAsync(
        Guid sourceEventId,
        CancellationToken cancellationToken) =>
        dbContext.OverpaymentSources
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.SourceEventId == sourceEventId,
                cancellationToken);

    public Task<OverpaymentSource?> FindSourceByIdAsync(
        Guid sourceId,
        CancellationToken cancellationToken) =>
        dbContext.OverpaymentSources
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.SourceId == sourceId, cancellationToken);

    public Task<bool> IsExactKztScopeAsync(
        Guid sourceExpenseRequestId,
        Guid organizationId,
        Guid counterpartyId,
        Guid currencyId,
        CancellationToken cancellationToken) =>
        dbContext.ExpenseRequests
            .AsNoTracking()
            .AnyAsync(
                request => request.Id == sourceExpenseRequestId
                    && request.OrganizationId == organizationId
                    && request.CounterpartyId == counterpartyId
                    && EF.Functions.Collate(
                        request.CurrencyCode,
                        "Latin1_General_100_BIN2") == "KZT"
                    && dbContext.Currencies.Any(currency =>
                        currency.Id == currencyId
                        && EF.Functions.Collate(
                            currency.Code,
                            "Latin1_General_100_BIN2") == "KZT"),
                cancellationToken);

    public async Task<int> InsertSourceAsync(
        OverpaymentSource source,
        CancellationToken cancellationToken)
    {
        dbContext.OverpaymentSources.Add(source);
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    private static StoredTreasuryPaymentFact ToStored(ExpenseRequestPaymentFact fact) => new(
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
        fact.ReversedEventId,
        fact.ProcessedAt);
}
