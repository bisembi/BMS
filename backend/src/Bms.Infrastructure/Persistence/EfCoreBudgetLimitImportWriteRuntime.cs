using System.Data;
using Bms.Application.Budgeting;
using Bms.Domain.Budgeting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

internal sealed class EfCoreBudgetLimitImportWriteRuntime :
    IBudgetLimitImportWriteRuntime
{
    private const int ManagedLockTimeoutMilliseconds = 10_000;
    private readonly BmsDbContext dbContext;

    internal EfCoreBudgetLimitImportWriteRuntime(BmsDbContext dbContext)
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
            DECLARE @BmsBudgetLimitLockResult int;
            EXEC @BmsBudgetLimitLockResult = sys.sp_getapplock
                @Resource = {{resource}},
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = {{ManagedLockTimeoutMilliseconds}},
                @DbPrincipal = N'public';
            IF @BmsBudgetLimitLockResult < 0
                THROW 60150, N'BMS budget-limit managed lock was not acquired.', 1;
            """, cancellationToken);
    }

    public async Task<bool> ReferencesExistAsync(
        IReadOnlyCollection<Guid> costCenterIds,
        IReadOnlyCollection<Guid> cashFlowItemIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(costCenterIds);
        ArgumentNullException.ThrowIfNull(cashFlowItemIds);
        if (!HasSerializableTransaction || HasPendingChanges)
        {
            throw new InvalidOperationException(
                "A clean Serializable transaction is required for reference validation.");
        }

        var distinctCostCenterIds = costCenterIds.Distinct().ToArray();
        var distinctCashFlowItemIds = cashFlowItemIds.Distinct().ToArray();
        if (distinctCostCenterIds.Any(id => id == Guid.Empty)
            || distinctCashFlowItemIds.Any(id => id == Guid.Empty))
        {
            return false;
        }

        var costCenterCount = await dbContext.CostCenters
            .AsNoTracking()
            .CountAsync(item => distinctCostCenterIds.Contains(item.Id), cancellationToken);
        if (costCenterCount != distinctCostCenterIds.Length)
        {
            return false;
        }

        var cashFlowItemCount = await dbContext.CashFlowItems
            .AsNoTracking()
            .CountAsync(item => distinctCashFlowItemIds.Contains(item.Id), cancellationToken);
        return cashFlowItemCount == distinctCashFlowItemIds.Length;
    }

    public async Task<IReadOnlyList<BudgetLimitPositionSnapshot>> ReadPositionsForUpdateAsync(
        IReadOnlyCollection<BudgetKey> keys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (!HasSerializableTransaction || HasPendingChanges)
        {
            throw new InvalidOperationException(
                "A clean Serializable transaction is required for budget-position reads.");
        }

        var orderedKeys = keys
            .Distinct()
            .OrderBy(BusinessManagedLockResources.Budget, StringComparer.Ordinal)
            .ToArray();
        var result = new List<BudgetLimitPositionSnapshot>(orderedKeys.Length);
        foreach (var key in orderedKeys)
        {
            var rows = await dbContext.BudgetPositions
                .FromSqlInterpolated($$"""
                    SELECT
                        [CostCenterId], [CashFlowItemId], [BudgetMonth],
                        [LimitAmountKzt], [ReservedAmountKzt], [PaidAmountKzt],
                        [ConcurrencyStamp]
                    FROM [bms_web].[BudgetPositions]
                        WITH (UPDLOCK, HOLDLOCK, INDEX([PK_bms_web_BudgetPositions]))
                    WHERE [CostCenterId] = {{key.CostCenterId}}
                      AND [CashFlowItemId] = {{key.CashFlowItemId}}
                      AND [BudgetMonth] = {{key.BudgetMonth}}
                    """)
                .AsNoTracking()
                .ToArrayAsync(cancellationToken);
            var position = rows.SingleOrDefault();
            if (position is not null)
            {
                result.Add(ToSnapshot(position));
            }
        }

        return result.AsReadOnly();
    }

    public async Task<int> InsertAsync(
        BudgetLimitImportItem item,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        EnsureCleanSerializableAttempt();

        var position = BudgetPosition.Create(item.Key, item.LimitAmountKzt);
        dbContext.BudgetPositions.Add(position);
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.Entry(position).State = EntityState.Detached;
        }
    }

    public Task<int> SetLimitAsync(
        BudgetLimitImportItem item,
        Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (expectedConcurrencyStamp == Guid.Empty)
        {
            throw new ArgumentException(
                "An expected budget-position concurrency stamp is required.",
                nameof(expectedConcurrencyStamp));
        }
        EnsureCleanSerializableAttempt();

        var key = item.Key;
        var nextConcurrencyStamp = Guid.NewGuid();
        return dbContext.BudgetPositions
            .Where(position =>
                position.CostCenterId == key.CostCenterId
                && position.CashFlowItemId == key.CashFlowItemId
                && position.BudgetMonth == key.BudgetMonth
                && position.ConcurrencyStamp == expectedConcurrencyStamp)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(position => position.LimitAmountKzt, item.LimitAmountKzt)
                    .SetProperty(position => position.ConcurrencyStamp, nextConcurrencyStamp),
                cancellationToken);
    }

    private void EnsureCleanSerializableAttempt()
    {
        if (!HasSerializableTransaction || HasPendingChanges)
        {
            throw new InvalidOperationException(
                "A clean Serializable transaction is required for budget-limit writes.");
        }
    }

    private static BudgetLimitPositionSnapshot ToSnapshot(BudgetPosition position) =>
        BudgetLimitPositionSnapshot.Create(
            position.Key,
            position.LimitAmountKzt,
            position.ReservedAmountKzt,
            position.PaidAmountKzt,
            position.ConcurrencyStamp);
}
