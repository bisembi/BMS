using System.Data;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

internal sealed class EfCoreTreasuryKztPaymentProjectionRuntime :
    ITreasuryKztPaymentProjectionRuntime
{
    private readonly BmsDbContext dbContext;

    internal EfCoreTreasuryKztPaymentProjectionRuntime(BmsDbContext dbContext)
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
            DECLARE @BmsPaymentProjectionLockResult int;
            EXEC @BmsPaymentProjectionLockResult = sys.sp_getapplock
                @Resource = {resource},
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 15000,
                @DbPrincipal = N'public';
            IF @BmsPaymentProjectionLockResult < 0
                THROW 60151, N'BMS payment projection lock was not acquired.', 1;
            """, cancellationToken);
    }

    public async Task<StoredPaymentProjectionRequest?> FindRequestAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken)
    {
        var request = await dbContext.ExpenseRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == expenseRequestId, cancellationToken);
        if (request is null || request.EffectiveBudgetMonth is not { } budgetMonth)
        {
            return null;
        }

        return new StoredPaymentProjectionRequest(
            request.Id,
            request.Status,
            request.CurrencyCode,
            request.ConcurrencyStamp,
            request.ActiveApprovalRouteSnapshotNumber,
            request.ActiveApprovalRouteStageNumber,
            budgetMonth,
            request.UpdatedAtUtc);
    }

    public async Task<IReadOnlyList<StoredPaymentProjectionLine>> FindLinesAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken)
    {
        var request = await dbContext.ExpenseRequests.AsNoTracking().SingleAsync(r => r.Id == expenseRequestId, cancellationToken);
        var rows = await dbContext.ExpenseRequestLines
            .AsNoTracking()
            .Where(line => line.ExpenseRequestId == expenseRequestId)
            .OrderBy(line => line.SortOrder)
            .ToArrayAsync(cancellationToken);
        return rows
            .Select(line => new StoredPaymentProjectionLine(
                line.Id,
                line.SortOrder,
                line.Amount,
                line.AmountKzt,
                Bms.Domain.Budgeting.ExpenseBudgetDimensions372.CostCenter(request, line),
                line.CashFlowItemId ?? Guid.Empty))
            .ToArray();
    }

    public async Task<IReadOnlyList<StoredPaymentBudgetProjection>>
        FindBudgetProjectionsAsync(
            Guid expenseRequestId,
            CancellationToken cancellationToken)
    {
        var rows = await dbContext.BudgetRequestProjections
            .AsNoTracking()
            .Where(projection => projection.ExpenseRequestId == expenseRequestId)
            .OrderBy(projection => projection.ExpenseRequestLineId)
            .ToArrayAsync(cancellationToken);
        return rows
            .Select(projection => new StoredPaymentBudgetProjection(
                projection.ExpenseRequestLineId,
                projection.Key.CostCenterId,
                projection.Key.CashFlowItemId,
                projection.Key.BudgetMonth,
                projection.ReservedAmountKzt))
            .ToArray();
    }

    public async Task<StoredPaymentBudgetPosition?> FindBudgetPositionAsync(
        Guid costCenterId,
        Guid cashFlowItemId,
        DateOnly budgetMonth,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.BudgetPositions
            .FromSqlInterpolated($$"""
                SELECT
                    [CostCenterId], [CashFlowItemId], [BudgetMonth],
                    [LimitAmountKzt], [ReservedAmountKzt], [PaidAmountKzt],
                    [ConcurrencyStamp]
                FROM [bms_web].[BudgetPositions]
                    WITH (UPDLOCK, HOLDLOCK, INDEX([PK_bms_web_BudgetPositions]))
                WHERE [CostCenterId] = {{costCenterId}}
                  AND [CashFlowItemId] = {{cashFlowItemId}}
                  AND [BudgetMonth] = {{budgetMonth}}
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var position = rows.SingleOrDefault();
        return position is null
            ? null
            : new StoredPaymentBudgetPosition(
                position.CostCenterId,
                position.CashFlowItemId,
                position.BudgetMonth,
                position.ReservedAmountKzt,
                position.PaidAmountKzt,
                position.ConcurrencyStamp);
    }

    public Task<int> UpdateRequestStatusAsync(
        Guid expenseRequestId,
        Guid expectedConcurrencyStamp,
        ExpenseRequestStatus targetStatus,
        Guid nextConcurrencyStamp,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken) =>
        dbContext.ExpenseRequests
            .Where(request => request.Id == expenseRequestId
                && request.ConcurrencyStamp == expectedConcurrencyStamp)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(request => request.Status, targetStatus)
                    .SetProperty(request => request.ConcurrencyStamp, nextConcurrencyStamp)
                    .SetProperty(request => request.UpdatedAtUtc, updatedAtUtc),
                cancellationToken);

    public Task<int> ReplaceBudgetProjectionAsync(
        Guid expenseRequestId,
        Guid expenseRequestLineId,
        Guid costCenterId,
        Guid cashFlowItemId,
        DateOnly budgetMonth,
        decimal reservedAmountKzt,
        CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            MERGE [bms_web].[BudgetRequestProjections] WITH (HOLDLOCK) AS target
            USING (SELECT
                       {expenseRequestId} AS ExpenseRequestId,
                       {expenseRequestLineId} AS ExpenseRequestLineId) AS source
                ON target.[ExpenseRequestId] = source.ExpenseRequestId
               AND target.[ExpenseRequestLineId] = source.ExpenseRequestLineId
            WHEN MATCHED THEN
                UPDATE SET [ReservedAmountKzt] = {reservedAmountKzt}
            WHEN NOT MATCHED THEN
                INSERT ([Id], [ExpenseRequestId], [ExpenseRequestLineId],
                        [CostCenterId], [CashFlowItemId], [BudgetMonth], [ReservedAmountKzt])
                VALUES (NEWID(), {expenseRequestId}, {expenseRequestLineId},
                        {costCenterId}, {cashFlowItemId}, {budgetMonth}, {reservedAmountKzt});
            """, cancellationToken);

    public Task<int> DeleteBudgetProjectionAsync(
        Guid expenseRequestId,
        Guid expenseRequestLineId,
        CancellationToken cancellationToken) =>
        dbContext.BudgetRequestProjections
            .Where(projection => projection.ExpenseRequestId == expenseRequestId
                && projection.ExpenseRequestLineId == expenseRequestLineId)
            .ExecuteDeleteAsync(cancellationToken);

    public Task<int> UpdateBudgetPositionAsync(
        Guid costCenterId,
        Guid cashFlowItemId,
        DateOnly budgetMonth,
        Guid expectedConcurrencyStamp,
        decimal reservedAmountKzt,
        decimal paidAmountKzt,
        Guid nextConcurrencyStamp,
        CancellationToken cancellationToken) =>
        dbContext.BudgetPositions
            .Where(position => position.CostCenterId == costCenterId
                && position.CashFlowItemId == cashFlowItemId
                && position.BudgetMonth == budgetMonth
                && position.ConcurrencyStamp == expectedConcurrencyStamp)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(position => position.ReservedAmountKzt, reservedAmountKzt)
                    .SetProperty(position => position.PaidAmountKzt, paidAmountKzt)
                    .SetProperty(position => position.ConcurrencyStamp, nextConcurrencyStamp),
                cancellationToken);
}
