using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

internal sealed class EfCoreTreasuryPaymentFactWriteRuntime :
    ITreasuryPaymentFactWriteRuntime
{
    private const int ManagedLockTimeoutMilliseconds = 10_000;
    private readonly BmsDbContext dbContext;

    internal EfCoreTreasuryPaymentFactWriteRuntime(BmsDbContext dbContext)
    {
        this.dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public bool HasSerializableTransaction
    {
        get
        {
            var transaction = dbContext.Database.CurrentTransaction;
            return transaction is not null
                && transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable;
        }
    }

    public bool HasPendingChanges => dbContext.ChangeTracker.HasChanges();

    public async Task AcquireExclusiveTransactionLockAsync(
        string resource,
        CancellationToken cancellationToken)
    {
        if (!HasSerializableTransaction || string.IsNullOrWhiteSpace(resource))
        {
            throw new InvalidOperationException(
                "A valid lock resource and Serializable transaction are required.");
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
            DECLARE @BmsPaymentFactLockResult int;
            EXEC @BmsPaymentFactLockResult = sys.sp_getapplock
                @Resource = {{resource}},
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = {{ManagedLockTimeoutMilliseconds}},
                @DbPrincipal = N'public';
            IF @BmsPaymentFactLockResult < 0
                THROW 60130, N'BMS payment-fact managed lock was not acquired.', 1;
            """, cancellationToken);
    }

    public Task<bool> ExpenseRequestUsesCurrencyAsync(
        Guid expenseRequestId,
        Guid currencyId,
        CancellationToken cancellationToken) =>
        dbContext.ExpenseRequests
            .AsNoTracking()
            .AnyAsync(
                request => request.Id == expenseRequestId
                    && dbContext.Currencies.Any(currency =>
                        currency.Id == currencyId
                        && currency.Code == EF.Functions.Collate(
                            request.CurrencyCode,
                            "Latin1_General_100_BIN2")),
                cancellationToken);

    public async Task<StoredTreasuryPaymentFact?> FindByStructuralIdentityAsync(
        string sourceSystem,
        string externalId,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.ExpenseRequestPaymentFacts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                fact => fact.SourceSystem == sourceSystem
                    && fact.ExternalId == externalId,
                cancellationToken);
        return entity is null ? null : FromEntity(entity);
    }

    public async Task<StoredTreasuryPaymentFact?> FindByEventIdAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.ExpenseRequestPaymentFacts
            .AsNoTracking()
            .SingleOrDefaultAsync(fact => fact.EventId == eventId, cancellationToken);
        return entity is null ? null : FromEntity(entity);
    }

    public async Task<IReadOnlyList<StoredTreasuryPaymentFact>> FindReversalsAsync(
        Guid reversedEventId,
        CancellationToken cancellationToken)
    {
        var entities = await dbContext.ExpenseRequestPaymentFacts
            .AsNoTracking()
            .Where(fact => fact.IsReversal && fact.ReversedEventId == reversedEventId)
            .OrderBy(fact => fact.EventId)
            .ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(entities.Select(FromEntity).ToArray());
    }

    public async Task<int> InsertAsync(
        StoredTreasuryPaymentFact paymentFact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paymentFact);
        if (!HasSerializableTransaction || HasPendingChanges)
        {
            throw new InvalidOperationException(
                "A clean Serializable transaction is required for payment-fact insertion.");
        }

        var entity = ToEntity(paymentFact);
        dbContext.ExpenseRequestPaymentFacts.Add(entity);
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.Entry(entity).State = EntityState.Detached;
        }
    }

    private static StoredTreasuryPaymentFact FromEntity(ExpenseRequestPaymentFact entity) =>
        new(
            entity.SourceSystem,
            entity.ExternalId,
            entity.EventId,
            entity.ExpenseRequestId,
            entity.CurrencyId,
            entity.Amount,
            entity.AmountKzt,
            entity.PaidAt,
            entity.PaymentNumber,
            entity.IsReversal,
            entity.ReversedEventId,
            entity.ProcessedAt);

    private static ExpenseRequestPaymentFact ToEntity(StoredTreasuryPaymentFact paymentFact) =>
        new()
        {
            SourceSystem = paymentFact.SourceSystem,
            ExternalId = paymentFact.ExternalId,
            EventId = paymentFact.EventId,
            ExpenseRequestId = paymentFact.ExpenseRequestId,
            CurrencyId = paymentFact.CurrencyId,
            Amount = paymentFact.Amount,
            AmountKzt = paymentFact.AmountKzt,
            PaidAt = paymentFact.PaidAt,
            PaymentNumber = paymentFact.PaymentNumber,
            IsReversal = paymentFact.IsReversal,
            ReversedEventId = paymentFact.ReversedEventId,
            ProcessedAt = paymentFact.ProcessedAtUtc
        };
}
