using Bms.Application.Payments;
using Bms.Application.Accruals.Notifications;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Internal bridge to the immutable fact/projection writers. The legacy W447
/// orchestrator stays dormant. The separately approved accounting258 worker
/// uses the settlement adapter, never a public payment mutation endpoint.
/// </summary>
internal sealed partial class EfCoreConfirmedPaymentApplicationRuntime :
    IConfirmedPaymentApplicationRuntime
{
    private readonly BmsDbContext dbContext;
    private readonly IAccrualNotificationControl notificationControl;

    internal EfCoreConfirmedPaymentApplicationRuntime(BmsDbContext dbContext,
        IAccrualNotificationControl notificationControl)
    {
        this.dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        this.notificationControl = notificationControl ??
            throw new ArgumentNullException(nameof(notificationControl));
    }

    public async Task<bool> StorePaymentFactAsync(
        ConfirmedPaymentEvent paymentEvent,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await TreasuryPaymentFactWriter.WriteAsync(
            paymentEvent,
            processedAtUtc,
            new EfCoreTreasuryPaymentFactWriteRuntime(dbContext),
            cancellationToken);
        return result.Outcome == TreasuryPaymentFactWriteOutcome.Stored;
    }

    public async Task<IReadOnlyList<ConfirmedPaymentEvent>> ReadLedgerAsync(
        Guid expenseRequestId,
        Guid currencyId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.ExpenseRequestPaymentFacts
            .AsNoTracking()
            .Where(item => item.ExpenseRequestId == expenseRequestId
                && item.CurrencyId == currencyId)
            .OrderBy(item => item.PaidAt)
            .ThenBy(item => item.EventId)
            .ToArrayAsync(cancellationToken);
        return rows
            .Select(row => new ConfirmedPaymentEvent(
                row.SourceSystem,
                row.ExternalId,
                row.EventId,
                row.ExpenseRequestId,
                row.CurrencyId,
                row.Amount,
                row.AmountKzt,
                row.PaidAt,
                row.PaymentNumber,
                row.IsReversal,
                row.ReversedEventId))
            .ToArray();
    }

    public async Task<PaymentApplicationRequestSnapshot?> ReadRequestAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken)
    {
        var request = await dbContext.ExpenseRequests
            .AsNoTracking()
            .Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.Id == expenseRequestId, cancellationToken);
        if (request is null)
        {
            return null;
        }

        var lines = request.Lines
            .OrderBy(line => line.SortOrder)
            .Select(line => new PaymentSettlementLineSnapshot(
                line.Id,
                line.SortOrder,
                line.Amount,
                line.AmountKzt ?? line.Amount))
            .ToArray();
        return new PaymentApplicationRequestSnapshot(
            request.Id,
            request.Status,
            request.CurrencyCode,
            request.OrganizationId,
            request.CounterpartyId,
            lines);
    }

    public async Task<PaymentApplicationOutcome> ApplyProjectionAsync(
        KztPaymentApplicationPlan plan,
        string actorExternalId,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await TreasuryKztPaymentProjectionWriter.ApplyAsync(
            plan,
            appliedAtUtc,
            new EfCoreTreasuryKztPaymentProjectionRuntime(dbContext),
            cancellationToken);
        var origin = await EnsureAccrualOriginAsync(plan, appliedAtUtc, cancellationToken);
        if (result.AffectedEntries > 0 || origin.AffectedEntries > 0)
        {
            var notification = await notificationControl.CheckAsync(plan.ExpenseRequestId,
                actorExternalId, plan.TargetStatus == KztPaymentTargetStatus.Paid
                    ? AccrualNotificationTrigger.FullPayment : AccrualNotificationTrigger.PaymentChanged,
                appliedAtUtc, cancellationToken);
            if (notification.HasChanges) { await dbContext.SaveChangesAsync(cancellationToken); }
        }
        return new PaymentApplicationOutcome(
            result.PreviousStatus,
            result.AppliedStatus,
            checked(result.AffectedEntries + origin.AffectedEntries),
            origin.CycleId,
            origin.FullyPaidAtUtc,
            origin.CreatedCycle);
    }

    public Task<bool> HasActiveDeficitAsync(
        PaymentApplicationScope scope,
        CancellationToken cancellationToken) =>
        dbContext.OverpaymentDeficits
            .AsNoTracking()
            .AnyAsync(
                deficit => deficit.OrganizationId == scope.OrganizationId
                    && deficit.CounterpartyId == scope.CounterpartyId
                    && deficit.CurrencyId == scope.CurrencyId
                    && deficit.SettledAt == null
                    && deficit.CurrentAmount > 0m,
                cancellationToken);

    internal async Task ApplyAccountingSettlementAsync(Guid requestId,
        PaymentSettlementCalculation settlement, string actor, DateTimeOffset now, CancellationToken token)
    {
        var result = await TreasuryKztPaymentProjectionWriter.ApplySettlementAsync(requestId,
            settlement, now, new EfCoreTreasuryKztPaymentProjectionRuntime(dbContext), token);
        var plan = new KztPaymentApplicationPlan(requestId,
            settlement.Progress == PaymentSettlementProgress.FullySettled ? KztPaymentTargetStatus.Paid :
            settlement.Progress == PaymentSettlementProgress.Unpaid ? KztPaymentTargetStatus.ToPay : KztPaymentTargetStatus.PartiallyPaid,
            settlement.Progress, settlement.OriginalReserveKzt, settlement.ActualSettledAmountKzt,
            settlement.ReleasedReserveKzt, 0m, settlement.ReleasedReserveKzt, settlement.RemainingReserveKzt,
            -settlement.ReleasedReserveKzt, settlement.ReleasedReserveKzt, true, []);
        var origin = await EnsureAccrualOriginAsync(plan, now, token, settlement.ObligationAmountInRequestCurrency);
        if (result.AffectedEntries > 0 || origin.AffectedEntries > 0)
        {
            var change = await notificationControl.CheckAsync(requestId, actor,
                settlement.Progress == PaymentSettlementProgress.FullySettled
                    ? AccrualNotificationTrigger.FullPayment : AccrualNotificationTrigger.PaymentChanged, now, token);
            if (change.HasChanges) await dbContext.SaveChangesAsync(token);
        }
    }

    public Task<bool> HasAnyOverpaymentSourceAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken) =>
        dbContext.OverpaymentSources.AsNoTracking().AnyAsync(
            source => source.SourceExpenseRequestId == expenseRequestId,
            cancellationToken);

    public async Task<bool> CreateOverpaymentSourceAsync(
        PaymentApplicationScope scope,
        Guid sourceExpenseRequestId,
        Guid sourceEventId,
        decimal amountKzt,
        DateTimeOffset occurredAtUtc,
        bool allowCreate,
        CancellationToken cancellationToken)
    {
        var result = await TreasuryOverpaymentSourceCreationWriter.CreateAsync(
            new TreasuryOverpaymentSourceCreationCommand(
                Guid.NewGuid(),
                scope.OrganizationId,
                scope.CounterpartyId,
                scope.CurrencyId,
                sourceExpenseRequestId,
                sourceEventId,
                amountKzt,
                occurredAtUtc,
                AllowCreate: allowCreate),
            new EfCoreTreasuryOverpaymentSourceCreationRuntime(dbContext),
            cancellationToken);
        return result.Outcome == TreasuryOverpaymentSourceCreationOutcome.Created;
    }
}
