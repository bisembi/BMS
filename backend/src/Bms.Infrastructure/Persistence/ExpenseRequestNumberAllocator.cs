using System.Data;
using Bms.Application.Abstractions;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed partial class ExpenseRequestNumberAllocator(BmsDbContext db) : IExpenseRequestNumberAllocator
{
    public async Task<string> AllocateAsync(
        Guid costCenterId,
        CancellationToken cancellationToken)
    {
        EnsureSerializableTransaction();
        await AcquireAsync(cancellationToken);
        if (costCenterId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A CFO is required for an expense-request number.");
        }

        // Lock the CFO anchor before reading/creating its sequence row. This
        // makes a concurrent first request serialize on the same cost center.
        var costCenter = await db.CostCenters
            .FromSqlInterpolated($"""
                SELECT [c].* FROM [bms_web].[CostCenters] AS [c] WITH (UPDLOCK, HOLDLOCK)
                WHERE [c].[Id] = {costCenterId}
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (costCenter is null || !costCenter.IsActive || costCenter.IsGroup)
        {
            throw new DomainRuleViolationException("The selected CFO is inactive or unavailable.");
        }

        var prefix = ExpenseRequestDocumentNumber.CanonicalizePrefix(costCenter.Code);
        var counter = await db.ExpenseRequestCostCenterCounters
            .FromSqlInterpolated($"""
                SELECT [counter].* FROM [bms_web].[ExpenseRequestCostCenterCounters] AS [counter] WITH (UPDLOCK, HOLDLOCK)
                WHERE [counter].[CostCenterId] = {costCenterId}
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (counter is null)
        {
            counter = new ExpenseRequestCostCenterCounter
            {
                CostCenterId = costCenterId,
                Prefix = prefix,
                LastIssuedOrdinal = 0
            };
            db.ExpenseRequestCostCenterCounters.Add(counter);
        }
        else if (!string.Equals(counter.Prefix, prefix, StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException(
                "The CFO code changed after expense-request numbering started; preserve the existing sequence prefix.");
        }

        if (db.Entry(counter).State != EntityState.Added) await db.Entry(counter).ReloadAsync(cancellationToken);

        var next = ExpenseRequestDocumentNumber.Next(prefix, counter.LastIssuedOrdinal);
        // Legacy clients/copy commands must not take a number held by an open form.
        while (await db.ExpenseRequestNumberLeases.AsNoTracking().AnyAsync(
            row => row.Number == next.Number && !row.Consumed && row.ExpiresAtUtc > DateTimeOffset.UtcNow,
            cancellationToken))
            next = ExpenseRequestDocumentNumber.Next(prefix, next.Ordinal);
        if (await db.ExpenseRequests.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(request => request.RequestNumber == next.Number, cancellationToken))
        {
            throw new DomainRuleViolationException("The next expense-request number is already in use.");
        }

        counter.LastIssuedOrdinal = next.Ordinal;
        return next.Number;
    }

    private void EnsureSerializableTransaction()
    {
        var transaction = db.Database.CurrentTransaction;
        if (transaction is null
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
        {
            throw new InvalidOperationException(
                "Expense-request numbering requires an active Serializable transaction.");
        }
    }
}
