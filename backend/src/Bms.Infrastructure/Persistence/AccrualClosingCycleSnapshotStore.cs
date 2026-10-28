using Bms.Application.Abstractions;
using Bms.Application.ExpenseRequests;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Store for one immutable closing-cycle snapshot. Its caller must hold the
/// parent request lock inside a Serializable transaction before asking for
/// the next cycle number and adding the full snapshot.
/// </summary>
internal sealed class AccrualClosingCycleSnapshotStore(BmsDbContext dbContext)
    : IAccrualClosingCycleSnapshotStore
{
    public async Task<int> NextCycleNumberAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken)
    {
        if (expenseRequestId == Guid.Empty)
        {
            throw new InvalidOperationException("Expense request id is required.");
        }

        var last = await dbContext.AccrualClosingCycleSnapshots
            .Where(snapshot => snapshot.ExpenseRequestId == expenseRequestId)
            .Select(snapshot => (int?)snapshot.CycleNumber)
            .MaxAsync(cancellationToken);
        return checked((last ?? 0) + 1);
    }

    public void Add(AccrualClosingCycleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        dbContext.AccrualClosingCycleSnapshots.Add(new PersistedAccrualClosingCycleSnapshot
        {
            ExpenseRequestId = snapshot.ExpenseRequestId,
            CycleId = snapshot.CycleId,
            CycleNumber = snapshot.CycleNumber,
            OrganizationId = snapshot.OrganizationId,
            CostCenterId = snapshot.CostCenterId,
            RequestVersion = snapshot.RequestVersion,
            SourceUpdatedAtUtc = snapshot.SourceUpdatedAtUtc,
            CapturedAtUtc = snapshot.CapturedAtUtc,
            GrossAmount = snapshot.GrossAmount,
            VatAmount = snapshot.VatAmount,
            AmountExcludingVat = snapshot.AmountExcludingVat,
            GrossAmountKzt = snapshot.GrossAmountKzt,
            VatAmountKzt = snapshot.VatAmountKzt,
            AmountExcludingVatKzt = snapshot.AmountExcludingVatKzt
        });
        foreach (var line in snapshot.Lines)
        {
            dbContext.AccrualClosingCycleLines.Add(new PersistedAccrualClosingCycleLine
            {
                ExpenseRequestId = snapshot.ExpenseRequestId,
                CycleId = snapshot.CycleId,
                LineId = line.LineId,
                SortOrder = line.SortOrder,
                CashFlowItemId = line.CashFlowItemId,
                CostCenterId = line.CostCenterId,
                ExpenseItemId = line.ExpenseItemId,
                AccrualPeriod = line.AccrualPeriod,
                GrossAmount = line.GrossAmount,
                VatAmount = line.VatAmount,
                AmountExcludingVat = line.AmountExcludingVat,
                GrossAmountKzt = line.GrossAmountKzt,
                VatAmountKzt = line.VatAmountKzt,
                AmountExcludingVatKzt = line.AmountExcludingVatKzt
            });
        }
    }

    public async Task<AccrualClosingCycleSnapshot?> FindAsync(
        Guid expenseRequestId,
        Guid cycleId,
        CancellationToken cancellationToken)
    {
        if (expenseRequestId == Guid.Empty || cycleId == Guid.Empty)
        {
            return null;
        }

        var snapshot = await dbContext.AccrualClosingCycleSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.ExpenseRequestId == expenseRequestId &&
                candidate.CycleId == cycleId,
                cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        var lines = await dbContext.AccrualClosingCycleLines
            .AsNoTracking()
            .Where(line =>
                line.ExpenseRequestId == expenseRequestId &&
                line.CycleId == cycleId)
            .OrderBy(line => line.SortOrder)
            .Select(line => new AccrualClosingSourceLine(
                line.LineId,
                line.SortOrder,
                line.CashFlowItemId,
                line.CostCenterId,
                line.ExpenseItemId,
                line.AccrualPeriod,
                line.GrossAmount,
                line.VatAmount,
                line.AmountExcludingVat,
                line.GrossAmountKzt,
                line.VatAmountKzt,
                line.AmountExcludingVatKzt))
            .ToArrayAsync(cancellationToken);
        try
        {
            return AccrualClosingCycleSnapshot.Restore(
                snapshot.CycleId,
                snapshot.CycleNumber,
                snapshot.ExpenseRequestId,
                snapshot.OrganizationId,
                snapshot.CostCenterId,
                snapshot.RequestVersion,
                snapshot.SourceUpdatedAtUtc,
                snapshot.CapturedAtUtc,
                snapshot.GrossAmount,
                snapshot.VatAmount,
                snapshot.AmountExcludingVat,
                snapshot.GrossAmountKzt,
                snapshot.VatAmountKzt,
                snapshot.AmountExcludingVatKzt,
                lines.Select(line => new AccrualClosingCycleLine(line)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "Stored accrual closing cycle snapshot is inconsistent.");
        }
    }
}
