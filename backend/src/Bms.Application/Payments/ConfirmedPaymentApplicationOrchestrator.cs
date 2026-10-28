using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Payments;

namespace Bms.Application.Payments;

/// <summary>
/// What one confirmed payment event did to the request, as observed after the
/// whole transaction succeeded. Carries no identifier that a public response
/// is allowed to expose.
/// </summary>
public sealed record ConfirmedPaymentApplicationResult(
    bool IsReplay,
    ExpenseRequestStatus PreviousStatus,
    ExpenseRequestStatus AppliedStatus,
    decimal ObligationAmountKzt,
    decimal CashAppliedKzt,
    decimal ExcessAmountKzt,
    bool CreatedOverpaymentSource);

/// <summary>
/// Fixed outward failure of the confirmed-payment application. No SQL state,
/// server name, table name or stack trace ever reaches the caller.
/// </summary>
public sealed class ConfirmedPaymentApplicationUnavailableException : Exception
{
    private const string SafeMessage =
        "The confirmed payment could not be applied consistently.";

    public ConfirmedPaymentApplicationUnavailableException()
        : base(SafeMessage)
    {
    }

    public ConfirmedPaymentApplicationUnavailableException(Exception innerException)
        : base(
            SafeMessage,
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}

/// <summary>
/// The steps the orchestrator drives inside one caller-owned Serializable
/// transaction. Each is already implemented by a dormant infrastructure
/// writer; this seam keeps the application layer free of EF Core while making
/// the required order explicit and testable.
/// </summary>
public interface IConfirmedPaymentApplicationRuntime
{
    Task<bool> StorePaymentFactAsync(
        ConfirmedPaymentEvent paymentEvent,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConfirmedPaymentEvent>> ReadLedgerAsync(
        Guid expenseRequestId,
        Guid currencyId,
        CancellationToken cancellationToken);

    Task<PaymentApplicationRequestSnapshot?> ReadRequestAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken);

    Task<PaymentApplicationOutcome> ApplyProjectionAsync(
        KztPaymentApplicationPlan plan,
        string actorExternalId,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken);

    Task<bool> HasActiveDeficitAsync(
        PaymentApplicationScope scope,
        CancellationToken cancellationToken);

    // Includes every source lineage for the request, even consumed/reversed
    // sources. No approved withdrawal/attribution rule exists in this slice.
    Task<bool> HasAnyOverpaymentSourceAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken);

    Task<bool> CreateOverpaymentSourceAsync(
        PaymentApplicationScope scope,
        Guid sourceExpenseRequestId,
        Guid sourceEventId,
        decimal amountKzt,
        DateTimeOffset occurredAtUtc,
        bool allowCreate,
        CancellationToken cancellationToken);
}

public sealed record PaymentApplicationScope(
    Guid OrganizationId,
    Guid CounterpartyId,
    Guid CurrencyId);

public sealed record PaymentApplicationRequestSnapshot(
    Guid ExpenseRequestId,
    ExpenseRequestStatus Status,
    string CurrencyCode,
    Guid? OrganizationId,
    Guid? CounterpartyId,
    IReadOnlyList<PaymentSettlementLineSnapshot> Lines);

public sealed record PaymentApplicationOutcome(
    ExpenseRequestStatus PreviousStatus,
    ExpenseRequestStatus AppliedStatus,
    int AffectedEntries,
    Guid? AccrualCycleId,
    DateTimeOffset? FullyPaidAtUtc,
    bool CreatedAccrualCycle);

/// <summary>
/// Applies one already-confirmed payment event atomically: store the fact,
/// rebuild the whole ledger, plan against the immutable snapshots, apply the
/// projection, settle only the proven overpayment effects, then write audit
/// and outbox exactly once.
///
/// The type is deliberately dormant. It is not registered in dependency
/// injection, no HTTP route reaches it and no scheduler drives it; the Gate G1
/// intake path is not approved. The actor is an input, never invented here: no
/// approved integration identity exists yet, so a caller that cannot supply a
/// canonical one cannot use this orchestrator at all.
/// </summary>
public static class ConfirmedPaymentApplicationOrchestrator
{
    public const string AuditObjectType = "ExpenseRequest";
    public const string AuditAction = "PaymentApplied";
    public const string OutboxMessageType = "expense-request.payment-applied.v1";

    public static async Task<ConfirmedPaymentApplicationResult> ApplyAsync(
        ConfirmedPaymentEvent paymentEvent,
        string actorExternalId,
        DateTimeOffset processedAtUtc,
        IConfirmedPaymentApplicationRuntime runtime,
        IBusinessAuditSink auditSink,
        IOutboxSink outboxSink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paymentEvent);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(auditSink);
        ArgumentNullException.ThrowIfNull(outboxSink);

        try
        {
            ConfirmedPaymentEventContract.Validate(paymentEvent);
            ConfirmedPaymentEventContract.ValidateProcessingTime(processedAtUtc);
            if (!ActorIdentity.IsCanonical(actorExternalId))
            {
                throw new ConfirmedPaymentApplicationUnavailableException();
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 1. The fact itself. A structural conflict throws inside the writer;
            //    an exact replay reports that it was already stored.
            var stored = await runtime.StorePaymentFactAsync(
                paymentEvent,
                processedAtUtc,
                cancellationToken);

            // 2-3. Rebuild the complete ledger and read the request snapshot.
            var request = await runtime.ReadRequestAsync(
                paymentEvent.ExpenseRequestId,
                cancellationToken);
            if (request is null)
            {
                throw new ConfirmedPaymentApplicationUnavailableException();
            }

            // Foreign currency has no approved rule for splitting banked KZT
            // between the obligation and an overpayment, so the whole event is
            // refused rather than partially applied.
            if (!string.Equals(request.CurrencyCode, "KZT", StringComparison.Ordinal))
            {
                throw new ConfirmedPaymentApplicationUnavailableException();
            }

            var events = await runtime.ReadLedgerAsync(
                paymentEvent.ExpenseRequestId,
                paymentEvent.CurrencyId,
                cancellationToken);
            var ledger = PaymentFactLedgerReconciler.Reconcile(
                paymentEvent.ExpenseRequestId,
                paymentEvent.CurrencyId,
                events);

            // 4. Plan from the full ledger and the immutable line snapshots.
            var plan = KztPaymentApplicationPlanner.Plan(
                paymentEvent.ExpenseRequestId,
                request.Status,
                request.CurrencyCode,
                request.Lines,
                ledger);

            // A new reversal can consume ALL prior excess. Checking only the
            // resulting excess misses a source left without backing cash.
            // For a newly stored event, A + R is the previous ledger balance;
            // compare by subtraction to avoid overflowing that addition.
            // A replay may have later ledger events, so do not reconstruct its
            // historical balance from today's A. Every reversal still refuses
            // any existing source lineage (including legacy/consumed sources)
            // until source withdrawal and attribution are approved.
            if (paymentEvent.IsReversal && (plan.ExcessAmountKzt > 0m ||
                (stored && paymentEvent.AmountKzt >
                    plan.ObligationAmountKzt - plan.ActiveLedgerAmountKzt) ||
                await runtime.HasAnyOverpaymentSourceAsync(
                    paymentEvent.ExpenseRequestId, cancellationToken)))
            {
                throw new ConfirmedPaymentApplicationUnavailableException();
            }

            // 5. Apply the projection; the writer owns its own drift and
            //    affected-row checks and re-reads what it wrote.
            var outcome = await runtime.ApplyProjectionAsync(
                plan,
                actorExternalId,
                processedAtUtc,
                cancellationToken);
            EnsureAccrualOriginConsistent(outcome);

            // 6. Only the proven overpayment branch. Everything else fails the
            //    whole event closed instead of guessing an attribution.
            var createdSource = false;
            if (plan.ExcessAmountKzt > 0m)
            {
                createdSource = await ApplyExcessAsync(
                    paymentEvent,
                    request,
                    plan,
                    allowCreate: stored,
                    runtime,
                    cancellationToken);
            }

            // 7. Audit and outbox exactly once: an exact replay that changed
            //    nothing must not append a second record.
            var isReplay = !stored && outcome.AffectedEntries == 0;
            if (!isReplay)
            {
                var correlationId = Guid.NewGuid().ToString("N");
                var payload = BuildPayload(plan, outcome);
                auditSink.Append(
                    Guid.NewGuid(),
                    processedAtUtc,
                    actorExternalId,
                    AuditObjectType,
                    paymentEvent.ExpenseRequestId.ToString("D"),
                    AuditAction,
                    null,
                    payload,
                    correlationId);
                outboxSink.Enqueue(
                    Guid.NewGuid(),
                    processedAtUtc,
                    OutboxMessageType,
                    payload,
                    correlationId);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new ConfirmedPaymentApplicationResult(
                isReplay,
                outcome.PreviousStatus,
                outcome.AppliedStatus,
                plan.ObligationAmountKzt,
                plan.CashAppliedKzt,
                plan.ExcessAmountKzt,
                createdSource);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ConfirmedPaymentApplicationUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ConfirmedPaymentApplicationUnavailableException(exception);
        }
    }

    private static async Task<bool> ApplyExcessAsync(
        ConfirmedPaymentEvent paymentEvent,
        PaymentApplicationRequestSnapshot request,
        KztPaymentApplicationPlan plan,
        bool allowCreate,
        IConfirmedPaymentApplicationRuntime runtime,
        CancellationToken cancellationToken)
    {
        // A reversal that leaves excess would have to withdraw money already
        // turned into an overpayment source, which needs a deficit-creation
        // writer and an approved attribution rule. Neither exists.
        if (paymentEvent.IsReversal)
        {
            throw new ConfirmedPaymentApplicationUnavailableException();
        }

        if (request.OrganizationId is not { } organizationId
            || request.CounterpartyId is not { } counterpartyId)
        {
            throw new ConfirmedPaymentApplicationUnavailableException();
        }

        var scope = new PaymentApplicationScope(
            organizationId,
            counterpartyId,
            paymentEvent.CurrencyId);

        // An active deficit in the scope must be settled before free money is
        // created. Settlement is a separate approved writer; until the caller
        // runs it, this event fails closed rather than creating a source that
        // ignores the debt.
        if (await runtime.HasActiveDeficitAsync(scope, cancellationToken))
        {
            throw new ConfirmedPaymentApplicationUnavailableException();
        }

        return await runtime.CreateOverpaymentSourceAsync(
            scope,
            paymentEvent.ExpenseRequestId,
            paymentEvent.EventId,
            plan.ExcessAmountKzt,
            paymentEvent.PaidAt,
            allowCreate,
            cancellationToken);
    }

    private static void EnsureAccrualOriginConsistent(PaymentApplicationOutcome outcome)
    {
        if ((outcome.AppliedStatus == ExpenseRequestStatus.Paid && outcome.FullyPaidAtUtc is null) ||
            outcome.AccrualCycleId == Guid.Empty ||
            (outcome.AccrualCycleId is not null && outcome.FullyPaidAtUtc is null) ||
            (outcome.CreatedAccrualCycle && (outcome.AccrualCycleId is null ||
                outcome.AppliedStatus != ExpenseRequestStatus.Paid || outcome.AffectedEntries <= 0)) ||
            (outcome.FullyPaidAtUtc is { } date && (date == default ||
                date.Offset != TimeSpan.Zero || date.Ticks % TimeSpan.TicksPerMillisecond != 0)))
        {
            throw new ConfirmedPaymentApplicationUnavailableException();
        }
    }

    private static string BuildPayload(
        KztPaymentApplicationPlan plan,
        PaymentApplicationOutcome outcome) =>
        System.Text.Json.JsonSerializer.Serialize(
            new PaymentAppliedPayload(
                plan.ExpenseRequestId.ToString("D"),
                outcome.PreviousStatus.ToCode(),
                outcome.AppliedStatus.ToCode(),
                plan.ObligationAmountKzt,
                plan.CashAppliedKzt,
                plan.ExcessAmountKzt,
                outcome.AccrualCycleId,
                outcome.FullyPaidAtUtc,
                outcome.CreatedAccrualCycle),
            new System.Text.Json.JsonSerializerOptions(
                System.Text.Json.JsonSerializerDefaults.Web));

    private sealed record PaymentAppliedPayload(
        string ExpenseRequestId,
        string PreviousStatus,
        string AppliedStatus,
        decimal ObligationAmountKzt,
        decimal CashAppliedKzt,
        decimal ExcessAmountKzt,
        Guid? AccrualCycleId,
        DateTimeOffset? FullyPaidAtUtc,
        bool CreatedAccrualCycle);
}
