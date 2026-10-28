using Bms.Domain.Common;
using System.Text.Json.Serialization;

namespace Bms.Domain.ExpenseRequests;

// No gross amounts, DDS article, budget CFO, organisation, counterparty,
// payment date, currency, basis type or new-object flags in this contract.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExpenseRequestApprovalAccrualLine(Guid LineId, Guid AccrualCostCenterId,
    DateOnly AccrualPeriod, ExpenseVatRate VatRateCode);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExpenseRequestApprovalDetails(
    string? Purpose, string? Comment, string? BusinessJustification,
    bool IsUrgent, string? UrgencyReason, Guid? ContractId,
    string? InvoiceNumber, DateOnly? InvoiceDate,
    string? PublicOfferName, string? PublicOfferIdentifier, string? OtherBasisExplanation,
    string? CandidateContractNumber, bool CandidateContractNumberMissing,
    DateOnly? CandidateContractDate, string? CandidateContractSubject, string? CandidateContractTerms,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ExpenseRequestApprovalAccrualLine>? AccrualLines = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ExpenseRequestApprovalAllocationLine>? AllocationLines = null)
{
    public static ExpenseRequestApprovalDetails From(ExpenseRequest r) => new(
        r.Purpose,r.Comment,r.BusinessJustification,r.IsUrgent,r.UrgencyReason,r.ContractId,
        r.InvoiceNumber,r.InvoiceDate,r.PublicOfferName,r.PublicOfferIdentifier,r.OtherBasisExplanation,
        r.CandidateContractNumber,r.CandidateContractNumberMissing,r.CandidateContractDate,
        r.CandidateContractSubject,r.CandidateContractTerms,
        r.Lines.Where(l => l.AccrualPeriod.HasValue && l.VatRateCode.HasValue).Select(l => new ExpenseRequestApprovalAccrualLine(
            l.Id, (l.AccrualCostCenterId ?? l.CostCenterId).GetValueOrDefault(),
            l.AccrualPeriod.GetValueOrDefault(), l.VatRateCode.GetValueOrDefault())).ToArray());
}

public sealed partial class ExpenseRequest
{
    public void UpdateApprovalDetails(ExpenseRequestApprovalDetails d, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(d);
        if(Status is not (ExpenseRequestStatus.InApproval or ExpenseRequestStatus.Approved))
            throw new DomainRuleViolationException("Details can be edited only before treasury handoff.");
        if(now.Offset!=TimeSpan.Zero || now<UpdatedAtUtc)
            throw new DomainRuleViolationException("Expense-request edit time must be monotonic UTC.");
        if((IsNewContract || PaymentBasisCode != PaymentBasisType.Contract) && d.ContractId != ContractId)
            throw new DomainRuleViolationException("A new contract link and payment basis cannot change here.");
        if(!IsNewContract && (d.CandidateContractNumber != CandidateContractNumber
            || d.CandidateContractNumberMissing != CandidateContractNumberMissing
            || d.CandidateContractDate != CandidateContractDate || d.CandidateContractSubject != CandidateContractSubject
            || d.CandidateContractTerms != CandidateContractTerms))
            throw new DomainRuleViolationException("Candidate contract details require the existing new-contract flag.");
        // Normalize the complete evidence first; retain all non-editable
        // candidate identities, missing-file explanations and NSI decisions.
        var evidence=NormalizeEvidence(new ExpenseRequestEvidenceDraft(
            PaymentBasisCode,d.InvoiceNumber,d.InvoiceDate,d.PublicOfferName,d.PublicOfferIdentifier,d.OtherBasisExplanation,
            SupportingDocumentMissing,SupportingDocumentMissingExplanation,CandidateCounterpartyName,CandidateResidencyCode,
            CandidateBinIin,CandidateCountry,CandidateForeignRegistrationNumber,CandidateRegistrationNumberMissing,
            CandidateRegistrationNumberMissingExplanation,d.CandidateContractNumber,d.CandidateContractNumberMissing,
            d.CandidateContractDate,d.CandidateContractSubject,d.CandidateContractTerms,
            NewContractDocumentMissing,NewContractDocumentMissingExplanation,PaymentOperation),IsNewCounterparty,IsNewContract);
        var purpose=NormalizeRequiredText(d.Purpose,1000,nameof(Purpose));
        var comment=NormalizeOptionalText(d.Comment,1000,nameof(Comment));
        var justification=NormalizeOptionalText(d.BusinessJustification,1000,nameof(BusinessJustification));
        var urgency=d.IsUrgent?NormalizeRequiredText(d.UrgencyReason,1000,nameof(UrgencyReason)):null;
        var contract=NormalizeOptionalReference(d.ContractId);
        if(d.AccrualLines is not null && d.AllocationLines is not null)
            throw new DomainRuleViolationException("Use only one accrual editing contract.");
        var allocations = d.AllocationLines is null ? null : PrepareApprovalAllocations(d.AllocationLines);
        var accrualChanges = PrepareApprovalAccrualChanges(d.AccrualLines);
        Purpose=purpose;Comment=comment;BusinessJustification=justification;
        IsUrgent=d.IsUrgent;UrgencyReason=urgency;ContractId=contract;ApplyEvidence(evidence);
        foreach(var c in accrualChanges)
            c.Line.UpdateApprovalAccrual(c.Edit.AccrualCostCenterId,c.Edit.AccrualPeriod,c.Edit.VatRateCode,c.VatKzt,c.NetKzt);
        if(allocations is not null)
        {
            foreach(var c in allocations)
            {
                if(c.Existing is null){_lines.Add(c.Prepared);continue;}
                if(c.Existing.Amount==c.Prepared.Amount && c.Existing.AmountKzt==c.Prepared.AmountKzt
                    && (c.Existing.AccrualCostCenterId??c.Existing.CostCenterId)==c.Prepared.AccrualCostCenterId
                    && c.Existing.AccrualPeriod==c.Prepared.AccrualPeriod && c.Existing.VatRateCode==c.Prepared.VatRateCode)continue;
                c.Existing.Update(c.Prepared.CashFlowItemId,c.Prepared.CostCenterId,c.Prepared.AccrualPeriod,
                    c.Prepared.Amount,c.Prepared.VatRateCode,c.Prepared.AccrualCostCenterId);
                c.Existing.ApplyKztCalculation(c.Prepared.AmountKzt!.Value,c.Prepared.VatAmountKzt,c.Prepared.AmountExcludingVatKzt);
            }
        }
        UpdatedAtUtc=now;ConcurrencyStamp=Guid.NewGuid();
        // Gross financial values, budget CFO/article, accrual article snapshots, reserve,
        // route snapshot/stage, initiator and status are intentionally untouched.
    }

    private IReadOnlyList<(ExpenseRequestLine Line,ExpenseRequestApprovalAccrualLine Edit,decimal VatKzt,decimal NetKzt)>
        PrepareApprovalAccrualChanges(IReadOnlyList<ExpenseRequestApprovalAccrualLine>? edits)
    {
        if(edits is null)return [];
        var eligible=Lines.Where(l=>l.AccrualPeriod.HasValue&&l.VatRateCode.HasValue).ToDictionary(l=>l.Id);
        if(edits.Count!=eligible.Count || edits.Any(e=>e is null) || edits.Select(e=>e.LineId).Distinct().Count()!=edits.Count
            || FullyPaidAtUtc is not null || AccrualCycleId is not null)
            throw new DomainRuleViolationException("Invalid accrual edits or payment already started.");
        var result=new List<(ExpenseRequestLine,ExpenseRequestApprovalAccrualLine,decimal,decimal)>();
        foreach(var e in edits)
        {
            if (PaymentOperation is not null && e.VatRateCode != ExpenseVatRate.WithoutVat)
                throw new DomainRuleViolationException("VAT is not applicable to this payment operation.");
            if(!eligible.TryGetValue(e.LineId,out var line) || e.AccrualCostCenterId==Guid.Empty
                || e.AccrualPeriod.Year is <1900 or >9998 || e.AccrualPeriod.Day!=1 || !Enum.IsDefined(e.VatRateCode)
                || (line.AccrualSnapshotFormsManagerialAccrual==true && line.AccrualSnapshotExpenseItemId is null) || line.AmountKzt is null
                || line.ConfirmedClosedAmount!=0 || line.ConfirmedClosedAmountKzt!=0)
                throw new DomainRuleViolationException("Invalid accrual line details.");
            // A comment-only save must not materialize a nullable legacy department
            // or rewrite VAT components. The frozen accrual flag/article never change.
            if((line.AccrualCostCenterId??line.CostCenterId)==e.AccrualCostCenterId
                &&line.AccrualPeriod==e.AccrualPeriod&&line.VatRateCode==e.VatRateCode)continue;
            var vat=e.VatRateCode==ExpenseVatRate.WithoutVat?0m:decimal.Round(line.Amount*16m/116m,2,MidpointRounding.AwayFromZero);
            var net=line.Amount-vat;
            if(CurrencyCode!="KZT" && (CurrencyRate is null or <=0 || CurrencyMultiplicity is null or <=0))
                throw new DomainRuleViolationException("The frozen currency snapshot is required.");
            // A split line owns an exact share of the frozen KZT gross, which
            // can differ by a tiyn from converting its native gross again.
            // Department/period edits must preserve its monetary components;
            // a VAT change is allocated within that same frozen gross pair.
            var netKzt=e.VatRateCode==line.VatRateCode
                ? line.AmountExcludingVatKzt??throw new DomainRuleViolationException("The frozen KZT VAT components are required.")
                : PairedMoneyAllocationPolicy.Allocate(line.AmountKzt.Value,net,line.Amount);
            var vatKzt=line.AmountKzt.Value-netKzt;
            MoneyAmountPolicy.EnsureNonNegative(netKzt,"Net KZT amount");MoneyAmountPolicy.EnsureNonNegative(vatKzt,"VAT KZT amount");
            result.Add((line,e,vatKzt,netKzt));
        }
        return result;
    }
}
