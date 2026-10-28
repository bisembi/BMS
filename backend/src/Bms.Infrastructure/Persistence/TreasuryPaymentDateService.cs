using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;
using Bms.Domain.Budgeting;
using Bms.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Actual dates are a read-only projection of confirmed bank facts. The old
/// append-only treasury journal is retained solely for handoff/budget scheduling
/// compatibility; it is never evidence that money left the bank.
/// </summary>
public sealed class TreasuryPaymentDateService(BmsDbContext db, IBusinessTransactionRunner transactions,
    IExpenseRequestRepository requests, ICurrentActor actor, EmployeeActorResolver employees,
    EmployeeFunctionalRoleResolver roles, IClock clock, IBusinessAuditSink audit,
    IUnitOfWork unitOfWork, IBudgetRepository? budgets = null,
    ISubmissionBudgetSnapshotReader? budgetSnapshot = null, TreasuryApprovalRestart? restart = null,
    AccountingPaymentSettings? settings=null)
    : ITreasuryPaymentDateJournal, ITreasuryPaymentDateService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Evidence(int Sequence, DateOnly Date, Guid RequestVersion, string Status);
    internal static DateOnly BusinessDate(DateTimeOffset time) => DateOnly.FromDateTime(time.ToOffset(TimeSpan.FromHours(5)).DateTime);
    internal static bool Editable(ExpenseRequestStatus status, bool confirmed, bool treasurer) => false;

    internal static DateOnly? ConfirmedDate(Guid requestId, Guid currencyId, IEnumerable<ConfirmedPaymentEvent> events)
    {
        var ledger = PaymentFactLedgerReconciler.Reconcile(requestId, currencyId, events);
        // A full reversal removes a source date; a partial reversal retains its
        // original bank date. Never substitute import, handoff or planned dates.
        return ledger.Sources.Where(s => s.ActiveAmount > 0m)
            .Select(s => (DateOnly?)BusinessDate(s.PaidAt)).Max();
    }

    private async Task<Evidence?> ReadEvidenceAsync(Guid id, CancellationToken token)
    {
        var objectId = id.ToString("D");
        var rows = await db.BusinessAuditEntries.AsNoTracking().Where(a => a.ObjectType == "TreasuryPaymentDate"
            && a.ObjectId == objectId && a.Action == "TreasuryPaymentDateSet").Select(a => a.AfterJson).Take(10001).ToArrayAsync(token);
        if (rows.Length > 10000) throw new ExpenseRequestWorkflowUnavailableException();
        var parsed = rows.Select(row => row.Length <= 1024 ? JsonSerializer.Deserialize<Evidence>(row, Json) ?? throw new ExpenseRequestWorkflowUnavailableException()
            : throw new ExpenseRequestWorkflowUnavailableException()).ToArray();
        if (parsed.Any(e => e.Sequence < 1 || e.Date == default || e.RequestVersion == Guid.Empty
                || e.Status is not ("APPROVED" or "TO_PAY" or "IN_APPROVAL"))
            || parsed.Select(e => e.Sequence).Distinct().Count() != rows.Length
            || (rows.Length > 0 && parsed.Max(e => e.Sequence) != rows.Length))
            throw new ExpenseRequestWorkflowUnavailableException();
        return parsed.OrderByDescending(e => e.Sequence).FirstOrDefault();
    }

    private async Task<bool> IsTreasurerAsync(string actorId, CancellationToken token)
    {
        if (actor.IsBmsAdministrator) return false;
        var employee = await employees.ResolveAsync(actorId, token);
        return employee.HasActiveEmployee && employee.EmployeeId is Guid id
            && (await roles.ResolveAsync(id, token)).HasExactRole(ExpenseRequestWorkflowCodes.TreasurerRoleCode);
    }

    private async Task<TreasuryPaymentDateState> StateAsync(ExpenseRequest request, string actorId, CancellationToken token)
    {
        var rows = await db.ExpenseRequestPaymentFacts.AsNoTracking().Where(p => p.ExpenseRequestId == request.Id).ToArrayAsync(token);
        var currency = await db.Currencies.AsNoTracking().SingleAsync(c => c.Code == request.CurrencyCode, token);
        var date = ConfirmedDate(request.Id, currency.Id, rows.Select(p => new ConfirmedPaymentEvent(
            p.SourceSystem, p.ExternalId, p.EventId, p.ExpenseRequestId, p.CurrencyId, p.Amount, p.AmountKzt,
            p.PaidAt, p.PaymentNumber, p.IsReversal, p.ReversedEventId)));
        return new(request.Id, request.ConcurrencyStamp, date, BusinessDate(clock.UtcNow),
            false, date is not null);
    }

    public Task<TreasuryPaymentDateState> ReadAsync(Guid id, CancellationToken token)
    {
        var actorId = ActorIdentity.RequireBusinessDataAccess(actor);
        return transactions.ExecuteAsync(async ct =>
        {
            var request = await requests.FindVisibleAsync(id, actorId, actor.HasFullBusinessDataAccess, ct)
                ?? throw new ExpenseRequestNotFoundException();
            return await StateAsync(request, actorId, ct);
        }, token);
    }

    public Task<TreasuryPaymentDateSaveResult> SetAsync(Guid id, SetTreasuryPaymentDateCommand command, string key, CancellationToken token)
    {
        ActorIdentity.RequireBusinessDataAccess(actor);
        // Keep the legacy route fail-closed for cached clients and old replay
        // keys. No actor, including the treasurer, may manufacture a bank date.
        throw new ActorForbiddenException();
    }

    private async Task<bool> MoveBudgetMonthAsync(ExpenseRequest request, DateOnly date, DateTimeOffset now, CancellationToken token,
        bool checkApprovedBudget=false)
    {
        var month = new DateOnly(date.Year, date.Month, 1);
        var changedMonth=request.EffectiveBudgetMonth!=month;
        if (!changedMonth && !checkApprovedBudget) return false;
        if(changedMonth && settings is {BudgetControlEnabled:true,ReschedulingEnabled:false})throw new TreasuryReschedulingPausedException();
        if (budgets is null || budgetSnapshot is null || restart is null)
            throw new ExpenseRequestWorkflowUnavailableException();
        if(changedMonth) request.SetTreasuryBudgetMonth(date);
        var calculation = ExpenseRequestBudgetProjectionCalculator.Calculate(request);
        if (!calculation.IsComplete) throw new ExpenseRequestWorkflowUnavailableException();
        var before=changedMonth ? await budgets.ReplaceForRequestAsync(request.Id,calculation.Projections,token)
            : await budgets.ListRequestProjectionsAsync([request.Id],token);
        if(changedMonth) await unitOfWork.SaveChangesAsync(token);
        SubmissionBudgetPreflight preflight;
        try
        {
            preflight=SubmissionBudgetPreflightEvaluator.Evaluate(
                await budgetSnapshot.ReadAsync(request.Id, calculation.Projections, token));
        }
        catch(OperationCanceledException){throw;}
        catch(Exception e)when(e is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {throw new TreasuryBudgetCheckUnavailableException();}
        if (!preflight.IsReady || preflight.PositiveDeficitKzt is not decimal deficit)
            throw new ExpenseRequestWorkflowUnavailableException();
        // A handoff does not reopen an unchanged approved financial version,
        // including the supported register-disabled compatibility mode. Keep
        // consistency checks above, but never replace its approved decision
        // merely because other requests or the current limit have changed.
        var repeated=changedMonth && deficit>0m;
        if(repeated) await restart.RestartAsync(request,deficit,now,token);
        audit.Append(Guid.NewGuid(), now, ActorIdentity.RequireBusinessDataAccess(actor), "ExpenseRequest",
            request.Id.ToString("D"), changedMonth ? "TreasuryBudgetMonthChanged" : "TreasuryBudgetRechecked", JsonSerializer.Serialize(before, Json),
            JsonSerializer.Serialize(new { month, deficit, repeatedApproval = repeated, calculation.Projections }, Json),
            Guid.NewGuid().ToString("N"));
        return repeated;
    }

    public async Task<bool> PrepareHandoffAsync(Guid requestId, Guid expectedVersion, string actorExternalId,
        DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
    {
        var request = await requests.FindForWorkflowUpdateAsync(requestId, cancellationToken)
            ?? throw new ExpenseRequestNotFoundException();
        if (request.ConcurrencyStamp != expectedVersion || request.Status != ExpenseRequestStatus.Approved)
            throw new ExpenseRequestVersionConflictException();
        if (actor.ExternalId != actorExternalId || !await IsTreasurerAsync(actorExternalId, cancellationToken))
            throw new ActorForbiddenException();
        if (await HasActivePaymentAsync(request, cancellationToken))
            throw new TreasuryPaymentDateConfirmedException();
        var previous = await ReadEvidenceAsync(requestId, cancellationToken);
        var selected = (previous?.Status == "APPROVED" && previous.RequestVersion == expectedVersion)
            || previous?.Status == "IN_APPROVAL" ? previous.Date : BusinessDate(occurredAtUtc);
        // Handoff is not a financial edit. Even in a later month, consume the
        // request's existing budget month, retaining legacy month evidence and
        // its frozen decision. The handoff date below remains a separate journal.
        var budgetMonth = request.EffectiveBudgetMonth ?? throw new ExpenseRequestWorkflowUnavailableException();
        var restarted = await MoveBudgetMonthAsync(request, budgetMonth, occurredAtUtc, cancellationToken,checkApprovedBudget:true);
        if (restarted)
            Append(requestId, previous, new((previous?.Sequence ?? 0) + 1, selected, request.ConcurrencyStamp, "IN_APPROVAL"), actorExternalId, occurredAtUtc);
        return !restarted;
    }

    public async Task RecordHandoffAsync(Guid requestId, Guid previousVersion, Guid newVersion, string actorExternalId,
        DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != System.Data.IsolationLevel.Serializable
            || previousVersion == Guid.Empty || newVersion == Guid.Empty || previousVersion == newVersion
            || actor.ExternalId != actorExternalId || !await IsTreasurerAsync(actorExternalId, cancellationToken)
            || !db.ChangeTracker.Entries<ExpenseRequest>().Any(e => e.Entity.Id == requestId
                && e.Entity.Status == ExpenseRequestStatus.ToPay && e.Entity.ConcurrencyStamp == newVersion))
            throw new ExpenseRequestWorkflowUnavailableException();
        var tracked=db.ChangeTracker.Entries<ExpenseRequest>().Single(e=>e.Entity.Id==requestId).Entity;
        if (await HasActivePaymentAsync(tracked, cancellationToken))
            throw new TreasuryPaymentDateConfirmedException();
        var previous = await ReadEvidenceAsync(requestId, cancellationToken);
        var selected = (previous?.Status == "APPROVED" && previous.RequestVersion == previousVersion)
            || previous?.Status == "IN_APPROVAL"
            ? previous.Date : BusinessDate(occurredAtUtc);
        Append(requestId, previous, new((previous?.Sequence ?? 0) + 1, selected, newVersion, "TO_PAY"), actorExternalId, occurredAtUtc);
    }

    private void Append(Guid id, Evidence? before, Evidence after, string actorId, DateTimeOffset time)
    {
        audit.Append(Guid.NewGuid(), time, actorId, "TreasuryPaymentDate", id.ToString("D"), "TreasuryPaymentDateSet",
            before is null ? "{}" : JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(after, Json), Guid.NewGuid().ToString("N"));
    }

    private async Task<bool> HasActivePaymentAsync(ExpenseRequest request,CancellationToken token)
    {
        var rows=await db.ExpenseRequestPaymentFacts.AsNoTracking().Where(p=>p.ExpenseRequestId==request.Id).ToArrayAsync(token);
        if(rows.Length==0)return false;
        var currency=await db.Currencies.AsNoTracking().SingleAsync(c=>c.Code==request.CurrencyCode,token);
        var ledger=PaymentFactLedgerReconciler.Reconcile(request.Id,currency.Id,rows.Select(p=>new ConfirmedPaymentEvent(
            p.SourceSystem,p.ExternalId,p.EventId,p.ExpenseRequestId,p.CurrencyId,p.Amount,p.AmountKzt,p.PaidAt,p.PaymentNumber,p.IsReversal,p.ReversedEventId)));
        return ledger.ActiveAmount>0m||ledger.ActiveAmountKzt>0m;
    }
}
