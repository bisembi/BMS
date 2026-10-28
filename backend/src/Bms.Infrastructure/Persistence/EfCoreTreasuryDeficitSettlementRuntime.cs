using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

internal sealed class EfCoreTreasuryDeficitSettlementRuntime :
    ITreasuryDeficitSettlementRuntime
{
    private const int ManagedLockTimeoutMilliseconds = 10_000;
    private readonly BmsDbContext dbContext;

    internal EfCoreTreasuryDeficitSettlementRuntime(BmsDbContext dbContext)
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
        if (!HasSerializableTransaction
            || string.IsNullOrWhiteSpace(resource)
            || resource.Length > BusinessManagedLockResources.SqlServerMaximumResourceLength)
        {
            throw new InvalidOperationException(
                "A bounded lock resource and Serializable transaction are required.");
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
            DECLARE @BmsDeficitSettlementLockResult int;
            EXEC @BmsDeficitSettlementLockResult = sys.sp_getapplock
                @Resource = {{resource}},
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = {{ManagedLockTimeoutMilliseconds}},
                @DbPrincipal = N'public';
            IF @BmsDeficitSettlementLockResult < 0
                THROW 60133, N'BMS deficit settlement lock was not acquired.', 1;
            """, cancellationToken);
    }

    public Task<bool> UserExistsAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        dbContext.Employees
            .AsNoTracking()
            .AnyAsync(employee => employee.Id == userId, cancellationToken);

    public Task<bool> ExpenseRequestMatchesScopeAsync(
        Guid expenseRequestId,
        Guid organizationId,
        Guid counterpartyId,
        Guid currencyId,
        CancellationToken cancellationToken) =>
        dbContext.ExpenseRequests
            .AsNoTracking()
            .AnyAsync(request => request.Id == expenseRequestId
                && request.OrganizationId == organizationId
                && request.CounterpartyId == counterpartyId
                && dbContext.Currencies.Any(currency =>
                    currency.Id == currencyId
                    && currency.Code == EF.Functions.Collate(
                        request.CurrencyCode,
                        "Latin1_General_100_BIN2")),
                cancellationToken);

    public Task<bool> SettlementCommandExistsAsync(
        Guid commandId,
        CancellationToken cancellationToken) =>
        dbContext.OverpaymentDeficitSettlements
            .AsNoTracking()
            .AnyAsync(settlement => settlement.CommandId == commandId, cancellationToken);

    public async Task<IReadOnlyList<StoredOverpaymentDeficitSettlement>>
        FindByCommandAsync(Guid commandId, CancellationToken cancellationToken)
    {
        var rows = await BuildSettlementQuery(
                dbContext.OverpaymentDeficitSettlements
                    .AsNoTracking()
                    .Where(settlement => settlement.CommandId == commandId)
                    .OrderBy(settlement => settlement.DeficitId))
            .ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(rows);
    }

    public async Task<IReadOnlyList<StoredOverpaymentDeficitSettlement>> FindBySourceAsync(
        string kind,
        string sourceReference,
        CancellationToken cancellationToken)
    {
        var rows = await BuildSettlementQuery(
                dbContext.OverpaymentDeficitSettlements
                    .AsNoTracking()
                    .Where(settlement => settlement.Kind == kind
                        && settlement.SourceReference == sourceReference)
                    .OrderBy(settlement => settlement.DeficitId))
            .ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(rows);
    }

    public async Task<IReadOnlyList<StoredOverpaymentDeficit>> FindActiveDeficitsAsync(
        Guid organizationId,
        Guid counterpartyId,
        Guid currencyId,
        CancellationToken cancellationToken)
    {
        var rows = await BuildDeficitQuery(
                dbContext.OverpaymentDeficits
                    .AsNoTracking()
                    .Where(deficit => deficit.OrganizationId == organizationId
                        && deficit.CounterpartyId == counterpartyId
                        && deficit.CurrencyId == currencyId
                        && deficit.CurrentAmount > 0m
                        && deficit.CurrentAmountKzt > 0m
                        && deficit.SettledAt == null)
                    .OrderBy(deficit => deficit.OccurredAt)
                    .ThenBy(deficit => deficit.DeficitId))
            .ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(rows);
    }

    public async Task<IReadOnlyList<Guid>> FindMatchingOverpaymentSourceIdsAsync(
        IReadOnlyCollection<Guid> sourceIds,
        Guid organizationId,
        Guid counterpartyId,
        Guid currencyId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        if (sourceIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var rows = await dbContext.OverpaymentSources
            .AsNoTracking()
            .Where(source => sourceIds.Contains(source.SourceId)
                && source.OrganizationId == organizationId
                && source.CounterpartyId == counterpartyId
                && source.CurrencyId == currencyId)
            .Select(source => source.SourceId)
            .OrderBy(sourceId => sourceId)
            .ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(rows);
    }

    public async Task<int> UpdateDeficitAsync(
        StoredOverpaymentDeficit expectedDeficit,
        decimal amountAfter,
        decimal amountKztAfter,
        DateTimeOffset? settledAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedDeficit);
        EnsureCleanSerializableAttempt();

        return await dbContext.OverpaymentDeficits
            .Where(deficit => deficit.OrganizationId == expectedDeficit.OrganizationId
                && deficit.CounterpartyId == expectedDeficit.CounterpartyId
                && deficit.CurrencyId == expectedDeficit.CurrencyId
                && deficit.DeficitId == expectedDeficit.DeficitId
                && deficit.OriginalAmount == expectedDeficit.OriginalAmount
                && deficit.OriginalAmountKzt == expectedDeficit.OriginalAmountKzt
                && deficit.CurrentAmount == expectedDeficit.CurrentAmount
                && deficit.CurrentAmountKzt == expectedDeficit.CurrentAmountKzt
                && deficit.SourceOverpaymentId == expectedDeficit.SourceOverpaymentId
                && deficit.ExternalReversalId == expectedDeficit.ExternalReversalId
                && deficit.OccurredAt == expectedDeficit.OccurredAtUtc
                && deficit.SourceExpenseRequestId == expectedDeficit.SourceExpenseRequestId
                && deficit.CreationCommandId == expectedDeficit.CreationCommandId
                && deficit.ActualCorrectionExecutorId ==
                    expectedDeficit.ActualCorrectionExecutorId
                && deficit.SettledAt == expectedDeficit.SettledAtUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(deficit => deficit.CurrentAmount, amountAfter)
                    .SetProperty(deficit => deficit.CurrentAmountKzt, amountKztAfter)
                    .SetProperty(deficit => deficit.SettledAt, settledAtUtc),
                cancellationToken);
    }

    public async Task<int> InsertSettlementAsync(
        StoredOverpaymentDeficitSettlement settlement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        EnsureCleanSerializableAttempt();
        var entity = new OverpaymentDeficitSettlement
        {
            DeficitId = settlement.DeficitId,
            CommandId = settlement.CommandId,
            Kind = settlement.Kind,
            SourceReference = settlement.SourceReference,
            SourceExpenseRequestId = settlement.SourceExpenseRequestId,
            CorrectionId = null,
            Amount = settlement.Amount,
            AmountKzt = settlement.AmountKzt,
            AppliedAt = settlement.AppliedAtUtc,
            UserId = settlement.UserId
        };
        dbContext.OverpaymentDeficitSettlements.Add(entity);
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.Entry(entity).State = EntityState.Detached;
        }
    }

    public Task<StoredOverpaymentDeficit?> FindDeficitByIdAsync(
        Guid deficitId,
        CancellationToken cancellationToken) =>
        BuildDeficitQuery(
                dbContext.OverpaymentDeficits
                    .AsNoTracking()
                    .Where(deficit => deficit.DeficitId == deficitId))
            .SingleOrDefaultAsync(cancellationToken);

    // EF Core cannot translate member access over a custom record-constructor
    // projection, so every filter and ordering runs on the entity before the
    // record is projected (found by W443 on a real SQL Server; the pattern
    // mirrors the already-proven W150 runtime).
    private IQueryable<StoredOverpaymentDeficit> BuildDeficitQuery(
        IQueryable<OverpaymentDeficit> filtered) =>
        from deficit in filtered
        join currency in dbContext.Currencies.AsNoTracking()
            on deficit.CurrencyId equals currency.Id
        select new StoredOverpaymentDeficit(
            deficit.OrganizationId,
            deficit.CounterpartyId,
            deficit.CurrencyId,
            currency.Code,
            deficit.DeficitId,
            deficit.OriginalAmount,
            deficit.OriginalAmountKzt,
            deficit.CurrentAmount,
            deficit.CurrentAmountKzt,
            deficit.SourceOverpaymentId,
            deficit.ExternalReversalId,
            deficit.OccurredAt,
            deficit.SourceExpenseRequestId,
            deficit.CreationCommandId,
            deficit.ActualCorrectionExecutorId,
            deficit.SettledAt);

    // Filters and ordering must run on the entity: EF Core cannot translate
    // member access over a custom record-constructor projection (W443).
    private static IQueryable<StoredOverpaymentDeficitSettlement> BuildSettlementQuery(
        IQueryable<OverpaymentDeficitSettlement> filtered) =>
        filtered
            .Where(settlement => settlement.SourceReference != null)
            .Select(settlement => new StoredOverpaymentDeficitSettlement(
                settlement.DeficitId,
                settlement.CommandId,
                settlement.Kind,
                settlement.SourceReference!,
                settlement.SourceExpenseRequestId,
                settlement.Amount,
                settlement.AmountKzt,
                settlement.AppliedAt,
                settlement.UserId));

    private void EnsureCleanSerializableAttempt()
    {
        if (!HasSerializableTransaction || HasPendingChanges)
        {
            throw new InvalidOperationException(
                "A clean Serializable transaction is required for deficit settlement writes.");
        }
    }
}
