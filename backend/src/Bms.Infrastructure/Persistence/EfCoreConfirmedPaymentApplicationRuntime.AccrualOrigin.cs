using Bms.Application.ExpenseRequests;
using Bms.Application.Payments;
using Bms.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

internal sealed partial class EfCoreConfirmedPaymentApplicationRuntime
{
    private sealed record PaymentAccrualOrigin(
        Guid? CycleId,
        DateTimeOffset? FullyPaidAtUtc,
        bool CreatedCycle,
        int AffectedEntries);

    // The preceding projection writer holds the request lock until the caller's
    // Serializable transaction ends. No independent commit or payment endpoint.
    private async Task<PaymentAccrualOrigin> EnsureAccrualOriginAsync(
        KztPaymentApplicationPlan plan,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken, decimal? nativeObligation = null)
    {
        var request = await dbContext.ExpenseRequests.AsNoTracking()
            .Include(item => item.Lines)
            .SingleAsync(item => item.Id == plan.ExpenseRequestId, cancellationToken);
        var cycleStore = new AccrualClosingCycleSnapshotStore(dbContext);
        var cycleId = request.AccrualCycleId;
        if (cycleId is { } currentCycleId)
        {
            var existing = await cycleStore.FindAsync(
                request.Id, currentCycleId, cancellationToken);
            if (request.FullyPaidAtUtc is null || existing is null ||
                !existing.MatchesRequest(request))
            {
                throw new ConfirmedPaymentApplicationUnavailableException();
            }
        }

        // Reversal removes the active accrual through status, not by destroying
        // its origin. A first partial payment has neither a date nor a cycle.
        if (plan.TargetStatus != KztPaymentTargetStatus.Paid)
        {
            return new PaymentAccrualOrigin(cycleId, request.FullyPaidAtUtc, false, 0);
        }

        var assessment = AccrualClosingSourceSnapshotBuilder.Capture(request);
        if (assessment.State is not (AccrualClosingSourceState.Ready or
                AccrualClosingSourceState.NoManagerialAccrualLines) ||
            (assessment.State == AccrualClosingSourceState.NoManagerialAccrualLines &&
                cycleId is not null))
        {
            throw new ConfirmedPaymentApplicationUnavailableException();
        }

        var currencyId = await dbContext.Currencies.AsNoTracking()
            .Where(currency => currency.Code == request.CurrencyCode)
            .Select(currency => currency.Id).SingleAsync(cancellationToken);
        var events = await ReadLedgerAsync(request.Id, currencyId, cancellationToken);
        var fullyPaidAtUtc = ExpenseRequestFullPaymentEvidenceResolver.ResolveFirstFullyPaidAtUtc(
            request.Id, currencyId, nativeObligation ?? plan.ObligationAmountKzt, events, request.FullyPaidAtUtc,
            await ApprovedAccountingEvidence400.ReadRoundingAsync(dbContext, request.Id, cancellationToken));

        AccrualClosingCycleSnapshot? newCycle = null;
        if (assessment.Snapshot is { } source && cycleId is null)
        {
            // A lost current-cycle pointer must not silently manufacture a new
            // cycle. Only the separate reopening command advances the number.
            var number = await cycleStore.NextCycleNumberAsync(request.Id, cancellationToken);
            if (number != 1 || request.FullyPaidAtUtc is not null)
            {
                throw new ConfirmedPaymentApplicationUnavailableException();
            }
            var initialCycleId = Guid.NewGuid();
            cycleId = initialCycleId;
            newCycle = AccrualClosingCycleSnapshot.Capture(
                initialCycleId, number, appliedAtUtc, source);
        }

        if (request.FullyPaidAtUtc == fullyPaidAtUtc && request.AccrualCycleId == cycleId)
        {
            return new PaymentAccrualOrigin(cycleId, fullyPaidAtUtc, false, 0);
        }
        if (appliedAtUtc < request.UpdatedAtUtc)
        {
            throw new ConfirmedPaymentApplicationUnavailableException();
        }

        if (newCycle is not null)
        {
            cycleStore.Add(newCycle);
            // SQL026's current-cycle FK requires the principal first. Save is
            // inside the SAME caller transaction; failures later roll it back.
            await dbContext.SaveChangesAsync(cancellationToken);
            var stored = await cycleStore.FindAsync(
                request.Id, newCycle.CycleId, cancellationToken);
            if (stored is null || !stored.MatchesRequest(request) ||
                stored.CycleNumber != 1 || stored.CapturedAtUtc != appliedAtUtc ||
                stored.RequestVersion != request.ConcurrencyStamp)
            {
                throw new ConfirmedPaymentApplicationUnavailableException();
            }
        }

        var nextVersion = Guid.NewGuid();
        var affected = await dbContext.ExpenseRequests
            .Where(item => item.Id == request.Id &&
                item.ConcurrencyStamp == request.ConcurrencyStamp)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.AccrualCycleId, cycleId)
                .SetProperty(item => item.FullyPaidAtUtc, fullyPaidAtUtc)
                .SetProperty(item => item.ConcurrencyStamp, nextVersion)
                .SetProperty(item => item.UpdatedAtUtc, appliedAtUtc), cancellationToken);
        if (affected != 1)
        {
            throw new ConfirmedPaymentApplicationUnavailableException();
        }
        var persisted = await dbContext.ExpenseRequests.AsNoTracking()
            .Where(item => item.Id == request.Id)
            .Select(item => new
            {
                item.AccrualCycleId,
                item.FullyPaidAtUtc,
                item.ConcurrencyStamp,
                item.UpdatedAtUtc
            }).SingleAsync(cancellationToken);
        if (persisted.AccrualCycleId != cycleId || persisted.FullyPaidAtUtc != fullyPaidAtUtc ||
            persisted.ConcurrencyStamp != nextVersion || persisted.UpdatedAtUtc != appliedAtUtc)
        {
            throw new ConfirmedPaymentApplicationUnavailableException();
        }
        return new PaymentAccrualOrigin(cycleId, fullyPaidAtUtc, newCycle is not null, affected);
    }
}
