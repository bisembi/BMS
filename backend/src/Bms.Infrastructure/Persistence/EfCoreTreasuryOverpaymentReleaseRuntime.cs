using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

internal sealed class EfCoreTreasuryOverpaymentReleaseRuntime :
    ITreasuryOverpaymentReleaseRuntime
{
    private const int ManagedLockTimeoutMilliseconds = 10_000;
    private readonly BmsDbContext dbContext;

    internal EfCoreTreasuryOverpaymentReleaseRuntime(BmsDbContext dbContext)
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
            DECLARE @BmsOverpaymentReleaseLockResult int;
            EXEC @BmsOverpaymentReleaseLockResult = sys.sp_getapplock
                @Resource = {{resource}},
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = {{ManagedLockTimeoutMilliseconds}},
                @DbPrincipal = N'public';
            IF @BmsOverpaymentReleaseLockResult < 0
                THROW 60132, N'BMS overpayment release lock was not acquired.', 1;
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
        var rows = await BuildAllocationQuery(
                dbContext.OverpaymentAllocations
                    .AsNoTracking()
                    .Where(allocation => allocation.CommandId == commandId)
                    .OrderBy(allocation => allocation.AllocationId))
            .ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(rows);
    }

    public async Task<IReadOnlyList<StoredOverpaymentAllocation>>
        FindReservedAllocationsAsync(
            Guid targetExpenseRequestId,
            CancellationToken cancellationToken)
    {
        var rows = await BuildAllocationQuery(
                dbContext.OverpaymentAllocations
                    .AsNoTracking()
                    .Where(allocation =>
                        allocation.TargetExpenseRequestId == targetExpenseRequestId
                        && allocation.Status == "Reserved")
                    .OrderBy(allocation => allocation.AllocationId))
            .ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(rows);
    }

    public async Task<IReadOnlyList<StoredOverpaymentSource>> FindSourcesAsync(
        IReadOnlyCollection<Guid> sourceIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        if (sourceIds.Count == 0)
        {
            return Array.Empty<StoredOverpaymentSource>();
        }

        var rows = await BuildSourceQuery(
                dbContext.OverpaymentSources
                    .AsNoTracking()
                    .Where(source => sourceIds.Contains(source.SourceId))
                    .OrderBy(source => source.SourceId))
            .ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(rows);
    }

    public async Task<int> ReleaseSourceAsync(
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
                        expectedSource.ReservedAmount - amount)
                    .SetProperty(
                        source => source.ReservedAmountKzt,
                        expectedSource.ReservedAmountKzt - amountKzt),
                cancellationToken);
    }

    public async Task<int> MarkAllocationReleasedAsync(
        StoredOverpaymentAllocation expectedAllocation,
        Guid commandId,
        Guid userId,
        DateTimeOffset releasedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedAllocation);
        EnsureCleanSerializableAttempt();

        return await dbContext.OverpaymentAllocations
            .Where(allocation =>
                allocation.TargetExpenseRequestId == expectedAllocation.TargetExpenseRequestId
                && allocation.SourceId == expectedAllocation.SourceId
                && allocation.AllocationId == expectedAllocation.AllocationId
                && allocation.Amount == expectedAllocation.Amount
                && allocation.AmountKzt == expectedAllocation.AmountKzt
                && allocation.Status == expectedAllocation.Status
                && allocation.CommandId == expectedAllocation.CommandId
                && allocation.ReservedAt == expectedAllocation.ReservedAtUtc
                && allocation.AppliedOrReleasedAt == expectedAllocation.AppliedOrReleasedAtUtc
                && allocation.UserId == expectedAllocation.UserId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(allocation => allocation.Status, "Released")
                    .SetProperty(allocation => allocation.CommandId, commandId)
                    .SetProperty(allocation => allocation.AppliedOrReleasedAt, releasedAtUtc)
                    .SetProperty(allocation => allocation.UserId, userId),
                cancellationToken);
    }

    public Task<StoredOverpaymentAllocation?> FindAllocationByIdAsync(
        Guid allocationId,
        CancellationToken cancellationToken) =>
        BuildAllocationQuery(
                dbContext.OverpaymentAllocations
                    .AsNoTracking()
                    .Where(allocation => allocation.AllocationId == allocationId))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<StoredOverpaymentSource?> FindSourceByIdAsync(
        Guid sourceId,
        CancellationToken cancellationToken) =>
        BuildSourceQuery(
                dbContext.OverpaymentSources
                    .AsNoTracking()
                    .Where(source => source.SourceId == sourceId))
            .SingleOrDefaultAsync(cancellationToken);

    // Filters and ordering must run on the entity: EF Core cannot translate
    // member access over a custom record-constructor projection (W443).
    private static IQueryable<StoredOverpaymentAllocation> BuildAllocationQuery(
        IQueryable<OverpaymentAllocation> filtered) =>
        filtered
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
                allocation.UserId));

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

    private void EnsureCleanSerializableAttempt()
    {
        if (!HasSerializableTransaction || HasPendingChanges)
        {
            throw new InvalidOperationException(
                "A clean Serializable transaction is required for release writes.");
        }
    }
}
