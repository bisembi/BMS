using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

internal sealed class EfCoreTreasuryOverpaymentReservationRuntime :
    ITreasuryOverpaymentReservationRuntime
{
    private const int ManagedLockTimeoutMilliseconds = 10_000;
    private readonly BmsDbContext dbContext;

    internal EfCoreTreasuryOverpaymentReservationRuntime(BmsDbContext dbContext)
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

    public Guid CreateAllocationId() => Guid.NewGuid();

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
            DECLARE @BmsOverpaymentReservationLockResult int;
            EXEC @BmsOverpaymentReservationLockResult = sys.sp_getapplock
                @Resource = {{resource}},
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = {{ManagedLockTimeoutMilliseconds}},
                @DbPrincipal = N'public';
            IF @BmsOverpaymentReservationLockResult < 0
                THROW 60131, N'BMS overpayment reservation lock was not acquired.', 1;
            """, cancellationToken);
    }

    public async Task<StoredOverpaymentScope?> FindTargetScopeAsync(
        Guid targetExpenseRequestId,
        CancellationToken cancellationToken) =>
        await (
            from request in dbContext.ExpenseRequests.AsNoTracking()
            join currency in dbContext.Currencies.AsNoTracking()
                on EF.Functions.Collate(
                    request.CurrencyCode,
                    "Latin1_General_100_BIN2") equals currency.Code
            where request.Id == targetExpenseRequestId
                && request.OrganizationId != null
                && request.CounterpartyId != null
            select new StoredOverpaymentScope(
                request.OrganizationId!.Value,
                request.CounterpartyId!.Value,
                currency.Id,
                currency.Code))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> UserExistsAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        dbContext.Employees
            .AsNoTracking()
            .AnyAsync(employee => employee.Id == userId, cancellationToken);

    public async Task<IReadOnlyList<StoredOverpaymentAllocation>>
        FindAllocationsByCommandAsync(
            Guid commandId,
            CancellationToken cancellationToken)
    {
        var allocations = await dbContext.OverpaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.CommandId == commandId)
            .OrderBy(allocation => allocation.AllocationId)
            .Select(allocation => new StoredOverpaymentAllocation(
                allocation.TargetExpenseRequestId,
                allocation.SourceId,
                allocation.AllocationId,
                allocation.Amount,
                allocation.AmountKzt,
                allocation.Status,
                allocation.CommandId,
                allocation.ReservedAt,
                allocation.AppliedOrReleasedAt,
                allocation.UserId))
            .ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(allocations);
    }

    public Task<bool> HasActiveDeficitAsync(
        StoredOverpaymentScope scope,
        CancellationToken cancellationToken) =>
        dbContext.OverpaymentDeficits
            .AsNoTracking()
            .AnyAsync(
                deficit => deficit.OrganizationId == scope.OrganizationId
                    && deficit.CounterpartyId == scope.CounterpartyId
                    && deficit.CurrencyId == scope.CurrencyId
                    && deficit.SettledAt == null
                    && deficit.CurrentAmount > 0m
                    && deficit.CurrentAmountKzt > 0m,
                cancellationToken);

    public async Task<IReadOnlyList<StoredOverpaymentSource>> FindSourcesAsync(
        StoredOverpaymentScope scope,
        CancellationToken cancellationToken)
    {
        var sources = await BuildSourceQuery(
                dbContext.OverpaymentSources
                    .AsNoTracking()
                    .Where(source => source.OrganizationId == scope.OrganizationId
                        && source.CounterpartyId == scope.CounterpartyId
                        && source.CurrencyId == scope.CurrencyId)
                    .OrderBy(source => source.OccurredAt)
                    .ThenBy(source => source.SourceId))
            .ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(sources);
    }

    public async Task<int> ReserveSourceAsync(
        StoredOverpaymentSource expectedSource,
        decimal amount,
        decimal amountKzt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedSource);
        EnsureCleanSerializableAttempt();

        return await dbContext.OverpaymentSources
            .Where(source => source.OrganizationId == expectedSource.OrganizationId
                && source.CounterpartyId == expectedSource.CounterpartyId
                && source.CurrencyId == expectedSource.CurrencyId
                && source.SourceId == expectedSource.SourceId
                && source.CurrentRemainingAmount == expectedSource.CurrentRemainingAmount
                && source.CurrentRemainingAmountKzt == expectedSource.CurrentRemainingAmountKzt
                && source.ReservedAmount == expectedSource.ReservedAmount
                && source.ReservedAmountKzt == expectedSource.ReservedAmountKzt)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        source => source.ReservedAmount,
                        expectedSource.ReservedAmount + amount)
                    .SetProperty(
                        source => source.ReservedAmountKzt,
                        expectedSource.ReservedAmountKzt + amountKzt),
                cancellationToken);
    }

    public async Task<int> InsertAllocationAsync(
        StoredOverpaymentAllocation allocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        EnsureCleanSerializableAttempt();

        var entity = new OverpaymentAllocation
        {
            TargetExpenseRequestId = allocation.TargetExpenseRequestId,
            SourceId = allocation.SourceId,
            AllocationId = allocation.AllocationId,
            Amount = allocation.Amount,
            AmountKzt = allocation.AmountKzt,
            Status = allocation.Status,
            CommandId = allocation.CommandId,
            ReservedAt = allocation.ReservedAtUtc,
            AppliedOrReleasedAt = allocation.AppliedOrReleasedAtUtc,
            UserId = allocation.UserId
        };
        dbContext.OverpaymentAllocations.Add(entity);
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.Entry(entity).State = EntityState.Detached;
        }
    }

    public async Task<StoredOverpaymentAllocation?> FindAllocationByIdAsync(
        Guid allocationId,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.OverpaymentAllocations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                allocation => allocation.AllocationId == allocationId,
                cancellationToken);
        return entity is null ? null : ToStored(entity);
    }

    public Task<StoredOverpaymentSource?> FindSourceByIdAsync(
        Guid sourceId,
        CancellationToken cancellationToken) =>
        BuildSourceQuery(
                dbContext.OverpaymentSources
                    .AsNoTracking()
                    .Where(source => source.SourceId == sourceId))
            .SingleOrDefaultAsync(cancellationToken);

    // EF Core cannot translate member access over a custom record-constructor
    // projection, so every filter and ordering runs on the entity before the
    // record is projected (found by W443 on a real SQL Server; the pattern
    // mirrors the already-proven W150 runtime).
    private IQueryable<StoredOverpaymentSource> BuildSourceQuery(
        IQueryable<OverpaymentSource> filtered) =>
        from source in filtered
        join currency in dbContext.Currencies.AsNoTracking()
            on source.CurrencyId equals currency.Id
        select new StoredOverpaymentSource(
            source.SourceId,
            source.OrganizationId,
            source.CounterpartyId,
            source.CurrencyId,
            currency.Code,
            source.OccurredAt,
            source.CurrentRemainingAmount,
            source.CurrentRemainingAmountKzt,
            source.ReservedAmount,
            source.ReservedAmountKzt);

    private static StoredOverpaymentAllocation ToStored(OverpaymentAllocation allocation) =>
        new(
            allocation.TargetExpenseRequestId,
            allocation.SourceId,
            allocation.AllocationId,
            allocation.Amount,
            allocation.AmountKzt,
            allocation.Status,
            allocation.CommandId,
            allocation.ReservedAt,
            allocation.AppliedOrReleasedAt,
            allocation.UserId);

    private void EnsureCleanSerializableAttempt()
    {
        if (!HasSerializableTransaction || HasPendingChanges)
        {
            throw new InvalidOperationException(
                "A clean Serializable transaction is required for reservation writes.");
        }
    }
}
