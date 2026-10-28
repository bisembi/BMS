using Bms.Domain.Common;
using System.Text.Json.Serialization;

namespace Bms.Domain.ExpenseRequests;

internal sealed record ExpenseRequestLineAccrualSnapshotAssignment(
    Guid LineId,
    bool FormsManagerialAccrual,
    Guid? ExpenseItemId);

public sealed partial class ExpenseRequest
{
    private readonly List<ExpenseRequestLine> _lines = [];

    private ExpenseRequest()
    {
    }

    private ExpenseRequest(
        Guid id,
        string initiatorExternalId,
        DateTimeOffset createdAtUtc,
        Guid? organizationId,
        Guid? defaultCostCenterId,
        DateOnly? plannedPaymentDate,
        string currencyCode,
        Guid? counterpartyId,
        Guid? contractId,
        string? purpose,
        string? comment,
        string? businessJustification,
        bool isUrgent,
        string? urgencyReason,
        bool isNewCounterparty,
        bool isNewContract,
        string? requestNumber,
        ExpenseRequestEvidenceDraft? evidence)
    {
        if (id == Guid.Empty)
        {
            throw new DomainRuleViolationException("Expense request id is required.");
        }

        if (string.IsNullOrWhiteSpace(initiatorExternalId)
            || initiatorExternalId.Length > 256
            || !string.Equals(
                initiatorExternalId,
                initiatorExternalId.Trim(),
                StringComparison.Ordinal)
            || initiatorExternalId.Any(char.IsControl))
        {
            throw new DomainRuleViolationException("Initiator identifier is not canonical.");
        }

        var normalizedCore = NormalizeCoreReferencesAndEvidence(
            organizationId,
            defaultCostCenterId,
            counterpartyId,
            contractId,
            isNewCounterparty,
            isNewContract,
            evidence);

        Id = id;
        RequestNumber = requestNumber is null
            ? null
            : NormalizeRequestNumber(requestNumber);
        InitiatorExternalId = initiatorExternalId;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        OrganizationId = normalizedCore.OrganizationId;
        DefaultCostCenterId = normalizedCore.DefaultCostCenterId;
        PlannedPaymentDate = plannedPaymentDate;
        CurrencyCode = NormalizeCurrency(currencyCode);
        CounterpartyId = normalizedCore.CounterpartyId;
        ContractId = normalizedCore.ContractId;
        Purpose = NormalizeOptionalText(purpose, 1_000, nameof(purpose));
        Comment = NormalizeOptionalText(comment, 1_000, nameof(comment));
        BusinessJustification = NormalizeOptionalText(
            businessJustification,
            1_000,
            nameof(businessJustification));
        IsUrgent = isUrgent;
        UrgencyReason = isUrgent
            ? NormalizeRequiredText(urgencyReason, 1_000, nameof(urgencyReason))
            : null;
        IsNewCounterparty = normalizedCore.IsNewCounterparty;
        IsNewContract = normalizedCore.IsNewContract;
        ApplyEvidence(normalizedCore.Evidence);
        Status = ExpenseRequestStatus.Draft;
        ConcurrencyStamp = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    /// <summary>Server-assigned immutable business number. Null is retained only
    /// for legacy rows created before SQL038.</summary>
    public string? RequestNumber { get; private set; }

    public ExpenseRequestStatus Status { get; private set; }

    /// <summary>
    /// The immutable approval-route snapshot currently governing an in-approval
    /// request. It is null for a draft and every terminal/non-route status.
    /// Route history itself remains in the snapshot tables.
    /// </summary>
    [JsonIgnore]
    public int? ActiveApprovalRouteSnapshotNumber { get; private set; }

    /// <summary>
    /// Current one-based stage of <see cref="ActiveApprovalRouteSnapshotNumber"/>.
    /// Zero is used only when no route is active.
    /// </summary>
    [JsonIgnore]
    public int ActiveApprovalRouteStageNumber { get; private set; }

    public string InitiatorExternalId { get; private set; } = string.Empty;

    public Guid? OrganizationId { get; private set; }

    public Guid? DefaultCostCenterId { get; private set; }

    public DateOnly? PlannedPaymentDate { get; private set; }

    /// <summary>Separate from the initiator's indicative date. Only a locked
    /// treasury reschedule may set this; copying a request does not copy it.</summary>
    public DateOnly? TreasuryBudgetMonth { get; private set; }

    public DateOnly? EffectiveBudgetMonth => TreasuryBudgetMonth
        ?? (PlannedPaymentDate is { } day ? new DateOnly(day.Year, day.Month, 1) : null);

    public void SetTreasuryBudgetMonth(DateOnly paymentDay)
    {
        if (paymentDay == default || Status is not (ExpenseRequestStatus.Approved or ExpenseRequestStatus.ToPay))
            throw new DomainRuleViolationException("Treasury budget month can change only before confirmed settlement.");
        TreasuryBudgetMonth = new(paymentDay.Year, paymentDay.Month, 1);
    }

    public string CurrencyCode { get; private set; } = "KZT";

    [JsonIgnore]
    public DateOnly? CurrencyRateDate { get; private set; }

    [JsonIgnore]
    public decimal? CurrencyRate { get; private set; }

    [JsonIgnore]
    public decimal? CurrencyMultiplicity { get; private set; }

    public Guid? CounterpartyId { get; private set; }

    public Guid? ContractId { get; private set; }

    public string? Purpose { get; private set; }

    public string? Comment { get; private set; }

    public string? BusinessJustification { get; private set; }

    public bool IsUrgent { get; private set; }

    public string? UrgencyReason { get; private set; }

    public bool IsNewCounterparty { get; private set; }

    public bool IsNewContract { get; private set; }

    public PaymentBasisType? PaymentBasisCode { get; private set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExpensePaymentOperation? PaymentOperation { get; private set; }

    public string? InvoiceNumber { get; private set; }

    public DateOnly? InvoiceDate { get; private set; }

    public string? PublicOfferName { get; private set; }

    public string? PublicOfferIdentifier { get; private set; }

    public string? OtherBasisExplanation { get; private set; }

    public bool SupportingDocumentMissing { get; private set; }

    public string? SupportingDocumentMissingExplanation { get; private set; }

    public string? CandidateCounterpartyName { get; private set; }

    public CounterpartyResidency? CandidateResidencyCode { get; private set; }

    public string? CandidateBinIin { get; private set; }

    public string? CandidateCountry { get; private set; }

    public string? CandidateForeignRegistrationNumber { get; private set; }

    public bool CandidateRegistrationNumberMissing { get; private set; }

    public string? CandidateRegistrationNumberMissingExplanation { get; private set; }

    public string? CandidateContractNumber { get; private set; }

    public bool CandidateContractNumberMissing { get; private set; }

    public DateOnly? CandidateContractDate { get; private set; }

    public string? CandidateContractSubject { get; private set; }

    public string? CandidateContractTerms { get; private set; }

    public bool NewContractDocumentMissing { get; private set; }

    public string? NewContractDocumentMissingExplanation { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid ConcurrencyStamp { get; private set; }

    /// <summary>
    /// Server-owned marker that at least one partial document-closing command
    /// has been applied in the current accrual cycle.
    /// </summary>
    [JsonIgnore]
    public bool ClosedPartially { get; private set; }

    /// <summary>
    /// Current or most recently closed accrual cycle. It changes only when a
    /// closed request is explicitly reopened.
    /// </summary>
    [JsonIgnore]
    public Guid? AccrualCycleId { get; private set; }

    [JsonIgnore]
    public DateOnly? LatestClosingDocumentsReceivedDate { get; private set; }

    /// <summary>
    /// Cumulative accountant-entered document control amount for the current
    /// cycle. It may exceed the qualified accrual balance; line allocations may not.
    /// </summary>
    [JsonIgnore]
    public decimal ConfirmedClosingControlAmount { get; private set; }

    /// <summary>The first factual full-payment time, preserved across reopen.</summary>
    [JsonIgnore]
    public DateTimeOffset? FullyPaidAtUtc { get; private set; }

    [JsonIgnore]
    public Guid? CopiedFromExpenseRequestId { get; private set; }

    public IReadOnlyCollection<ExpenseRequestLine> Lines => _lines
        .OrderBy(line => line.SortOrder)
        .ToArray();

    public decimal TotalAmount => _lines.Sum(line => line.Amount);

    public decimal? TotalAmountKzt => _lines.Count == 0
        ? string.Equals(CurrencyCode, "KZT", StringComparison.Ordinal) ? 0m : null
        : _lines.All(line => line.AmountKzt.HasValue)
            ? _lines.Sum(line => line.AmountKzt!.Value)
            : null;

    public static ExpenseRequest CreateDraft(
        Guid id,
        string initiatorExternalId,
        DateTimeOffset createdAtUtc,
        Guid? organizationId,
        Guid? defaultCostCenterId,
        DateOnly? plannedPaymentDate,
        string? currencyCode,
        Guid? counterpartyId,
        Guid? contractId,
        string? purpose,
        string? comment,
        string? businessJustification,
        bool isUrgent,
        string? urgencyReason,
        bool isNewCounterparty,
        bool isNewContract,
        IEnumerable<ExpenseRequestLineDraft>? lines,
        ExpenseRequestEvidenceDraft? evidence = null,
        string? requestNumber = null)
    {
        var request = new ExpenseRequest(
            id,
            initiatorExternalId,
            createdAtUtc,
            organizationId,
            defaultCostCenterId,
            plannedPaymentDate,
            string.IsNullOrWhiteSpace(currencyCode) ? "KZT" : currencyCode,
            counterpartyId,
            contractId,
            purpose,
            comment,
            businessJustification,
            isUrgent,
            urgencyReason,
            isNewCounterparty,
            isNewContract,
            requestNumber,
            evidence);

        var sortOrder = 0;
        foreach (var line in lines ?? [])
        {
            if (sortOrder >= ExpenseRequestAllocationPolicy.MaximumLines)
                throw new DomainRuleViolationException("Expense request line limit exceeded.");
            var assignedSortOrder = TakeNextSortOrder(ref sortOrder);
            request._lines.Add(new ExpenseRequestLine(
                Guid.NewGuid(),
                id,
                line.CashFlowItemId,
                line.CostCenterId ?? request.DefaultCostCenterId,
                line.AccrualPeriod,
                line.Amount,
                assignedSortOrder,
                line.VatRateCode, line.AccrualCostCenterId));
        }

        request.ResetPreliminaryCurrencyCalculation();

        return request;
    }

    public static ExpenseRequest CopyDraft(
        Guid id,
        string initiatorExternalId,
        DateTimeOffset createdAtUtc,
        ExpenseRequest source,
        string? requestNumber = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Id == Guid.Empty)
        {
            throw new DomainRuleViolationException("Copy source id is required.");
        }

        if (id == source.Id)
        {
            throw new DomainRuleViolationException("A copied expense request requires a new id.");
        }

        var copy = CreateDraft(
            id,
            initiatorExternalId,
            createdAtUtc,
            source.OrganizationId,
            source.DefaultCostCenterId,
            plannedPaymentDate: null,
            source.CurrencyCode,
            source.IsNewCounterparty ? null : source.CounterpartyId,
            contractId: null,
            purpose: null,
            comment: null,
            businessJustification: null,
            isUrgent: false,
            urgencyReason: null,
            isNewCounterparty: false,
            isNewContract: false,
            source.Lines.Select(line => new ExpenseRequestLineDraft(
                line.CashFlowItemId,
                line.CostCenterId,
                line.AccrualPeriod,
                line.Amount,
                Id: null,
                    line.VatRateCode, line.AccrualCostCenterId)),
            new ExpenseRequestEvidenceDraft(PaymentBasisCode: source.PaymentBasisCode, PaymentOperation: source.PaymentOperation),
            requestNumber);
        copy.CopiedFromExpenseRequestId = source.Id;
        return copy;
    }

    /// <summary>New document identity with the source's editable business fields.
    /// Workflow, payments, closing evidence and attached files are not cloned.</summary>
    public static ExpenseRequest CopyCompleteDraft(Guid id, string initiatorExternalId,
        DateTimeOffset createdAtUtc, ExpenseRequest source, DateOnly plannedPaymentDate, string? requestNumber = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (id == source.Id || source.Id == Guid.Empty || plannedPaymentDate == default)
            throw new DomainRuleViolationException("Invalid complete-copy identity or date.");
        var copy = CreateDraft(id, initiatorExternalId, createdAtUtc,
            source.OrganizationId, source.DefaultCostCenterId, plannedPaymentDate, source.CurrencyCode,
            source.CounterpartyId, source.ContractId, source.Purpose, source.Comment, source.BusinessJustification,
            source.IsUrgent, source.UrgencyReason, source.IsNewCounterparty, source.IsNewContract,
            source.Lines.Select(l => new ExpenseRequestLineDraft(l.CashFlowItemId, l.CostCenterId,
                l.AccrualPeriod, l.Amount, Id: null, l.VatRateCode, l.AccrualCostCenterId)),
            new ExpenseRequestEvidenceDraft(source.PaymentBasisCode, source.InvoiceNumber, source.InvoiceDate,
                source.PublicOfferName, source.PublicOfferIdentifier, source.OtherBasisExplanation,
                source.SupportingDocumentMissing, source.SupportingDocumentMissingExplanation,
                source.CandidateCounterpartyName, source.CandidateResidencyCode, source.CandidateBinIin,
                source.CandidateCountry, source.CandidateForeignRegistrationNumber, source.CandidateRegistrationNumberMissing,
                source.CandidateRegistrationNumberMissingExplanation, source.CandidateContractNumber,
                source.CandidateContractNumberMissing, source.CandidateContractDate, source.CandidateContractSubject,
                source.CandidateContractTerms, source.NewContractDocumentMissing, source.NewContractDocumentMissingExplanation, source.PaymentOperation),
            requestNumber);
        copy.CopiedFromExpenseRequestId = source.Id;
        return copy;
    }

    public void UpdateDraft(
        DateTimeOffset updatedAtUtc,
        Guid? organizationId,
        Guid? defaultCostCenterId,
        DateOnly? plannedPaymentDate,
        string? currencyCode,
        Guid? counterpartyId,
        Guid? contractId,
        string? purpose,
        string? comment,
        string? businessJustification,
        bool isUrgent,
        string? urgencyReason,
        bool isNewCounterparty,
        bool isNewContract,
        IEnumerable<ExpenseRequestLineDraft>? lines,
        ExpenseRequestEvidenceDraft? evidence = null)
    {
        if (!Status.IsEditableDraft())
        {
            throw new DomainRuleViolationException("Only a draft or returned expense request can be updated.");
        }

        var normalizedCurrency = NormalizeCurrency(
            string.IsNullOrWhiteSpace(currencyCode) ? "KZT" : currencyCode);
        var normalizedPurpose = NormalizeOptionalText(purpose, 1_000, nameof(purpose));
        var normalizedComment = NormalizeOptionalText(comment, 1_000, nameof(comment));
        var normalizedBusinessJustification = NormalizeOptionalText(
            businessJustification,
            1_000,
            nameof(businessJustification));
        var normalizedUrgencyReason = isUrgent
            ? NormalizeRequiredText(urgencyReason, 1_000, nameof(urgencyReason))
            : null;
        var normalizedCore = NormalizeCoreReferencesAndEvidence(
            organizationId,
            defaultCostCenterId,
            counterpartyId,
            contractId,
            isNewCounterparty,
            isNewContract,
            evidence);

        var existingLines = _lines.ToDictionary(line => line.Id);
        var currentMaximumSortOrder = existingLines.Count == 0
            ? 0
            : existingLines.Values.Max(line => line.SortOrder);
        var replacementLineIds = new HashSet<Guid>();
        var validatedLines = new List<(ExpenseRequestLineDraft Draft, Guid Id, ExpenseRequestLine Validated)>();
        foreach (var line in lines ?? [])
        {
            // Bound lazy/direct domain callers too, before any aggregate mutation.
            if (validatedLines.Count >= ExpenseRequestAllocationPolicy.MaximumLines)
                throw new DomainRuleViolationException("Expense request line limit exceeded.");
            var lineId = line.Id ?? Guid.NewGuid();
            if (line.Id.HasValue && !existingLines.ContainsKey(lineId))
            {
                throw new DomainRuleViolationException(
                    "An existing line id does not belong to this expense request.");
            }

            if (!replacementLineIds.Add(lineId))
            {
                throw new DomainRuleViolationException(
                    "An expense request line cannot be included more than once.");
            }

            var assignedSortOrder = existingLines.TryGetValue(lineId, out var existingLine)
                ? existingLine.SortOrder
                : TakeNextSortOrder(ref currentMaximumSortOrder);
            var validated = new ExpenseRequestLine(
                lineId,
                Id,
                line.CashFlowItemId,
                line.CostCenterId ?? normalizedCore.DefaultCostCenterId,
                line.AccrualPeriod,
                line.Amount,
                assignedSortOrder,
                line.VatRateCode, line.AccrualCostCenterId);
            validatedLines.Add((line, lineId, validated));
        }

        var replacementLines = new List<ExpenseRequestLine>(validatedLines.Count);
        foreach (var (line, lineId, validated) in validatedLines)
        {
            if (existingLines.TryGetValue(lineId, out var existingLine))
            {
                existingLine.Update(
                    line.CashFlowItemId,
                    line.CostCenterId ?? normalizedCore.DefaultCostCenterId,
                    line.AccrualPeriod,
                    line.Amount,
                    line.VatRateCode, line.AccrualCostCenterId ?? existingLine.AccrualCostCenterId);
                replacementLines.Add(existingLine);
            }
            else
            {
                replacementLines.Add(validated);
            }
        }

        OrganizationId = normalizedCore.OrganizationId;
        DefaultCostCenterId = normalizedCore.DefaultCostCenterId;
        PlannedPaymentDate = plannedPaymentDate;
        CurrencyCode = normalizedCurrency;
        CounterpartyId = normalizedCore.CounterpartyId;
        ContractId = normalizedCore.ContractId;
        Purpose = normalizedPurpose;
        Comment = normalizedComment;
        BusinessJustification = normalizedBusinessJustification;
        IsUrgent = isUrgent;
        UrgencyReason = normalizedUrgencyReason;
        IsNewCounterparty = normalizedCore.IsNewCounterparty;
        IsNewContract = normalizedCore.IsNewContract;
        ApplyEvidence(normalizedCore.Evidence);
        UpdatedAtUtc = updatedAtUtc;
        ConcurrencyStamp = Guid.NewGuid();

        _lines.Clear();
        _lines.AddRange(replacementLines.OrderBy(line => line.SortOrder));
        ResetPreliminaryCurrencyCalculation();
    }

    public void ApplyPreliminaryCurrencyCalculation(
        DateOnly? rateDate,
        decimal rate,
        decimal multiplicity,
        IReadOnlyCollection<ExpenseRequestLineKztCalculation> lineCalculations)
    {
        ArgumentNullException.ThrowIfNull(lineCalculations);
        ValidateRateSnapshot(rateDate, rate, multiplicity);

        if (lineCalculations.Count != _lines.Count)
        {
            throw new DomainRuleViolationException(
                "Currency calculation must contain exactly one value for every request line.");
        }

        var calculations = new Dictionary<Guid, ExpenseRequestLineKztCalculation>();
        foreach (var calculation in lineCalculations)
        {
            if (calculation.LineId == Guid.Empty || !calculations.TryAdd(calculation.LineId, calculation))
            {
                throw new DomainRuleViolationException(
                    "Currency calculation line identifiers must be non-empty and unique.");
            }

            MoneyAmountPolicy.EnsureNonNegative(calculation.AmountKzt, "Line KZT amount");
            if (calculation.VatAmountKzt.HasValue != calculation.AmountExcludingVatKzt.HasValue)
            {
                throw new DomainRuleViolationException(
                    "Line KZT VAT components must either both be present or both be absent.");
            }

            if (calculation.VatAmountKzt is { } vatKzt
                && calculation.AmountExcludingVatKzt is { } excludingVatKzt)
            {
                MoneyAmountPolicy.EnsureNonNegative(vatKzt, "Line KZT VAT amount");
                MoneyAmountPolicy.EnsureNonNegative(
                    excludingVatKzt,
                    "Line KZT amount excluding VAT");
                if (vatKzt + excludingVatKzt != calculation.AmountKzt)
                {
                    throw new DomainRuleViolationException(
                        "Line KZT VAT components must add up to the line KZT amount.");
                }
            }
        }

        foreach (var line in _lines)
        {
            if (!calculations.TryGetValue(line.Id, out var calculation))
            {
                throw new DomainRuleViolationException(
                    "Currency calculation contains a foreign or missing request line.");
            }

            if (line.VatRateCode.HasValue != calculation.VatAmountKzt.HasValue)
            {
                throw new DomainRuleViolationException(
                    "Currency calculation VAT shape does not match the request line.");
            }
        }

        CurrencyRateDate = rateDate;
        CurrencyRate = rate;
        CurrencyMultiplicity = multiplicity;
        foreach (var line in _lines)
        {
            var calculation = calculations[line.Id];
            line.ApplyKztCalculation(
                calculation.AmountKzt,
                calculation.VatAmountKzt,
                calculation.AmountExcludingVatKzt);
        }

        ConcurrencyStamp = Guid.NewGuid();
    }

    public void ClearPreliminaryCurrencyCalculation()
    {
        CurrencyRateDate = null;
        CurrencyRate = null;
        CurrencyMultiplicity = null;
        foreach (var line in _lines)
        {
            line.ClearKztCalculation();
        }

        ConcurrencyStamp = Guid.NewGuid();
    }

    /// <summary>
    /// Advances optimistic concurrency after an attachment was added or
    /// removed. The service proves actor authority and the storage write; the
    /// aggregate owns the status and monotonic-time invariants.
    /// </summary>
    public void RecordAttachmentMutation(
        Guid expectedVersion,
        DateTimeOffset occurredAtUtc,
        bool isRemoval,
        bool isPostSubmission)
    {
        if (expectedVersion == Guid.Empty || ConcurrencyStamp != expectedVersion)
        {
            throw new DomainRuleViolationException(
                "Expense-request attachment version does not match.");
        }

        if (occurredAtUtc.Offset != TimeSpan.Zero || occurredAtUtc < UpdatedAtUtc)
        {
            throw new DomainRuleViolationException(
                "Expense-request attachment time must be monotonic UTC.");
        }

        if (Status.IsEditableDraft())
        {
            if (isPostSubmission)
            {
                throw new DomainRuleViolationException(
                    "A draft attachment cannot be marked as post-submission evidence.");
            }
        }
        else
        {
            if (isRemoval || !isPostSubmission || !Status.CanAcceptAdditionalAttachment())
            {
                throw new DomainRuleViolationException(
                    "Submitted attachments are immutable outside the active business process.");
            }
        }

        UpdatedAtUtc = occurredAtUtc;
        ConcurrencyStamp = Guid.NewGuid();
    }

    /// <summary>
    /// Initializes the server-owned bank payment purpose at submission time.
    /// An already stored value is preserved so a later treasury value cannot
    /// be overwritten by resubmission after a return.
    /// </summary>
    public void InitializePaymentPurposeForSubmission(
        Guid expectedVersion,
        DateTimeOffset occurredAtUtc)
    {
        if (expectedVersion == Guid.Empty || ConcurrencyStamp != expectedVersion)
        {
            throw new DomainRuleViolationException(
                "Expense-request payment-purpose version does not match.");
        }

        if (!Status.IsEditableDraft())
        {
            throw new DomainRuleViolationException(
                "Payment purpose can be initialized only when submitting an editable request.");
        }

        if (occurredAtUtc.Offset != TimeSpan.Zero || occurredAtUtc < UpdatedAtUtc)
        {
            throw new DomainRuleViolationException(
                "Expense-request payment-purpose time must be monotonic UTC.");
        }

        if (!string.IsNullOrWhiteSpace(Purpose))
        {
            return;
        }

        Purpose = NormalizeRequiredText(
            BusinessJustification,
            1_000,
            nameof(BusinessJustification));
        UpdatedAtUtc = occurredAtUtc;
        ConcurrencyStamp = Guid.NewGuid();
    }

    /// <summary>
    /// Applies a transition that has already been planned by the versioned
    /// workflow matrix and authorized by the application service. The aggregate
    /// owns optimistic concurrency, time and active-route invariants; it never
    /// infers a role or chooses a target status itself.
    /// </summary>
    public void ApplyWorkflowTransition(
        Guid expectedVersion,
        DateTimeOffset occurredAtUtc,
        ExpenseRequestStatus expectedStatus,
        ExpenseRequestStatus targetStatus,
        int? activeSnapshotNumber,
        int activeStageNumber)
    {
        if (expectedVersion == Guid.Empty || ConcurrencyStamp != expectedVersion)
        {
            throw new DomainRuleViolationException("Expense-request workflow version does not match.");
        }

        if (Status != expectedStatus)
        {
            throw new DomainRuleViolationException("Expense-request workflow status does not match.");
        }

        if (occurredAtUtc.Offset != TimeSpan.Zero || occurredAtUtc < UpdatedAtUtc)
        {
            throw new DomainRuleViolationException("Expense-request workflow time must be monotonic UTC.");
        }

        var hasActiveRoute = targetStatus == ExpenseRequestStatus.InApproval;
        var routeStateIsValid = hasActiveRoute
            ? activeSnapshotNumber is { } snapshotNumber
              && snapshotNumber > 0
              && activeStageNumber > 0
            : activeSnapshotNumber is null && activeStageNumber == 0;
        if (!routeStateIsValid)
        {
            throw new DomainRuleViolationException(
                "Expense-request workflow route state is inconsistent with its target status.");
        }

        Status = targetStatus;
        ActiveApprovalRouteSnapshotNumber = activeSnapshotNumber;
        ActiveApprovalRouteStageNumber = activeStageNumber;
        UpdatedAtUtc = occurredAtUtc;
        ConcurrencyStamp = Guid.NewGuid();
    }

    /// <summary>
    /// Moves the request between the payment-driven statuses after the
    /// confirmed accounting ledger has been recalculated. This is not a
    /// workflow command: no role issues it and no user chooses the target.
    /// The caller passes the status a factual plan derived from the full
    /// ledger, so a reversal simply lands on a lower status instead of
    /// appending a compensating movement. CLOSED is never touched here.
    /// </summary>
    public void ApplyPaymentSettlementProjection(
        Guid expectedVersion,
        DateTimeOffset occurredAtUtc,
        ExpenseRequestStatus targetStatus)
    {
        if (expectedVersion == Guid.Empty || ConcurrencyStamp != expectedVersion)
        {
            throw new DomainRuleViolationException(
                "Expense-request payment version does not match.");
        }

        if (Status is not (ExpenseRequestStatus.ToPay
            or ExpenseRequestStatus.PartiallyPaid
            or ExpenseRequestStatus.Paid))
        {
            throw new DomainRuleViolationException(
                "Confirmed payments apply only to TO_PAY, PARTIALLY_PAID or PAID requests.");
        }

        if (targetStatus is not (ExpenseRequestStatus.ToPay
            or ExpenseRequestStatus.PartiallyPaid
            or ExpenseRequestStatus.Paid))
        {
            throw new DomainRuleViolationException(
                "A payment projection can only target TO_PAY, PARTIALLY_PAID or PAID.");
        }

        if (occurredAtUtc.Offset != TimeSpan.Zero || occurredAtUtc < UpdatedAtUtc)
        {
            throw new DomainRuleViolationException(
                "Expense-request payment time must be monotonic UTC.");
        }

        if (ActiveApprovalRouteSnapshotNumber is not null
            || ActiveApprovalRouteStageNumber != 0)
        {
            throw new DomainRuleViolationException(
                "A request in a payment status cannot hold an active approval route.");
        }

        Status = targetStatus;
        UpdatedAtUtc = occurredAtUtc;
        ConcurrencyStamp = Guid.NewGuid();
    }

    /// <summary>
    /// Applies one approved partial document closing while the request remains
    /// PAID. The caller supplies a pure allocation plan and owns persistence
    /// of its control excess, reason, audit and idempotency evidence.
    /// </summary>
    public void ApplyPartialClosing(
        Guid expectedVersion,
        DateTimeOffset occurredAtUtc,
        DateOnly documentsReceivedDate,
        Guid? accrualCycleId,
        DateTimeOffset fullyPaidAtUtc,
        decimal controlAmount,
        IReadOnlyList<AccrualClosingLineAllocation> allocations)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        EnsureClosingTransition(
            expectedVersion,
            occurredAtUtc,
            ExpenseRequestStatus.Paid);
        var hasQualifiedLines = _lines.Any(
            line => line.AccrualSnapshotFormsManagerialAccrual == true);
        EnsureClosingHeader(
            documentsReceivedDate,
            accrualCycleId,
            fullyPaidAtUtc,
            hasQualifiedLines);
        MoneyAmountPolicy.EnsurePositive(
            controlAmount,
            "Closing control amount");

        var lineById = _lines.ToDictionary(line => line.Id);
        var activeTotal = 0m;
        foreach (var line in _lines.Where(
                     line => line.AccrualSnapshotFormsManagerialAccrual == true))
        {
            if (line.AccrualSnapshotExpenseItemId is null ||
                line.AmountKzt is not { } originalAmountKzt ||
                line.ConfirmedClosedAmount < 0m ||
                line.ConfirmedClosedAmountKzt < 0m ||
                line.ConfirmedClosedAmount > line.Amount ||
                line.ConfirmedClosedAmountKzt > originalAmountKzt)
            {
                throw new DomainRuleViolationException(
                    "Stored closing totals violate the immutable source balance.");
            }

            var remaining = line.Amount - line.ConfirmedClosedAmount;
            var remainingKzt = originalAmountKzt - line.ConfirmedClosedAmountKzt;
            if ((remaining == 0m && remainingKzt != 0m) || remainingKzt < 0m)
            {
                throw new DomainRuleViolationException(
                    "Stored closing totals violate the immutable source balance.");
            }

            try
            {
                activeTotal = checked(activeTotal + remaining);
            }
            catch (OverflowException)
            {
                throw new DomainRuleViolationException(
                    "Active closing amount exceeds decimal storage range.");
            }
        }
        MoneyAmountPolicy.EnsureNonNegative(activeTotal, "Active closing amount");

        var seen = new HashSet<Guid>();
        var validated = new List<(ExpenseRequestLine Line, AccrualClosingLineAllocation Allocation)>();
        var allocatedTotal = 0m;
        foreach (var allocation in allocations.OrderBy(item => item.SortOrder))
        {
            if (allocation is null ||
                allocation.LineId == Guid.Empty ||
                allocation.SortOrder <= 0 ||
                allocation.Amount <= 0m ||
                !seen.Add(allocation.LineId) ||
                !lineById.TryGetValue(allocation.LineId, out var line) ||
                line.SortOrder != allocation.SortOrder)
            {
                throw new DomainRuleViolationException(
                    "Partial closing allocation does not match the request lines.");
            }

            if (line.AccrualSnapshotFormsManagerialAccrual != true ||
                line.AccrualSnapshotExpenseItemId is null ||
                line.AmountKzt is not { } originalAmountKzt)
            {
                throw new DomainRuleViolationException(
                    "Only a captured managerial-accrual line can be closed.");
            }

            var remainingAmount = line.Amount - line.ConfirmedClosedAmount;
            var remainingAmountKzt = originalAmountKzt - line.ConfirmedClosedAmountKzt;
            var expectedAmountKzt = PairedMoneyAllocationPolicy.Allocate(
                remainingAmountKzt,
                allocation.Amount,
                remainingAmount);
            if (allocation.AmountKzt != expectedAmountKzt ||
                remainingAmount - allocation.Amount != allocation.RemainingAmount ||
                remainingAmountKzt - allocation.AmountKzt !=
                    allocation.RemainingAmountKzt)
            {
                throw new DomainRuleViolationException(
                    "Partial closing allocation result is inconsistent.");
            }

            try
            {
                allocatedTotal = checked(allocatedTotal + allocation.Amount);
            }
            catch (OverflowException)
            {
                throw new DomainRuleViolationException(
                    "Closing allocation exceeds decimal storage range.");
            }
            validated.Add((line, allocation));
        }
        if (allocatedTotal != decimal.Min(controlAmount, activeTotal))
        {
            throw new DomainRuleViolationException(
                "Partial closing allocations do not match the control amount.");
        }

        foreach (var (line, allocation) in validated)
        {
            line.ApplyConfirmedClosingAllocation(
                allocation.Amount,
                allocation.AmountKzt);
        }

        ApplyClosingHeader(accrualCycleId, fullyPaidAtUtc);
        ConfirmedClosingControlAmount = AddClosingControlAmount(controlAmount);
        ClosedPartially = true;
        LatestClosingDocumentsReceivedDate = documentsReceivedDate;
        UpdatedAtUtc = occurredAtUtc;
        ConcurrencyStamp = Guid.NewGuid();
    }

    /// <summary>
    /// Closes a fully paid request and consumes every remaining qualified line
    /// balance. Payment facts and the cycle identity are preserved.
    /// </summary>
    public void ApplyFullClosing(
        Guid expectedVersion,
        DateTimeOffset occurredAtUtc,
        DateOnly documentsReceivedDate,
        Guid? accrualCycleId,
        DateTimeOffset fullyPaidAtUtc)
    {
        EnsureClosingTransition(
            expectedVersion,
            occurredAtUtc,
            ExpenseRequestStatus.Paid);
        var qualifiedLines = _lines
            .Where(line => line.AccrualSnapshotFormsManagerialAccrual == true)
            .OrderBy(line => line.SortOrder)
            .ToArray();
        EnsureClosingHeader(
            documentsReceivedDate,
            accrualCycleId,
            fullyPaidAtUtc,
            qualifiedLines.Length > 0);

        var remainders = new List<(ExpenseRequestLine Line, decimal Amount, decimal AmountKzt)>();
        var remainingTotal = 0m;
        foreach (var line in qualifiedLines)
        {
            if (line.AmountKzt is not { } amountKzt)
            {
                throw new DomainRuleViolationException(
                    "A qualified accrual line requires its immutable KZT amount.");
            }

            var remaining = line.Amount - line.ConfirmedClosedAmount;
            var remainingKzt = amountKzt - line.ConfirmedClosedAmountKzt;
            if (remaining < 0m || remainingKzt < 0m)
            {
                throw new DomainRuleViolationException(
                    "Stored closing totals exceed the immutable line amount.");
            }
            if (remaining > 0m)
            {
                try
                {
                    remainingTotal = checked(remainingTotal + remaining);
                }
                catch (OverflowException)
                {
                    throw new DomainRuleViolationException(
                        "Closing control amount exceeds decimal storage range.");
                }
                remainders.Add((line, remaining, remainingKzt));
            }
            else if (remainingKzt != 0m)
            {
                throw new DomainRuleViolationException(
                    "A fully closed line cannot retain a KZT balance.");
            }
        }

        foreach (var (line, remaining, remainingKzt) in remainders)
        {
            line.ApplyConfirmedClosingAllocation(remaining, remainingKzt);
        }

        ApplyClosingHeader(accrualCycleId, fullyPaidAtUtc);
        ConfirmedClosingControlAmount = AddClosingControlAmount(remainingTotal);
        Status = ExpenseRequestStatus.Closed;
        ClosedPartially = false;
        LatestClosingDocumentsReceivedDate = documentsReceivedDate;
        UpdatedAtUtc = occurredAtUtc;
        ConcurrencyStamp = Guid.NewGuid();
    }

    /// <summary>
    /// Explicitly reopens a closed request, preserving its first full-payment
    /// time while resetting current-cycle closure totals. A request with
    /// qualified accrual lines must receive a different nonempty cycle id.
    /// </summary>
    public void ApplyReopen(
        Guid expectedVersion,
        DateTimeOffset occurredAtUtc,
        Guid? nextAccrualCycleId)
    {
        EnsureClosingTransition(
            expectedVersion,
            occurredAtUtc,
            ExpenseRequestStatus.Closed);
        if (FullyPaidAtUtc is null)
        {
            throw new DomainRuleViolationException(
                "A closed request must preserve its original full-payment time.");
        }

        var hasQualifiedLines = _lines.Any(
            line => line.AccrualSnapshotFormsManagerialAccrual == true);
        if (hasQualifiedLines &&
            (nextAccrualCycleId is null ||
             nextAccrualCycleId == Guid.Empty ||
             nextAccrualCycleId == AccrualCycleId))
        {
            throw new DomainRuleViolationException(
                "Reopening an accrual request requires a new cycle identifier.");
        }
        if (!hasQualifiedLines && nextAccrualCycleId is not null)
        {
            throw new DomainRuleViolationException(
                "A request without accrual lines cannot receive an accrual cycle.");
        }

        foreach (var line in _lines)
        {
            line.ResetConfirmedClosing();
        }

        Status = ExpenseRequestStatus.Paid;
        ClosedPartially = false;
        AccrualCycleId = nextAccrualCycleId;
        LatestClosingDocumentsReceivedDate = null;
        ConfirmedClosingControlAmount = 0m;
        UpdatedAtUtc = occurredAtUtc;
        ConcurrencyStamp = Guid.NewGuid();
    }

    private void EnsureClosingTransition(
        Guid expectedVersion,
        DateTimeOffset occurredAtUtc,
        ExpenseRequestStatus expectedStatus)
    {
        if (expectedVersion == Guid.Empty || ConcurrencyStamp != expectedVersion)
        {
            throw new DomainRuleViolationException(
                "Expense-request closing version does not match.");
        }
        if (Status != expectedStatus)
        {
            throw new DomainRuleViolationException(
                "Expense-request closing status does not match.");
        }
        if (occurredAtUtc.Offset != TimeSpan.Zero || occurredAtUtc < UpdatedAtUtc)
        {
            throw new DomainRuleViolationException(
                "Expense-request closing time must be monotonic UTC.");
        }
        if (ActiveApprovalRouteSnapshotNumber is not null ||
            ActiveApprovalRouteStageNumber != 0)
        {
            throw new DomainRuleViolationException(
                "A request in a closing status cannot hold an active approval route.");
        }
    }

    private void EnsureClosingHeader(
        DateOnly documentsReceivedDate,
        Guid? accrualCycleId,
        DateTimeOffset fullyPaidAtUtc,
        bool requiresCycle)
    {
        if (documentsReceivedDate == default ||
            fullyPaidAtUtc == default ||
            fullyPaidAtUtc.Offset != TimeSpan.Zero ||
            fullyPaidAtUtc > UpdatedAtUtc)
        {
            throw new DomainRuleViolationException(
                "Expense-request closing evidence is invalid.");
        }
        if (requiresCycle &&
            (accrualCycleId is null || accrualCycleId == Guid.Empty))
        {
            throw new DomainRuleViolationException(
                "A qualified accrual request requires a cycle identifier.");
        }
        if (!requiresCycle && accrualCycleId is not null)
        {
            throw new DomainRuleViolationException(
                "A request without accrual lines cannot receive an accrual cycle.");
        }
        if (AccrualCycleId is { } currentCycleId && currentCycleId != accrualCycleId)
        {
            throw new DomainRuleViolationException(
                "The closing command does not belong to the current accrual cycle.");
        }
        if (FullyPaidAtUtc is { } storedFullyPaidAtUtc &&
            storedFullyPaidAtUtc != fullyPaidAtUtc)
        {
            throw new DomainRuleViolationException(
                "The original full-payment time cannot change.");
        }

    }

    private void ApplyClosingHeader(
        Guid? accrualCycleId,
        DateTimeOffset fullyPaidAtUtc)
    {
        AccrualCycleId = accrualCycleId;
        FullyPaidAtUtc ??= fullyPaidAtUtc;
    }

    private decimal AddClosingControlAmount(decimal amount)
    {
        MoneyAmountPolicy.EnsureNonNegative(amount, "Closing control amount");
        decimal total;
        try
        {
            total = checked(ConfirmedClosingControlAmount + amount);
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException(
                "Closing control amount exceeds decimal storage range.");
        }

        MoneyAmountPolicy.EnsureNonNegative(total, "Closing control amount");
        return total;
    }

    /// <summary>
    /// Applies the authoritative links selected by an NSI specialist before
    /// the AWAITING_NSI -> APPROVED transition. The operation is intentionally
    /// narrow: it can only resolve candidate flags that were present in the
    /// submitted request and cannot edit ordinary approved references.
    /// </summary>
    public void ApplyNsiReferenceLinks(
        Guid expectedVersion,
        DateTimeOffset occurredAtUtc,
        Guid? counterpartyId,
        Guid? contractId)
    {
        if (expectedVersion == Guid.Empty || ConcurrencyStamp != expectedVersion)
        {
            throw new DomainRuleViolationException("Expense-request NSI version does not match.");
        }

        if (Status != ExpenseRequestStatus.AwaitingNsi)
        {
            throw new DomainRuleViolationException("NSI links can be applied only while awaiting NSI.");
        }

        if (occurredAtUtc.Offset != TimeSpan.Zero || occurredAtUtc < UpdatedAtUtc)
        {
            throw new DomainRuleViolationException("Expense-request NSI time must be monotonic UTC.");
        }

        if (!IsNewCounterparty && !IsNewContract)
        {
            throw new DomainRuleViolationException(
                "NSI confirmation requires at least one candidate reference.");
        }

        if (IsNewCounterparty)
        {
            if (counterpartyId is not { } linkedCounterpartyId || linkedCounterpartyId == Guid.Empty)
            {
                throw new DomainRuleViolationException("An imported counterparty is required for NSI confirmation.");
            }

            CounterpartyId = linkedCounterpartyId;
            IsNewCounterparty = false;
            ClearCounterpartyCandidate();
        }
        else if (counterpartyId is not null)
        {
            throw new DomainRuleViolationException("A counterparty link was supplied for a non-candidate request.");
        }

        if (IsNewContract)
        {
            if (contractId is not { } linkedContractId || linkedContractId == Guid.Empty)
            {
                throw new DomainRuleViolationException("An imported contract is required for NSI confirmation.");
            }

            ContractId = linkedContractId;
            IsNewContract = false;
            ClearContractCandidate();
        }
        else if (contractId is not null)
        {
            throw new DomainRuleViolationException("A contract link was supplied for a non-candidate request.");
        }

        ConcurrencyStamp = Guid.NewGuid();
        UpdatedAtUtc = occurredAtUtc;
    }

    private void ClearCounterpartyCandidate()
    {
        CandidateCounterpartyName = null;
        CandidateResidencyCode = null;
        CandidateBinIin = null;
        CandidateCountry = null;
        CandidateForeignRegistrationNumber = null;
        CandidateRegistrationNumberMissing = false;
        CandidateRegistrationNumberMissingExplanation = null;
    }

    private void ClearContractCandidate()
    {
        CandidateContractNumber = null;
        CandidateContractNumberMissing = false;
        CandidateContractDate = null;
        CandidateContractSubject = null;
        CandidateContractTerms = null;
        NewContractDocumentMissing = false;
        NewContractDocumentMissingExplanation = null;
    }

    internal void RefreshReturnedAccrualSnapshot(
        Guid expectedVersion,
        IReadOnlyList<ExpenseRequestLineAccrualSnapshotAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        if (Id == Guid.Empty || expectedVersion == Guid.Empty || ConcurrencyStamp != expectedVersion
            || Status != ExpenseRequestStatus.Returned)
            throw AccrualSnapshotInvariantViolation();
        var captured = assignments.ToArray();
        var lines = _lines.ToArray();
        if (lines.Length == 0 || lines.Length != captured.Length || lines.Any(line => line is null))
            throw AccrualSnapshotInvariantViolation();
        // EF navigation materialization follows database order, not SortOrder.
        // Match the source plan in document order without reordering the backing
        // navigation or weakening the batch/identity/version guards below.
        Array.Sort(lines, static (left, right) => left.SortOrder.CompareTo(right.SortOrder));
        var seen = new HashSet<Guid>();
        var previousOrder = 0;
        var changed = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var assignment = captured[index];
            if (line is null || assignment is null || line.Id == Guid.Empty || !seen.Add(line.Id)
                || line.ExpenseRequestId != Id || line.Id != assignment.LineId
                || line.SortOrder <= previousOrder || !line.HasValidOrAbsentAccrualSnapshot()
                || (!assignment.FormsManagerialAccrual && assignment.ExpenseItemId is not null)
                || (assignment.FormsManagerialAccrual
                    && (assignment.ExpenseItemId is null || assignment.ExpenseItemId == Guid.Empty)))
                throw AccrualSnapshotInvariantViolation();
            previousOrder = line.SortOrder;
            changed |= line.AccrualSnapshotFormsManagerialAccrual != assignment.FormsManagerialAccrual
                || line.AccrualSnapshotExpenseItemId != assignment.ExpenseItemId;
        }
        if (ConcurrencyStamp != expectedVersion || Status != ExpenseRequestStatus.Returned)
            throw AccrualSnapshotInvariantViolation();
        if (!changed) return;
        for (var index = 0; index < lines.Length; index++)
            lines[index].ApplyResubmittedAccrualSnapshot(captured[index].FormsManagerialAccrual, captured[index].ExpenseItemId);
        var version = Guid.NewGuid();
        while (version == Guid.Empty || version == expectedVersion) version = Guid.NewGuid();
        ConcurrencyStamp = version;
    }

    internal void ApplyInitialAccrualSnapshot(
        Guid expectedVersion,
        IReadOnlyList<ExpenseRequestLineAccrualSnapshotAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        if (expectedVersion == Guid.Empty ||
            ConcurrencyStamp != expectedVersion ||
            Status != ExpenseRequestStatus.Draft)
        {
            throw AccrualSnapshotInvariantViolation();
        }

        var capturedAssignments = assignments.ToArray();
        ExpenseRequestLine[] lines;
        try
        {
            lines = Lines.ToArray();
        }
        catch (NullReferenceException)
        {
            throw AccrualSnapshotInvariantViolation();
        }

        if (capturedAssignments.Length == 0 ||
            capturedAssignments.Length != lines.Length)
        {
            throw AccrualSnapshotInvariantViolation();
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var assignment = capturedAssignments[index];
            if (line is null ||
                assignment is null ||
                line.ExpenseRequestId != Id ||
                line.Id != assignment.LineId ||
                (!assignment.FormsManagerialAccrual && assignment.ExpenseItemId is not null) ||
                (assignment.FormsManagerialAccrual &&
                 (assignment.ExpenseItemId is null || assignment.ExpenseItemId == Guid.Empty)) ||
                !line.HasAbsentAccrualSnapshot())
            {
                throw AccrualSnapshotInvariantViolation();
            }
        }

        if (ConcurrencyStamp != expectedVersion || Status != ExpenseRequestStatus.Draft)
        {
            throw AccrualSnapshotInvariantViolation();
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var assignment = capturedAssignments[index];
            lines[index].ApplyInitialAccrualSnapshot(
                assignment.FormsManagerialAccrual,
                assignment.ExpenseItemId);
        }

        var nextVersion = Guid.NewGuid();
        while (nextVersion == Guid.Empty || nextVersion == expectedVersion)
        {
            nextVersion = Guid.NewGuid();
        }

        ConcurrencyStamp = nextVersion;
    }

    public bool HasValidPreliminaryCurrencyCalculation()
    {
        if (!CurrencyRate.HasValue || !CurrencyMultiplicity.HasValue)
        {
            return false;
        }

        if (string.Equals(CurrencyCode, "KZT", StringComparison.Ordinal))
        {
            if (CurrencyRateDate is not null || CurrencyRate != 1m || CurrencyMultiplicity != 1m)
            {
                return false;
            }
        }
        else if (CurrencyRateDate is null || CurrencyRate <= 0m || CurrencyMultiplicity <= 0m)
        {
            return false;
        }

        return _lines.All(line => line.AmountKzt.HasValue && line.HasValidKztCalculatedAmounts());
    }

    private void ValidateRateSnapshot(DateOnly? rateDate, decimal rate, decimal multiplicity)
    {
        if (rate <= 0m
            || decimal.Round(rate, 6, MidpointRounding.ToEven) != rate)
        {
            throw new DomainRuleViolationException(
                "Currency rate must be positive and have no more than six decimal places.");
        }

        if (multiplicity <= 0m || decimal.Truncate(multiplicity) != multiplicity)
        {
            throw new DomainRuleViolationException(
                "Currency multiplicity must be a positive integer.");
        }

        if (string.Equals(CurrencyCode, "KZT", StringComparison.Ordinal))
        {
            if (rateDate is not null || rate != 1m || multiplicity != 1m)
            {
                throw new DomainRuleViolationException(
                    "KZT calculation requires no rate date and rate/multiplicity 1/1.");
            }
        }
        else if (rateDate is null)
        {
            throw new DomainRuleViolationException(
                "A foreign-currency calculation requires its source rate date.");
        }
    }

    private void ResetPreliminaryCurrencyCalculation()
    {
        CurrencyRateDate = null;
        CurrencyRate = null;
        CurrencyMultiplicity = null;
        foreach (var line in _lines)
        {
            if (string.Equals(CurrencyCode, "KZT", StringComparison.Ordinal))
            {
                line.ApplyKztCalculation(line.Amount, line.VatAmount, line.AmountExcludingVat);
            }
            else
            {
                line.ClearKztCalculation();
            }
        }
    }

    private static InvalidOperationException AccrualSnapshotInvariantViolation() => new(
        "The initial accrual snapshot violates the expense request invariant.");

    private void ApplyEvidence(ExpenseRequestEvidenceDraft evidence)
    {
        PaymentOperation = evidence.PaymentOperation;
        PaymentBasisCode = evidence.PaymentBasisCode;
        InvoiceNumber = evidence.InvoiceNumber;
        InvoiceDate = evidence.InvoiceDate;
        PublicOfferName = evidence.PublicOfferName;
        PublicOfferIdentifier = evidence.PublicOfferIdentifier;
        OtherBasisExplanation = evidence.OtherBasisExplanation;
        SupportingDocumentMissing = evidence.SupportingDocumentMissing;
        SupportingDocumentMissingExplanation = evidence.SupportingDocumentMissingExplanation;
        CandidateCounterpartyName = evidence.CandidateCounterpartyName;
        CandidateResidencyCode = evidence.CandidateResidencyCode;
        CandidateBinIin = evidence.CandidateBinIin;
        CandidateCountry = evidence.CandidateCountry;
        CandidateForeignRegistrationNumber = evidence.CandidateForeignRegistrationNumber;
        CandidateRegistrationNumberMissing = evidence.CandidateRegistrationNumberMissing;
        CandidateRegistrationNumberMissingExplanation = evidence.CandidateRegistrationNumberMissingExplanation;
        CandidateContractNumber = evidence.CandidateContractNumber;
        CandidateContractNumberMissing = evidence.CandidateContractNumberMissing;
        CandidateContractDate = evidence.CandidateContractDate;
        CandidateContractSubject = evidence.CandidateContractSubject;
        CandidateContractTerms = evidence.CandidateContractTerms;
        NewContractDocumentMissing = evidence.NewContractDocumentMissing;
        NewContractDocumentMissingExplanation = evidence.NewContractDocumentMissingExplanation;
    }

    private static NormalizedCoreReferencesAndEvidence NormalizeCoreReferencesAndEvidence(
        Guid? organizationId,
        Guid? defaultCostCenterId,
        Guid? counterpartyId,
        Guid? contractId,
        bool isNewCounterparty,
        bool isNewContract,
        ExpenseRequestEvidenceDraft? evidence)
    {
        evidence ??= new ExpenseRequestEvidenceDraft();
        var paymentOperation = ExpensePaymentOperation.Normalize(evidence.PaymentOperation);
        if (paymentOperation is not null && (counterpartyId is not null || contractId is not null || isNewCounterparty || isNewContract
            || evidence.PaymentBasisCode is not null || evidence.InvoiceNumber is not null || evidence.CandidateCounterpartyName is not null))
            throw new DomainRuleViolationException("Supplier requisites cannot be used for this payment operation.");
        evidence = evidence with { PaymentOperation = paymentOperation };
        var paymentBasisCode = NormalizeOptionalEnum(
            evidence.PaymentBasisCode,
            nameof(evidence.PaymentBasisCode));
        var effectiveIsNewContract = isNewContract && paymentBasisCode == PaymentBasisType.Contract;
        var normalizedEvidence = NormalizeEvidence(
            evidence with { PaymentBasisCode = paymentBasisCode },
            isNewCounterparty,
            effectiveIsNewContract);

        return new NormalizedCoreReferencesAndEvidence(
            NormalizeOptionalReference(organizationId),
            NormalizeOptionalReference(defaultCostCenterId),
            isNewCounterparty ? null : NormalizeOptionalReference(counterpartyId),
            paymentBasisCode == PaymentBasisType.Contract && !effectiveIsNewContract
                ? NormalizeOptionalReference(contractId)
                : null,
            isNewCounterparty,
            effectiveIsNewContract,
            normalizedEvidence);
    }

    private static Guid? NormalizeOptionalReference(Guid? value) =>
        value is null || value == Guid.Empty ? null : value;

    private static int TakeNextSortOrder(ref int currentMaximumSortOrder)
    {
        try
        {
            currentMaximumSortOrder = checked(currentMaximumSortOrder + 1);
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException(
                "The expense request cannot contain more ordered lines.");
        }

        return currentMaximumSortOrder;
    }

    private static ExpenseRequestEvidenceDraft NormalizeEvidence(
        ExpenseRequestEvidenceDraft? evidence,
        bool isNewCounterparty,
        bool isNewContract)
    {
        evidence ??= new ExpenseRequestEvidenceDraft();
        var normalized = evidence with
        {
            PaymentBasisCode = NormalizeOptionalEnum(
                evidence.PaymentBasisCode,
                nameof(evidence.PaymentBasisCode)),
            InvoiceNumber = NormalizeOptionalText(evidence.InvoiceNumber, 100, nameof(evidence.InvoiceNumber)),
            PublicOfferName = NormalizeOptionalText(
                evidence.PublicOfferName,
                300,
                nameof(evidence.PublicOfferName)),
            PublicOfferIdentifier = NormalizeOptionalText(
                evidence.PublicOfferIdentifier,
                1_000,
                nameof(evidence.PublicOfferIdentifier)),
            OtherBasisExplanation = NormalizeOptionalText(
                evidence.OtherBasisExplanation,
                1_000,
                nameof(evidence.OtherBasisExplanation)),
            SupportingDocumentMissingExplanation = NormalizeOptionalText(
                evidence.SupportingDocumentMissingExplanation,
                1_000,
                nameof(evidence.SupportingDocumentMissingExplanation)),
            CandidateCounterpartyName = NormalizeOptionalText(
                evidence.CandidateCounterpartyName,
                300,
                nameof(evidence.CandidateCounterpartyName)),
            CandidateResidencyCode = NormalizeOptionalEnum(
                evidence.CandidateResidencyCode,
                nameof(evidence.CandidateResidencyCode)),
            CandidateBinIin = NormalizeOptionalText(
                evidence.CandidateBinIin,
                12,
                nameof(evidence.CandidateBinIin)),
            CandidateCountry = NormalizeOptionalText(
                evidence.CandidateCountry,
                100,
                nameof(evidence.CandidateCountry)),
            CandidateForeignRegistrationNumber = NormalizeOptionalText(
                evidence.CandidateForeignRegistrationNumber,
                100,
                nameof(evidence.CandidateForeignRegistrationNumber)),
            CandidateRegistrationNumberMissingExplanation = NormalizeOptionalText(
                evidence.CandidateRegistrationNumberMissingExplanation,
                1_000,
                nameof(evidence.CandidateRegistrationNumberMissingExplanation)),
            CandidateContractNumber = NormalizeOptionalText(
                evidence.CandidateContractNumber,
                100,
                nameof(evidence.CandidateContractNumber)),
            CandidateContractSubject = NormalizeOptionalText(
                evidence.CandidateContractSubject,
                300,
                nameof(evidence.CandidateContractSubject)),
            CandidateContractTerms = NormalizeOptionalText(
                evidence.CandidateContractTerms,
                1_000,
                nameof(evidence.CandidateContractTerms)),
            NewContractDocumentMissingExplanation = NormalizeOptionalText(
                evidence.NewContractDocumentMissingExplanation,
                1_000,
                nameof(evidence.NewContractDocumentMissingExplanation))
        };

        normalized = normalized.PaymentBasisCode switch
        {
            PaymentBasisType.Contract => normalized with
            {
                InvoiceNumber = null,
                InvoiceDate = null,
                PublicOfferName = null,
                PublicOfferIdentifier = null,
                OtherBasisExplanation = null
            },
            PaymentBasisType.InvoiceWithoutContract => normalized with
            {
                PublicOfferName = null,
                PublicOfferIdentifier = null,
                OtherBasisExplanation = null
            },
            PaymentBasisType.PublicOffer => normalized with
            {
                InvoiceNumber = null,
                InvoiceDate = null,
                OtherBasisExplanation = null
            },
            PaymentBasisType.OtherBasis => normalized with
            {
                InvoiceNumber = null,
                InvoiceDate = null,
                PublicOfferName = null,
                PublicOfferIdentifier = null
            },
            null => normalized with
            {
                InvoiceNumber = null,
                InvoiceDate = null,
                PublicOfferName = null,
                PublicOfferIdentifier = null,
                OtherBasisExplanation = null
            },
            _ => normalized
        };

        if (!normalized.SupportingDocumentMissing)
        {
            normalized = normalized with { SupportingDocumentMissingExplanation = null };
        }

        if (!isNewCounterparty)
        {
            normalized = normalized with
            {
                CandidateCounterpartyName = null,
                CandidateResidencyCode = null,
                CandidateBinIin = null,
                CandidateCountry = null,
                CandidateForeignRegistrationNumber = null,
                CandidateRegistrationNumberMissing = false,
                CandidateRegistrationNumberMissingExplanation = null
            };
        }
        else if (normalized.CandidateResidencyCode == CounterpartyResidency.ResidentKz)
        {
            normalized = normalized with
            {
                CandidateCountry = null,
                CandidateForeignRegistrationNumber = null,
                CandidateRegistrationNumberMissing = false,
                CandidateRegistrationNumberMissingExplanation = null
            };
        }
        else if (normalized.CandidateResidencyCode == CounterpartyResidency.NonResident)
        {
            normalized = normalized with { CandidateBinIin = null };
            normalized = normalized.CandidateRegistrationNumberMissing
                ? normalized with { CandidateForeignRegistrationNumber = null }
                : normalized with { CandidateRegistrationNumberMissingExplanation = null };
        }
        else
        {
            normalized = normalized with
            {
                CandidateBinIin = null,
                CandidateCountry = null,
                CandidateForeignRegistrationNumber = null,
                CandidateRegistrationNumberMissing = false,
                CandidateRegistrationNumberMissingExplanation = null
            };
        }

        if (!isNewContract)
        {
            normalized = normalized with
            {
                CandidateContractNumber = null,
                CandidateContractNumberMissing = false,
                CandidateContractDate = null,
                CandidateContractSubject = null,
                CandidateContractTerms = null,
                NewContractDocumentMissing = false,
                NewContractDocumentMissingExplanation = null
            };
        }
        else
        {
            if (normalized.CandidateContractNumberMissing)
            {
                normalized = normalized with { CandidateContractNumber = null };
            }

            if (!normalized.NewContractDocumentMissing)
            {
                normalized = normalized with { NewContractDocumentMissingExplanation = null };
            }
        }

        return normalized;
    }

    private static TEnum? NormalizeOptionalEnum<TEnum>(TEnum? value, string fieldName)
        where TEnum : struct, Enum
    {
        if (value.HasValue && !Enum.IsDefined(value.Value))
        {
            throw new DomainRuleViolationException($"{fieldName} is invalid.");
        }

        return value;
    }

    private sealed record NormalizedCoreReferencesAndEvidence(
        Guid? OrganizationId,
        Guid? DefaultCostCenterId,
        Guid? CounterpartyId,
        Guid? ContractId,
        bool IsNewCounterparty,
        bool IsNewContract,
        ExpenseRequestEvidenceDraft Evidence);

    private static string NormalizeCurrency(string currencyCode)
    {
        var normalized = currencyCode.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(character => character is < 'A' or > 'Z'))
        {
            throw new DomainRuleViolationException("Currency code must contain three Latin letters.", DomainRuleViolationReason.ExpenseDraftCurrencyInvalid);
        }

        return normalized;
    }

    private static string NormalizeRequestNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 160
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException("Expense request number is not canonical.");
        }

        var parts = value.Split('-', StringSplitOptions.None);
        var prefix = parts.Length > 1 ? string.Join('-', parts[..^1]) : string.Empty;
        if (parts.Length < 2
            || parts[..^1].Any(string.IsNullOrEmpty)
            || parts[^1].Length < 5
            || !parts[^1].All(char.IsAsciiDigit)
            || parts[^1].All(character => character == '0'))
        {
            throw new DomainRuleViolationException("Expense request number is not canonical.");
        }

        string canonicalPrefix;
        try
        {
            canonicalPrefix = ExpenseRequestDocumentNumber.CanonicalizePrefix(prefix);
        }
        catch (DomainRuleViolationException)
        {
            throw new DomainRuleViolationException("Expense request number is not canonical.");
        }

        if (!string.Equals(prefix, canonicalPrefix, StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException("Expense request number is not canonical.");
        }

        return value;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new DomainRuleViolationException($"{fieldName} cannot exceed {maxLength} characters.", DraftTextReason(fieldName));
        }

        return normalized;
    }

    private static string NormalizeRequiredText(string? value, int maxLength, string fieldName)
    {
        var normalized = NormalizeOptionalText(value, maxLength, fieldName);
        if (normalized is null)
        {
            throw new DomainRuleViolationException($"{fieldName} is required.", DraftTextReason(fieldName));
        }

        return normalized;
    }

    private static DomainRuleViolationReason DraftTextReason(string fieldName) => fieldName switch
    {
        "comment" => DomainRuleViolationReason.ExpenseDraftCommentInvalid,
        "businessJustification" => DomainRuleViolationReason.ExpenseDraftJustificationInvalid,
        "urgencyReason" => DomainRuleViolationReason.ExpenseDraftUrgencyInvalid,
        _ => DomainRuleViolationReason.ExpenseDraftTextInvalid,
    };
}

public sealed record ExpenseRequestLineKztCalculation(
    Guid LineId,
    decimal AmountKzt,
    decimal? VatAmountKzt,
    decimal? AmountExcludingVatKzt);
