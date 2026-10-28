using System.Globalization;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Domain.Budgeting;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed partial class AccountingPaymentReconciliation
{
    /// <summary>Operator-only explicit decisions. There is deliberately no HTTP
    /// route and no automatic tolerance. Preview performs the same admission
    /// checks without writing. All approved pools commit together or not at all.</summary>
    public Task<IReadOnlyList<ApprovedAccountingGroup400>> ApplyApprovedAllocations400Async(
        IReadOnlyList<ApprovedAccountingPlan400> plans, bool apply, CancellationToken token) =>
        transactions.ExecuteAsync<IReadOnlyList<ApprovedAccountingGroup400>>(async ct =>
    {
        if (plans.Count is < 1 or > 20 || plans.Select(p => p.ApprovalId).Distinct().Count() != plans.Count
            || plans.Select(p => p.DocumentKey).Distinct(StringComparer.Ordinal).Count() != plans.Count)
            throw new InvalidOperationException("Unique bounded approval plans required.");
        await AccountingPaymentCaptureLock.AcquireAsync(db,false,ct,location);
        await AccountingPaymentCaptureLock.AcquireAsync(db,true,ct,location);
        var fact = await CurrentDdsActualsSource.ReadRequiredAsync(dds,ct);
        var capture = await source.ReadAsync(ct);
        await AcquireApprovalLock400Async(ct);
        if (capture.CapturedAtUtc < fact.CapturedAtUtc || capture.CutoffExclusiveLocal < fact.CutoffExclusiveLocal
            || capture.CapturedAtUtc < clock.UtcNow.AddHours(-36))
            throw new InvalidOperationException("Fresh coherent accounting capture required.");
        var all = await ApprovedAccountingEvidence400.ReadAsync(db,ct,includeReversed:true);
        var active = await ApprovedAccountingEvidence400.ReadAsync(db,ct);
        var represented = await source.ReadRepresentedLegsAsync(fact.Id,plans.Select(p=>p.DocumentKey).ToArray(),ct);
        var currency = await db.Currencies.AsNoTracking().SingleAsync(c=>c.Code=="KZT" && c.NumericCode=="398",ct);
        var parties = await db.Counterparties.AsNoTracking().ToArrayAsync(ct);
        var identities428 = await CounterpartySourceIdentity428.ReadAsync(db,parties,ct);
        var now = clock.UtcNow;
        var pending = new List<(ApprovedAccountingGroup400 Group, Dictionary<Guid,ExpenseRequest> Requests)>();
        var answer = new List<ApprovedAccountingGroup400>();
        var targetIds = new HashSet<Guid>();
        foreach (var plan in plans)
        {
            if (plan.ApprovalId == Guid.Empty || string.IsNullOrWhiteSpace(plan.Reason) || plan.Reason.Length > 2000
                || !plan.ApprovedBy.StartsWith("S-1-5-",StringComparison.Ordinal))
                throw new InvalidOperationException("Named financial approval and reason required.");
            var approvers = await (from mapping in db.EmployeeActorMappings.AsNoTracking()
                join employee in db.Employees.AsNoTracking() on mapping.EmployeeId equals employee.Id
                join assignment in db.EmployeeFunctionalRoles.AsNoTracking() on employee.Id equals assignment.EmployeeId
                join role in db.FunctionalRoles.AsNoTracking() on assignment.FunctionalRoleId equals role.Id
                where mapping.ActorExternalId==plan.ApprovedBy && mapping.IsActive && employee.IsActive
                    && assignment.IsActive && role.IsActive && role.Code=="FIN_DIRECTOR"
                select employee.Id).Distinct().CountAsync(ct);
            if (approvers != 1) throw new InvalidOperationException("One active financial director approval required.");
            var existing = all.SingleOrDefault(g=>g.ApprovalId==plan.ApprovalId);
            if (existing is not null)
            {
                if (!active.Any(g=>g.ApprovalId==existing.ApprovalId) || !SameApproval400(plan,existing))
                    throw new InvalidOperationException("Approval retry changed, or the original approval was reversed.");
                var current=capture.Documents.SingleOrDefault(d=>d.DocumentKey==existing.DocumentKey);
                if(current is null || !current.Eligible || current.FinancialHash!=existing.FinancialHash
                    || existing.Allocations.Any(a=>!identities428.MatchesExisting(current,a.CounterpartyId))
                    || !SameLegs(existing.Legs,represented.Where(l=>l.DocumentKey==existing.DocumentKey).ToArray()))
                    throw new InvalidOperationException("Approval source changed; retry cannot silently reaffirm it.");
                answer.Add(existing); continue;
            }
            if (all.Any(g=>g.DocumentKey==plan.DocumentKey)
                || await db.AccountingPaymentLinks.AnyAsync(l=>l.DocumentKey==plan.DocumentKey,ct))
                throw new InvalidOperationException("Primary document was already attributed; independent reuse refused.");
            var document = capture.Documents.SingleOrDefault(d=>d.DocumentKey==plan.DocumentKey)
                ?? throw new InvalidOperationException("Approved source document missing.");
            var legs = capture.Legs.Where(l=>l.DocumentKey==document.DocumentKey).ToArray();
            if (!document.Eligible || document.RequestNumber is not null || document.CurrencyNumericCode!="398"
                || document.FinancialHash!=plan.FinancialHash || document.Amount!=document.AmountKzt
                || legs.Length!=document.LegCount || legs.Sum(l=>l.AmountKzt)!=document.AmountKzt
                || !SameLegs(legs,represented.Where(l=>l.DocumentKey==document.DocumentKey).ToArray()))
                throw new InvalidOperationException("Exact eligible primary cash and complete DDS coverage required.");
            var organization = await db.Organizations.AsNoTracking().SingleAsync(o=>o.Code==document.OrganizationCode,ct);
            var matchedId = identities428.Resolve(document).TargetId
                ?? throw new InvalidOperationException("Unique approved counterparty identity required.");
            var paidAt = new DateTimeOffset(DateTime.Parse(legs.Max(l=>l.EntryDateLocal)
                ?? throw new InvalidOperationException("Primary cash date missing."),CultureInfo.InvariantCulture),
                TimeSpan.FromHours(5)).ToUniversalTime();
            var allocations = new List<ApprovedAccountingAllocation400>();
            var locked = new Dictionary<Guid,ExpenseRequest>();
            foreach (var target in plan.Targets.OrderBy(t=>t.ExpenseRequestId))
            {
                if (!targetIds.Add(target.ExpenseRequestId)) throw new InvalidOperationException("A target may appear in only one pool.");
                var request = await requests.FindForWorkflowUpdateAsync(target.ExpenseRequestId,ct)
                    ?? throw new InvalidOperationException("Approved target request missing.");
                if (request.RequestNumber!=target.RequestNumber || request.ConcurrencyStamp!=target.RequestVersion
                    || request.Status!=ExpenseRequestStatus.ToPay || request.CurrencyCode!="KZT" || request.PaymentOperation is not null
                    || request.OrganizationId!=organization.Id || request.CounterpartyId!=matchedId
                    || await db.ExpenseRequestPaymentFacts.AnyAsync(f=>f.ExpenseRequestId==request.Id,ct)
                    || await db.OverpaymentAllocations.AnyAsync(a=>a.TargetExpenseRequestId==request.Id,ct)
                    || await db.OverpaymentSources.AnyAsync(s=>s.SourceExpenseRequestId==request.Id,ct)
                    || await ReadDateAsync(request.Id,ct)!=document.PaymentDay)
                    throw new InvalidOperationException("Approved target identity, version, date or empty settlement changed.");
                var lines = request.Lines.Select(l=>new ApprovedAccountingLine400(l.Id,l.SortOrder,
                    ExpenseBudgetDimensions372.CostCenter(request,l),l.CashFlowItemId??throw new InvalidOperationException("Article missing."),
                    request.EffectiveBudgetMonth??throw new InvalidOperationException("Budget month missing."),l.AmountKzt??l.Amount)).ToArray();
                allocations.Add(new(request.Id,request.RequestNumber
                    ?? throw new InvalidOperationException("Approved request number missing."),request.ConcurrencyStamp,organization.Id,matchedId,currency.Id,
                    Identity(plan.ApprovalId.ToString("D"),1,request.Id.ToString("D")),request.TotalAmount,
                    target.CashAmount,target.RoundingAmount,lines));
                locked.Add(request.Id,request);
            }
            var group = new ApprovedAccountingGroup400(plan.ApprovalId,document.DocumentKey,document.FinancialHash,
                capture.Id,fact.Id,document.AmountKzt,document.PaymentDay,paidAt,now,plan.ApprovedBy,plan.Reason,legs,allocations);
            ApprovedAccountingEvidence400.Validate(group);
            pending.Add((group,locked)); answer.Add(group);
        }
        if (!apply) return answer;
        var runtime = new EfCoreConfirmedPaymentApplicationRuntime(db,notifications);
        foreach (var (group,locked) in pending)
        {
            audit.Append(group.ApprovalId,now,group.ApprovedBy,ApprovedAccountingEvidence400.ObjectType,
                group.ApprovalId.ToString("D"),ApprovedAccountingEvidence400.Approved,null,
                JsonSerializer.Serialize(group,Json),group.ApprovalId.ToString("N"));
            // The bank-fact writer refuses any pending EF state. Flush the
            // immutable approval inside this SAME transaction before inserting
            // cash; a later failure still rolls back approval and every fact.
            await db.SaveChangesAsync(ct);
            foreach (var allocation in group.Allocations)
            {
                var document = capture.Documents.Single(d=>d.DocumentKey==group.DocumentKey);
                await runtime.StorePaymentFactAsync(new("1C_APPROVED_ALLOCATION_400",
                    External(group.ApprovalId.ToString("D"),1,allocation.ExpenseRequestId.ToString("N")),allocation.EventId,
                    allocation.ExpenseRequestId,allocation.CurrencyId,allocation.CashAmount,allocation.CashAmount,
                    group.PaidAtUtc,document.DocumentNumber,false,null),now,ct);
            }
            await db.SaveChangesAsync(ct);
            foreach (var allocation in group.Allocations)
                await ProjectApproved400Async(locked[allocation.ExpenseRequestId],allocation.CurrencyId,runtime,group,ct);
        }
        if (pending.Count>0 && db.BudgetRegisterEnabled343)
            await new BudgetExecutionCash343(db,new AccountingPaymentCoverageReader(db,source),location).RefreshAsync(fact,ct);
        return answer;
    },token);

    private static bool SameApproval400(ApprovedAccountingPlan400 plan, ApprovedAccountingGroup400 group) =>
        plan.DocumentKey==group.DocumentKey && plan.FinancialHash==group.FinancialHash && plan.ApprovedBy==group.ApprovedBy
        && plan.Reason==group.Reason && plan.Targets.Count==group.Allocations.Count
        && plan.Targets.All(t=>group.Allocations.Any(a=>a.ExpenseRequestId==t.ExpenseRequestId
            && a.RequestNumber==t.RequestNumber && a.RequestVersion==t.RequestVersion
            && a.CashAmount==t.CashAmount && a.RoundingAmount==t.RoundingAmount));

    private async Task AcquireApprovalLock400Async(CancellationToken token) =>
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @r int;
            EXEC @r=sys.sp_getapplock @Resource=N'BMS-ApprovedAccountingAllocation400',
                @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
            IF @r<0 THROW 65841,N'Approved accounting allocation is busy.',1;
            """,token);

    private async Task ProjectApproved400Async(ExpenseRequest request,Guid currencyId,
        EfCoreConfirmedPaymentApplicationRuntime runtime,ApprovedAccountingGroup400 group,CancellationToken token)
    {
        var ledger=PaymentFactLedgerReconciler.Reconcile(request.Id,currencyId,await runtime.ReadLedgerAsync(request.Id,currencyId,token));
        var rounding=ApprovedPaymentRoundingPolicy400.Validate(request.Id,currencyId,request.TotalAmount,ledger,
            await ApprovedAccountingEvidence400.ReadRoundingAsync(db,request.Id,token));
        var settlement=PaymentSettlementProgressCalculator.Calculate(request.CurrencyCode,
            request.Lines.Select(l=>new PaymentSettlementLineSnapshot(l.Id,l.SortOrder,l.Amount,l.AmountKzt??l.Amount)),
            ledger.ActiveAmount+rounding,ledger.ActiveAmountKzt+rounding);
        await runtime.ApplyAccountingSettlementAsync(request.Id,settlement,ServiceActor,clock.UtcNow,token);
        var now=clock.UtcNow;
        // Runtime may already have advanced the stamp while creating an accrual
        // origin. This second bump exposes evidence changes even on a reversal.
        await db.ExpenseRequests.Where(r=>r.Id==request.Id).ExecuteUpdateAsync(s=>s
            .SetProperty(r=>r.ConcurrencyStamp,Guid.NewGuid()).SetProperty(r=>r.UpdatedAtUtc,now),token);
        var payload=JsonSerializer.Serialize(new{expenseRequestId=request.Id,approvalId=group.ApprovalId,
            sourceCaptureId=group.SourceCaptureId,ddsCaptureId=group.DdsCaptureId,
            cashAmount=ledger.ActiveAmount,approvedRoundingAmount=rounding,reason=group.Reason,settlement},Json);
        audit.Append(Guid.NewGuid(),now,group.ApprovedBy,"ExpenseRequest",request.Id.ToString("D"),"PaymentApplied",null,payload,group.ApprovalId.ToString("N"));
        outbox.Enqueue(Guid.NewGuid(),now,"expense-request.payment-applied.v1",payload,group.ApprovalId.ToString("N"));
        await db.SaveChangesAsync(token);
    }

    private async Task<int> ReverseChangedApprovals400Async(AccountingPaymentSourceSnapshot capture,DirectDdsSnapshot fact,
        IReadOnlyDictionary<string,AccountingSourceCashLeg[]> represented,List<string> warnings,
        EfCoreConfirmedPaymentApplicationRuntime runtime,CounterpartySourceIdentity428 identities428,CancellationToken token)
    {
        var count=0;
        foreach(var group in await ApprovedAccountingEvidence400.ReadAsync(db,token))
        {
            var next=capture.Documents.SingleOrDefault(d=>d.DocumentKey==group.DocumentKey);
            var currentLegs=capture.Legs.Where(l=>l.DocumentKey==group.DocumentKey).ToArray();
            var found=represented.GetValueOrDefault(group.DocumentKey)??[];
            // Identity approval is not authority to migrate an existing financial allocation.
            // Preserve the ledger when identity changed or was held; source unposting still uses
            // the original coherent DDS/reversal rules below.
            if(next is not null && next.Eligible && group.Allocations.Any(a=>!identities428.MatchesExisting(next,a.CounterpartyId)))
            {warnings.Add($"{group.DocumentKey}: идентичность контрагента требует проверки; прежнее согласованное разнесение сохранено.");continue;}
            if(next is not null && next.Eligible && next.FinancialHash==group.FinancialHash
                && SameLegs(group.Legs,found) && SameLegs(group.Legs,currentLegs)) continue;
            // Source changed, but DDS still has the old generation: preserve
            // the old coherent financial snapshot until publication catches up.
            if(next is not null && next.Eligible ? !SameLegs(currentLegs,found) : found.Length>0)
            {warnings.Add($"{group.DocumentKey}: исправление согласованного разнесения ожидает согласованный срез ДДС.");continue;}
            var locked=new Dictionary<Guid,ExpenseRequest>();
            foreach(var a in group.Allocations.OrderBy(a=>a.ExpenseRequestId))
            {
                var request=await requests.FindForWorkflowUpdateAsync(a.ExpenseRequestId,token)
                    ??throw new InvalidOperationException("Approved target missing.");
                if(request.Status is not(ExpenseRequestStatus.Paid or ExpenseRequestStatus.PartiallyPaid or ExpenseRequestStatus.ToPay))
                {warnings.Add($"{a.RequestNumber}: исправление разнесения требует проверки закрытого периода.");break;}
                locked.Add(request.Id,request);
            }
            if(locked.Count!=group.Allocations.Count)continue;
            var now=clock.UtcNow;
            foreach(var a in group.Allocations)
            {
                var original=(await runtime.ReadLedgerAsync(a.ExpenseRequestId,a.CurrencyId,token)).Single(e=>e.EventId==a.EventId);
                await runtime.StorePaymentFactAsync(original with{EventId=Identity(group.ApprovalId.ToString("D"),1,"R"+a.EventId),
                    ExternalId=External(group.ApprovalId.ToString("D"),1,"R"+a.EventId),IsReversal=true,ReversedEventId=a.EventId},now,token);
            }
            audit.Append(Guid.NewGuid(),now,ServiceActor,ApprovedAccountingEvidence400.ObjectType,group.ApprovalId.ToString("D"),
                ApprovedAccountingEvidence400.Reversed,null,JsonSerializer.Serialize(new{group.ApprovalId,sourceCaptureId=capture.Id,
                    reason="PRIMARY_ACCOUNTING_CHANGED_EXPLICIT_REAPPROVAL_REQUIRED"},Json),group.ApprovalId.ToString("N"));
            await db.SaveChangesAsync(token);
            foreach(var a in group.Allocations)
            {
                await ProjectApproved400Async(locked[a.ExpenseRequestId],a.CurrencyId,runtime,group,token);
                warnings.Add($"{a.RequestNumber}: первичный платёж изменён; прежнее разнесение и округление отменены, требуется новое явное согласование.");
                count++;
            }
        }
        return count;
    }
}
