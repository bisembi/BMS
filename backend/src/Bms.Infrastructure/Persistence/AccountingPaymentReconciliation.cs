using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Accruals.Notifications;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed record AccountingPaymentRun(Guid PaymentCaptureId, Guid DdsCaptureId,
    int AppliedDocuments, int ReversedDocuments, int AmbiguousRequests, IReadOnlyList<string> Warnings);

/// <summary>Internal service-only intake. No HTTP mutation endpoint, source SQL
/// credential, invented employee, fuzzy match or financial fallback. All money,
/// immutable evidence, workflow, reserve and notifications share one commit.</summary>
public sealed partial class AccountingPaymentReconciliation(BmsDbContext db, IBusinessTransactionRunner transactions,
    IExpenseRequestRepository requests, IAccountingPaymentSourceReader source, IDirectDdsReplicaReader dds,
    IAccrualNotificationControl notifications, IBusinessAuditSink audit, IOutboxSink outbox, IClock clock,
    AccountingReplicaLocation? location=null)
{
    internal const string ServiceActor="S-1-5-80-3928983041-4098722958-1494938123-1762692221-3803012568";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private sealed record DateEvidence(int Sequence, DateOnly Date, Guid RequestVersion, string Status);

    internal static void EnlistReversalLink407(BmsDbContext context, AccountingPaymentLink link)
    {
        if(context.Entry(link).State==EntityState.Detached)
            context.AccountingPaymentLinks.Attach(link);
    }

    public Task<AccountingPaymentRun> ReconcileAsync(CancellationToken token) => transactions.ExecuteAsync(async ct =>
    {
        await AccountingPaymentCaptureLock.AcquireAsync(db,false,ct,location);
        await AccountingPaymentCaptureLock.AcquireAsync(db,true,ct,location);
        var fact=await CurrentDdsActualsSource.ReadRequiredAsync(dds,ct);
        var capture=await source.ReadAsync(ct);
        if(capture.Documents.Any(d=>d.PayrollAdvanceEligible)&&capture.PayrollEvidenceDdsSnapshotId!=fact.Id)
            throw new InvalidOperationException("Payroll evidence must belong to the accepted DDS capture.");
        await AcquireApprovalLock400Async(ct);
        if(capture.CapturedAtUtc<fact.CapturedAtUtc || capture.CutoffExclusiveLocal<fact.CutoffExclusiveLocal
            ||capture.CapturedAtUtc<clock.UtcNow.AddHours(-36))
            throw new InvalidOperationException("Payment capture must follow the current DDS capture and be fresh.");
        var documents=capture.Documents.ToDictionary(d=>d.DocumentKey,StringComparer.Ordinal);
        var legs=capture.Legs.GroupBy(l=>l.DocumentKey).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
        var history=await db.AccountingPaymentLinks.Take(200001).ToArrayAsync(ct);
        if(history.Length>200000)throw new InvalidOperationException("Payment history requires bounded archival review.");
        var active=history.Where(l=>l.ReversedAtUtc==null).ToArray();
        var approvedHistory400=await ApprovedAccountingEvidence400.ReadAsync(db,ct,includeReversed:true);
        var represented=await source.ReadRepresentedLegsAsync(fact.Id,
            capture.Documents.Where(Eligible).Select(d=>d.DocumentKey)
                .Concat(active.Select(l=>l.DocumentKey)).Concat(approvedHistory400.Select(g=>g.DocumentKey))
                .Distinct(StringComparer.Ordinal).ToArray(),ct);
        var representedByKey=represented.GroupBy(l=>l.DocumentKey).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
        var warnings=new List<string>();var applied=0;var reversed=0;
        var organizations=await db.Organizations.AsNoTracking().ToArrayAsync(ct);
        var currencies=await db.Currencies.AsNoTracking().ToArrayAsync(ct);
        var parties=await db.Counterparties.AsNoTracking().ToArrayAsync(ct);
        var identities428=await CounterpartySourceIdentity428.ReadAsync(db,parties,ct);
        var resolved428=capture.Documents.Where(d=>d.Eligible && !d.PayrollAdvanceEligible && d.RequestNumber is null)
            .ToDictionary(d=>d.DocumentKey,identities428.Resolve,StringComparer.Ordinal);
        var blocked428=resolved428.Values.Count(r=>r.IsRegistryBlock);
        if(blocked428>0) warnings.Add($"{blocked428}: сопоставление контрагента заблокировано реестром идентичности; финансовые связи не изменены.");
        AccountingPaymentCandidate? Resolve(AccountingSourcePayment d)
        {
            var org=organizations.Where(o=>o.Code==d.OrganizationCode).ToArray();
            var currency=currencies.Where(c=>c.NumericCode==d.CurrencyNumericCode).ToArray();
            var partyId=resolved428.GetValueOrDefault(d.DocumentKey)?.TargetId;
            if(org.Length!=1||currency.Length!=1||partyId is null) return null;
            return new(d.DocumentKey,d.FinancialHash,org[0].Id,partyId.Value,currency[0].Code,d.PaymentDay,d.Amount,d.AmountKzt);
        }
        bool IdentityMatches(AccountingSourcePayment d, Guid? org, Guid? party, string currency,
            ExpensePaymentOperation? operation, string? number)
        {
            if (operation is null)
                return d.RequestNumber is null && d.Eligible && Resolve(d) is {} mapped
                    && mapped.OrganizationId == org && mapped.CounterpartyId == party && mapped.CurrencyCode == currency;
            if(operation.IsPayrollAdvance && d.RequestNumber is null)
                return d.PayrollAdvanceEligible && currency=="KZT"
                    && organizations.Count(o=>o.Id==org&&o.Code==d.OrganizationCode)==1&&d.CurrencyNumericCode=="398";
            return d.OperationReferenceEligible && d.RequestNumber == number
                && organizations.Count(o => o.Id == org && o.Code == d.OrganizationCode) == 1
                && currencies.Count(c => c.Code == currency && c.NumericCode == d.CurrencyNumericCode) == 1;
        }
        bool Covered(AccountingSourcePayment d)=>legs.TryGetValue(d.DocumentKey,out var expected)
            &&representedByKey.TryGetValue(d.DocumentKey,out var found)&&SameLegs(expected,found);
        var runtime=new EfCoreConfirmedPaymentApplicationRuntime(db,notifications);
        reversed+=await ReverseChangedApprovals400Async(capture,fact,representedByKey,warnings,runtime,identities428,ct);
        db.ChangeTracker.Clear();
        var knownIds=history.Select(l=>l.ExpenseRequestId).Distinct().ToArray();
        var headers=await db.ExpenseRequests.AsNoTracking().Where(r=>knownIds.Contains(r.Id))
            .Select(r=>new{r.Id,r.OrganizationId,r.CounterpartyId,r.CurrencyCode,r.PaymentOperation,r.RequestNumber}).ToDictionaryAsync(r=>r.Id,ct);
        // Corrections first. A changed/unposted source is not reversed while
        // the pinned DDS still contains its old footprint. Closed requests are
        // quarantined for explicit period review, never silently reopened.
        var known=history.GroupBy(l=>l.DocumentKey).Select(g=>g.MaxBy(l=>l.Revision)??throw new InvalidOperationException("Source revision missing."))
            .Where(l=>l.ReversedAtUtc==null || documents.TryGetValue(l.DocumentKey,out var d)&&Eligible(d)&&Covered(d));
        foreach(var group in known.GroupBy(l=>l.ExpenseRequestId).OrderBy(g=>g.Key))
        {
            var header=headers.GetValueOrDefault(group.Key)??throw new InvalidOperationException("Linked request missing.");
            var identityDrift=group.Any(l=>documents.TryGetValue(l.DocumentKey,out var d)&&Eligible(d)
                && !IdentityMatches(d,header.OrganizationId,header.CounterpartyId,header.CurrencyCode,header.PaymentOperation,header.RequestNumber));
            var changed=group.Where(l=>l.ReversedAtUtc!=null||!documents.TryGetValue(l.DocumentKey,out var d)||!Eligible(d)
                ||d.FinancialHash!=l.FinancialHash||!Covered(d)).ToArray();
            if(changed.Length==0&&!identityDrift) continue;
            var request=await requests.FindForWorkflowUpdateAsync(group.Key,ct)??throw new InvalidOperationException("Linked request missing.");
            if(request.Status is not(ExpenseRequestStatus.ToPay or ExpenseRequestStatus.PartiallyPaid or ExpenseRequestStatus.Paid))
            { warnings.Add($"{request.RequestNumber}: исправление оплаты требует проверки закрытого состояния.");continue; }
            // Rebuild the WHOLE linked request ledger, not just the changed
            // document: line reserve allocations otherwise drift when an earlier
            // payment is reversed and another payment remains active.
            var changes=new List<(AccountingPaymentLink Old,AccountingSourcePayment? New)>();
            var valid=true;
            var treasuryDay=await ReadDateAsync(request.Id,ct);
            foreach(var link in group)
            {
                documents.TryGetValue(link.DocumentKey,out var next);
                if(next is not null && Eligible(next))
                {
                    if(!Covered(next)||!IdentityMatches(next,request.OrganizationId,request.CounterpartyId,request.CurrencyCode,request.PaymentOperation,request.RequestNumber)
                        ||(request.PaymentOperation is null || request.PaymentOperation.IsPayrollAdvance&&next.RequestNumber is null) && next.PaymentDay!=treasuryDay)
                    { valid=false;break; }
                }
                else if(representedByKey.TryGetValue(link.DocumentKey,out var remaining)&&remaining.Length>0)
                { valid=false;break; }
                changes.Add((link,next is not null && Eligible(next)?next:null));
            }
            if(!valid){warnings.Add($"{request.RequestNumber}: исправление 1С ещё не согласовано с фактом ДДС или реквизитами заявки.");continue;}
            var total=changes.Sum(c=>c.New?.Amount??0m);
            if(total>request.Lines.Sum(l=>l.Amount))
            { warnings.Add($"{request.RequestNumber}: исправленная оплата превышает обязательство; требуется разбор.");continue; }
            var currency=currencies.Single(c=>c.Code==request.CurrencyCode);
            foreach(var change in changes)
            {
                var now=clock.UtcNow;var old=change.Old;
                if(old.ReversedAtUtc is not null)continue;
                // The approved-evidence refresh above clears EF tracking. The
                // legacy history remains protected by this transaction's SQL
                // locks, but its detached reversal must be enlisted again;
                // otherwise SaveChanges silently loses it and a replacement
                // collides with the active-document uniqueness constraint.
                EnlistReversalLink407(db,old);
                var original=(await runtime.ReadLedgerAsync(request.Id,currency.Id,ct)).Single(e=>e.EventId==old.EventId);
                var revId=Identity(old.DocumentKey,old.Revision,"reversal");
                await runtime.StorePaymentFactAsync(original with {EventId=revId,ExternalId=External(old.DocumentKey,old.Revision,"R"),
                    IsReversal=true,ReversedEventId=old.EventId},now,ct);
                old.ReversedAtUtc=now;old.ReversedByBatchId=capture.Id;
                await db.SaveChangesAsync(ct);
                reversed++;
            }
            foreach(var change in changes.Where(c=>c.New is not null).OrderBy(c=>c.Old.DocumentKey,StringComparer.Ordinal))
            {
                await AddAsync(request,change.New??throw new InvalidOperationException("Source revision missing."),legs[change.Old.DocumentKey],capture.Id,currency.Id,runtime,ct);applied++;
            }
            await ProjectAsync(request,currency.Id,runtime,capture.Id,fact.Id,ct);
        }
        db.ChangeTracker.Clear();
        // Never reuse a previously attributed document for a different request,
        // including after unposting. Reposting is a known-identity review.
        var historicKeys=(await db.AccountingPaymentLinks.Select(l=>l.DocumentKey).Distinct().ToArrayAsync(ct)).ToHashSet(StringComparer.Ordinal);
        historicKeys.UnionWith(approvedHistory400.Select(g=>g.DocumentKey));
        var payable=await db.ExpenseRequests.AsNoTracking().Include(r=>r.Lines)
            .Where(r=>r.Status==ExpenseRequestStatus.ToPay && r.OrganizationId!=null && r.CounterpartyId!=null && r.PaymentOperation==null)
            .Take(10001).ToArrayAsync(ct);
        if(payable.Length>10000) throw new InvalidOperationException("Bounded payment queue exceeded.");
        var candidateRequests=new List<PaymentRequestCandidate>();
        var payableIds=payable.Select(r=>r.Id).ToArray();
        var settledIds=new HashSet<Guid>();
        foreach(var batch in payableIds.Chunk(200))
            settledIds.UnionWith(await db.ExpenseRequestPaymentFacts.AsNoTracking()
                .Where(p=>batch.Contains(p.ExpenseRequestId)).Select(p=>p.ExpenseRequestId).Distinct().ToArrayAsync(ct));
        var dates415=await ReadCandidateDates415Async(payable.Where(r=>!settledIds.Contains(r.Id)).Select(r=>r.Id).ToArray(),ct);
        foreach(var r in payable)
        {
            if(settledIds.Contains(r.Id)) continue;
            var date=dates415.GetValueOrDefault(r.Id);
            if(date is not null) candidateRequests.Add(new(r.Id,r.OrganizationId.GetValueOrDefault(),r.CounterpartyId.GetValueOrDefault(),r.CurrencyCode,date.Value,r.Lines.Sum(l=>l.Amount)));
        }
        // Ambiguity is evaluated against ALL eligible primary payments. DDS
        // lag must not make a duplicate disappear and create a false unique
        // match. Coherence is checked only AFTER that complete matching plan.
        var candidateDocs=capture.Documents.Where(d=>d.Eligible&&!d.PayrollAdvanceEligible&&d.RequestNumber is null&&!historicKeys.Contains(d.DocumentKey))
            .Select(Resolve).Where(d=>d is not null).Cast<AccountingPaymentCandidate>().ToArray();
        var plan=AccountingPaymentMatcher.Plan(candidateRequests,candidateDocs);
        foreach(var match in plan.Matches)
        {
            if(match.DocumentKeys.Any(key=>!Covered(documents[key])))
            {
                warnings.Add($"{payable.Single(r=>r.Id==match.RequestId).RequestNumber}: платёж найден в 1С, но его проводки не полностью подтверждены срезом ДДС. Проверьте обновление и исключения обмена; оплата не подтверждена.");
                continue;
            }
            var request=await requests.FindForWorkflowUpdateAsync(match.RequestId,ct)??throw new InvalidOperationException("Payment request missing.");
            var r=candidateRequests.Single(c=>c.RequestId==request.Id);
            if(request.Status!=ExpenseRequestStatus.ToPay||request.OrganizationId!=r.OrganizationId
                ||request.CounterpartyId!=r.CounterpartyId||request.CurrencyCode!=r.CurrencyCode
                ||request.Lines.Sum(l=>l.Amount)!=r.OutstandingAmount||await ReadDateAsync(request.Id,ct)!=r.TreasuryDay
                ||await db.ExpenseRequestPaymentFacts.AnyAsync(p=>p.ExpenseRequestId==request.Id,ct))
                throw new InvalidOperationException("Payment request changed during matching.");
            var currency=currencies.Single(c=>c.Code==request.CurrencyCode);
            foreach(var key in match.DocumentKeys)
            {
                await AddAsync(request,documents[key],legs[key],capture.Id,currency.Id,runtime,ct);applied++;
            }
            await ProjectAsync(request,currency.Id,runtime,capture.Id,fact.Id,ct);
            db.ChangeTracker.Clear();
        }
        foreach(var id in plan.AmbiguousRequestIds) warnings.Add($"{payable.Single(r=>r.Id==id).RequestNumber}: неоднозначное или неполное совпадение оплаты; автоматически не подтверждено.");
        db.ChangeTracker.Clear();
        var operations=await db.ExpenseRequests.AsNoTracking().Include(r=>r.Lines)
            .Where(r=>(r.Status==ExpenseRequestStatus.ToPay||r.Status==ExpenseRequestStatus.PartiallyPaid)
                &&r.OrganizationId!=null&&r.PaymentOperation!=null).Take(10001).ToArrayAsync(ct);
        if(operations.Length>10000)throw new InvalidOperationException("Bounded operation payment queue exceeded.");
        var referencedRequests=new List<ReferencedOperationRequest>();
        foreach(var r in operations)
        {
            var currency=currencies.Single(c=>c.Code==r.CurrencyCode);
            var ledger=PaymentFactLedgerReconciler.Reconcile(r.Id,currency.Id,await runtime.ReadLedgerAsync(r.Id,currency.Id,ct));
            // Do not combine an unidentified manual/external settlement with this source ledger.
            var linked=await db.AccountingPaymentLinks.Where(l=>l.ExpenseRequestId==r.Id&&l.ReversedAtUtc==null).SumAsync(l=>l.Amount,ct);
            if(ledger.ActiveAmount!=linked){warnings.Add($"{r.RequestNumber}: есть оплаты из другого источника; требуется сверка.");continue;}
            referencedRequests.Add(new(r.Id,r.RequestNumber??"",r.OrganizationId!.Value,r.CurrencyCode,r.Lines.Sum(l=>l.Amount)-ledger.ActiveAmount));
        }
        var referenceDocuments=capture.Documents.Where(d=>d.RequestNumber is not null&&!historicKeys.Contains(d.DocumentKey)).ToArray();
        var operationPlan=ReferencedOperationPaymentMatcher.Plan(referencedRequests,referenceDocuments.Select(d=>new ReferencedOperationPayment(
            d.DocumentKey,d.RequestNumber!,organizations.SingleOrDefault(o=>o.Code==d.OrganizationCode)?.Id??Guid.Empty,
            currencies.SingleOrDefault(c=>c.NumericCode==d.CurrencyNumericCode)?.Code??"",d.Amount,d.OperationReferenceEligible)).ToArray());
        foreach(var match in operationPlan.Matches)
        {
            if(match.DocumentKeys.Any(key=>!Covered(documents[key])))
            {warnings.Add($"{operations.Single(r=>r.Id==match.RequestId).RequestNumber}: проводки оплаты ещё не подтверждены срезом ДДС.");continue;}
            var request=await requests.FindForWorkflowUpdateAsync(match.RequestId,ct)??throw new InvalidOperationException("Operation request missing.");
            var prior=operations.Single(r=>r.Id==request.Id);
            if(request.ConcurrencyStamp!=prior.ConcurrencyStamp||request.PaymentOperation is null
                ||match.DocumentKeys.Any(key=>!IdentityMatches(documents[key],request.OrganizationId,request.CounterpartyId,request.CurrencyCode,request.PaymentOperation,request.RequestNumber)))
                throw new InvalidOperationException("Operation request changed during matching.");
            var currency=currencies.Single(c=>c.Code==request.CurrencyCode);
            foreach(var key in match.DocumentKeys.OrderBy(key=>documents[key].PaymentDay).ThenBy(key=>key,StringComparer.Ordinal))
            {await AddAsync(request,documents[key],legs[key],capture.Id,currency.Id,runtime,ct);applied++;}
            await ProjectAsync(request,currency.Id,runtime,capture.Id,fact.Id,ct);db.ChangeTracker.Clear();
        }
        var advanceRequests=new List<PayrollAdvanceRequest455>();
        var advanceDates=await ReadCandidateDates415Async(operations.Where(r=>r.PaymentOperation!.IsPayrollAdvance
            &&r.Status==ExpenseRequestStatus.ToPay&&r.CurrencyCode=="KZT").Select(r=>r.Id).ToArray(),ct);
        foreach(var r in operations.Where(r=>r.PaymentOperation!.IsPayrollAdvance&&r.Status==ExpenseRequestStatus.ToPay&&r.CurrencyCode=="KZT"
            &&!operationPlan.Matches.Any(m=>m.RequestId==r.Id)))
        {
            if(advanceDates.GetValueOrDefault(r.Id) is not{} date||await db.ExpenseRequestPaymentFacts.AnyAsync(p=>p.ExpenseRequestId==r.Id,ct))continue;
            advanceRequests.Add(new(r.Id,r.OrganizationId!.Value,date,r.Lines.Sum(l=>l.Amount)));
        }
        var advanceDocuments=capture.Documents.Where(d=>d.PayrollAdvanceEligible&&d.RequestNumber is null&&!historicKeys.Contains(d.DocumentKey))
            .Select(d=>new PayrollAdvanceDocument455(d.DocumentKey,organizations.Single(o=>o.Code==d.OrganizationCode).Id,d.PaymentDay,d.Amount)).ToArray();
        var advancePlan=PayrollAdvancePaymentMatcher455.Plan(advanceRequests,advanceDocuments);
        foreach(var match in advancePlan.Matches)
        {
            var d=documents[match.DocumentKeys.Single()];
            if(!Covered(d)){warnings.Add($"{operations.Single(r=>r.Id==match.RequestId).RequestNumber}: зарплатная проводка ещё не подтверждена срезом ДДС.");continue;}
            var request=await requests.FindForWorkflowUpdateAsync(match.RequestId,ct)??throw new InvalidOperationException("Payroll request missing.");
            var prior=operations.Single(r=>r.Id==request.Id);
            if(request.Status!=ExpenseRequestStatus.ToPay||request.ConcurrencyStamp!=prior.ConcurrencyStamp||request.PaymentOperation?.IsPayrollAdvance!=true
                ||request.CurrencyCode!="KZT"||!IdentityMatches(d,request.OrganizationId,request.CounterpartyId,request.CurrencyCode,request.PaymentOperation,request.RequestNumber)
                ||await ReadDateAsync(request.Id,ct)!=d.PaymentDay||request.Lines.Sum(l=>l.Amount)!=d.Amount
                ||await db.ExpenseRequestPaymentFacts.AnyAsync(p=>p.ExpenseRequestId==request.Id,ct))
                throw new InvalidOperationException("Payroll request changed during matching.");
            var currency=currencies.Single(c=>c.Code=="KZT");
            await AddAsync(request,d,legs[d.DocumentKey],capture.Id,currency.Id,runtime,ct);applied++;
            await ProjectAsync(request,currency.Id,runtime,capture.Id,fact.Id,ct);db.ChangeTracker.Clear();
        }
        foreach(var r in operations)
        {
            if(operationPlan.AmbiguousRequestIds.Contains(r.Id)) warnings.Add($"{r.RequestNumber}: номер BMS найден, но реквизиты, сумма или источник неоднозначны; оплата не подтверждена.");
            else if(advancePlan.AmbiguousRequestIds.Contains(r.Id))warnings.Add($"{r.RequestNumber}: несколько авансов или зарплатных платежей с одинаковой суммой и датой; автоматически не подтверждено.");
            else if(!operationPlan.Matches.Any(m=>m.RequestId==r.Id)&&!advancePlan.Matches.Any(m=>m.RequestId==r.Id)) warnings.Add(r.PaymentOperation!.IsPayrollAdvance
                ?$"{r.RequestNumber}: ожидается однозначная проведённая зарплатная оплата 1С по организации, казначейской дате и точной сумме; резерв сохраняется."
                :$"{r.RequestNumber}: ожидается проведённый платёж с точной ссылкой BMS:{r.RequestNumber}; резерв сохраняется.");
        }
        if(db.BudgetRegisterEnabled343)
            await new BudgetExecutionCash343(db,new AccountingPaymentCoverageReader(db,source),location).RefreshAsync(fact,ct);
        return new AccountingPaymentRun(capture.Id,fact.Id,applied,reversed,plan.AmbiguousRequestIds.Count+operationPlan.AmbiguousRequestIds.Union(advancePlan.AmbiguousRequestIds).Count(),warnings);
    },token);

    private static bool Eligible(AccountingSourcePayment d)=>d.Eligible||d.OperationReferenceEligible||d.PayrollAdvanceEligible;

    private async Task<DateOnly?> ReadDateAsync(Guid requestId,CancellationToken token)
    {
        var id=requestId.ToString("D");
        var json=await db.BusinessAuditEntries.AsNoTracking().Where(a=>a.ObjectType=="TreasuryPaymentDate"
            &&a.ObjectId==id&&a.Action=="TreasuryPaymentDateSet").Select(a=>a.AfterJson).Take(10001).ToArrayAsync(token);
        return ParseDateEvidence415(json);
    }

    internal static DateOnly? ParseDateEvidence415(IReadOnlyCollection<string> json)
    {
        if(json.Count is 0 or >10000) return null;
        var rows=json.Select(j=>JsonSerializer.Deserialize<DateEvidence>(j,Json)??throw new InvalidOperationException("Payment day evidence invalid.")).ToArray();
        if(rows.Select(r=>r.Sequence).Distinct().Count()!=rows.Length||rows.Max(r=>r.Sequence)!=rows.Length)
            throw new InvalidOperationException("Payment day sequence invalid.");
        var last=rows.MaxBy(r=>r.Sequence)??throw new InvalidOperationException("Payment date sequence missing.");
        return last.Status=="TO_PAY"&&last.Date!=default ? last.Date:null;
    }

    internal async Task<Dictionary<Guid,DateOnly?>> ReadCandidateDates415Async(Guid[] ids,CancellationToken token)
    {
        var result=new Dictionary<Guid,DateOnly?>();
        foreach(var batch in ids.Chunk(200))
        {
            var keys=batch.Select(id=>id.ToString("D")).ToArray();
            var query=db.BusinessAuditEntries.AsNoTracking().Where(a=>a.ObjectType=="TreasuryPaymentDate"
                &&a.Action=="TreasuryPaymentDateSet"&&keys.Contains(a.ObjectId));
            var counts=await query.GroupBy(a=>a.ObjectId).Select(g=>new{Id=g.Key,Count=g.Count()}).ToArrayAsync(token);
            var valid=counts.Where(g=>g.Count<=10000).Select(g=>g.Id).ToArray();
            // Same per-request ceiling and sequence validation as ReadDateAsync;
            // stream a stable group at a time instead of retaining the backlog.
            Guid current=Guid.Empty;var evidence=new List<string>();
            await foreach(var row in query.Where(a=>valid.Contains(a.ObjectId)).OrderBy(a=>a.ObjectId)
                .ThenBy(a=>a.OccurredAtUtc).ThenBy(a=>a.Id).Select(a=>new{a.ObjectId,a.AfterJson}).AsAsyncEnumerable().WithCancellation(token))
            {
                var id=Guid.ParseExact(row.ObjectId,"D");
                if(id!=current)
                {
                    if(current!=Guid.Empty)result[current]=ParseDateEvidence415(evidence);
                    current=id;evidence.Clear();
                }
                evidence.Add(row.AfterJson);
                if(evidence.Count>10000)throw new InvalidOperationException("Payment date evidence changed during matching.");
            }
            if(current!=Guid.Empty)result[current]=ParseDateEvidence415(evidence);
        }
        return result;
    }

    private async Task AddAsync(ExpenseRequest request,AccountingSourcePayment document,AccountingSourceCashLeg[] legs,
        Guid batch,Guid currencyId,EfCoreConfirmedPaymentApplicationRuntime runtime,CancellationToken token)
    {
        var previous=PaymentFactLedgerReconciler.Reconcile(request.Id,currencyId,await runtime.ReadLedgerAsync(request.Id,currencyId,token));
        var requestLines=request.Lines;
        var snapshots=requestLines.Select(l=>new PaymentSettlementLineSnapshot(l.Id,l.SortOrder,l.Amount,l.AmountKzt??l.Amount)).ToArray();
        var before=PaymentSettlementProgressCalculator.Calculate(request.CurrencyCode,snapshots,previous.ActiveAmount,previous.ActiveAmountKzt);
        var after=PaymentSettlementProgressCalculator.Calculate(request.CurrencyCode,snapshots,
            previous.ActiveAmount+document.Amount,previous.ActiveAmountKzt+document.AmountKzt);
        var beforeById=before.Lines.ToDictionary(l=>l.LineId);
        var originalById=requestLines.ToDictionary(l=>l.Id);
        var revision=1+(await db.AccountingPaymentLinks.Where(l=>l.DocumentKey==document.DocumentKey).Select(l=>(int?)l.Revision).MaxAsync(token)??0);
        var now=clock.UtcNow;var eventId=Identity(document.DocumentKey,revision,"payment");
        var paidAt=new DateTimeOffset(DateTime.Parse(legs.Max(l=>l.EntryDateLocal)
            ??throw new InvalidOperationException("Source cash legs missing."),CultureInfo.InvariantCulture),TimeSpan.FromHours(5)).ToUniversalTime();
        await runtime.StorePaymentFactAsync(new("1C_ACCOUNTING_258",External(document.DocumentKey,revision,"P"),eventId,
            request.Id,currencyId,document.Amount,document.AmountKzt,paidAt,document.DocumentNumber,false,null),now,token);
        var link=new AccountingPaymentLink{Id=Guid.NewGuid(),DocumentKey=document.DocumentKey,FinancialHash=document.FinancialHash,
            Revision=revision,SourceBatchId=batch,ExpenseRequestId=request.Id,EventId=eventId,CurrencyCode=request.CurrencyCode,
            Amount=document.Amount,AmountKzt=document.AmountKzt,PaymentDay=document.PaymentDay,SourceLegsJson=JsonSerializer.Serialize(legs,Json),MatchedAtUtc=now};
        db.AccountingPaymentLinks.Add(link);
        foreach(var line in after.Lines)
        {
            token.ThrowIfCancellationRequested();
            var delta=line.ReleasedReserveKzt-beforeById[line.LineId].ReleasedReserveKzt;
            if(delta<=0m)continue;
            var original=originalById[line.LineId];
            db.AccountingPaymentLinkLines.Add(new(){LinkId=link.Id,ExpenseRequestLineId=line.LineId,
                CostCenterId=Bms.Domain.Budgeting.ExpenseBudgetDimensions372.CostCenter(request,original),
                CashFlowItemId=original.CashFlowItemId??throw new InvalidOperationException("Payment article missing."),
                BudgetMonth=request.EffectiveBudgetMonth??throw new InvalidOperationException("Payment budget month missing."),OriginalReservePaidKzt=delta});
        }
        await db.SaveChangesAsync(token);
    }

    private async Task ProjectAsync(ExpenseRequest request,Guid currencyId,EfCoreConfirmedPaymentApplicationRuntime runtime,
        Guid batch,Guid ddsId,CancellationToken token)
    {
        var ledger=PaymentFactLedgerReconciler.Reconcile(request.Id,currencyId,await runtime.ReadLedgerAsync(request.Id,currencyId,token));
        var settlement=PaymentSettlementProgressCalculator.Calculate(request.CurrencyCode,
            request.Lines.Select(l=>new PaymentSettlementLineSnapshot(l.Id,l.SortOrder,l.Amount,l.AmountKzt??l.Amount)),ledger.ActiveAmount,ledger.ActiveAmountKzt);
        await runtime.ApplyAccountingSettlementAsync(request.Id,settlement,ServiceActor,clock.UtcNow,token);
        // An FX-only correction can leave original reserve/status unchanged,
        // but the optimistic version must still expose changed payment facts.
        var now=clock.UtcNow;
        await db.ExpenseRequests.Where(r=>r.Id==request.Id&&r.ConcurrencyStamp==request.ConcurrencyStamp)
            .ExecuteUpdateAsync(s=>s.SetProperty(r=>r.ConcurrencyStamp,Guid.NewGuid())
                .SetProperty(r=>r.UpdatedAtUtc,now),token);
        var payload=JsonSerializer.Serialize(new{expenseRequestId=request.Id,sourceCaptureId=batch,ddsCaptureId=ddsId,settlement},Json);
        var correlation=Guid.NewGuid().ToString("N");
        audit.Append(Guid.NewGuid(),clock.UtcNow,ServiceActor,"ExpenseRequest",request.Id.ToString("D"),"PaymentApplied",null,payload,correlation);
        outbox.Enqueue(Guid.NewGuid(),clock.UtcNow,"expense-request.payment-applied.v1",payload,correlation);
        await db.SaveChangesAsync(token);
    }

    internal static bool SameLegs(IReadOnlyCollection<AccountingSourceCashLeg> expected,IReadOnlyCollection<AccountingSourceCashLeg> found)
    {
        // These are NOT an amount-selected subset. The primary 1C query pins
        // exact line identities in the document currency. A separate KZT fee
        // in the same foreign-currency document must remain in DDS cash and
        // must not hide or invalidate the independently proven payment lines.
        var rows=found.ToDictionary(l=>l.LineNo);
        return expected.Count>0&&expected.All(l=>rows.TryGetValue(l.LineNo,out var value)&&value==l);
    }
    private static bool ValidBin(string? value)=>value?.Trim() is {Length:12} s&&s.All(c=>c is >= '0' and <= '9');
    private static Guid Identity(string key,int revision,string kind)=>new(SHA256.HashData(Encoding.UTF8.GetBytes($"BMS258|{key}|{revision}|{kind}")).AsSpan(0,16));
    private static string External(string key,int revision,string kind)=>$"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))}:{revision}:{kind}";
}
