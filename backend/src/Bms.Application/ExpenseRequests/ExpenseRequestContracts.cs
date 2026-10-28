using Bms.Domain.ExpenseRequests;
using Bms.Domain.Budgeting;

namespace Bms.Application.ExpenseRequests;

public static class ExpenseRequestProblemCodes
{
    public const string IdempotencyConflict = "idempotency-conflict";
    public const string VersionConflict = "version-conflict";
}

public sealed record CreateExpenseRequestLineCommand(
    Guid? CashFlowItemId,
    Guid? CostCenterId,
    DateOnly? AccrualPeriod,
    decimal Amount,
    ExpenseVatRate? VatRateCode = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? AccrualCostCenterId = null);

public sealed record UpdateExpenseRequestLineCommand(
    Guid? Id,
    Guid? CashFlowItemId,
    Guid? CostCenterId,
    DateOnly? AccrualPeriod,
    decimal Amount,
    ExpenseVatRate? VatRateCode = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? AccrualCostCenterId = null);

public sealed record CreateExpenseRequestCommand(
    Guid? OrganizationId,
    Guid? DefaultCostCenterId,
    DateOnly? PlannedPaymentDate,
    string? CurrencyCode,
    Guid? CounterpartyId,
    Guid? ContractId,
    string? Purpose,
    string? Comment,
    string? BusinessJustification,
    bool IsUrgent,
    string? UrgencyReason,
    bool IsNewCounterparty,
    bool IsNewContract,
    IReadOnlyList<CreateExpenseRequestLineCommand>? Lines,
    PaymentBasisType? PaymentBasisCode = null,
    string? InvoiceNumber = null,
    DateOnly? InvoiceDate = null,
    string? PublicOfferName = null,
    string? PublicOfferIdentifier = null,
    string? OtherBasisExplanation = null,
    bool SupportingDocumentMissing = false,
    string? SupportingDocumentMissingExplanation = null,
    string? CandidateCounterpartyName = null,
    CounterpartyResidency? CandidateResidencyCode = null,
    string? CandidateBinIin = null,
    string? CandidateCountry = null,
    string? CandidateForeignRegistrationNumber = null,
    bool CandidateRegistrationNumberMissing = false,
    string? CandidateRegistrationNumberMissingExplanation = null,
    string? CandidateContractNumber = null,
    bool CandidateContractNumberMissing = false,
    DateOnly? CandidateContractDate = null,
    string? CandidateContractSubject = null,
    string? CandidateContractTerms = null,
    bool NewContractDocumentMissing = false,
    string? NewContractDocumentMissingExplanation = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? NumberLeaseId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? AttachmentUploadSessionId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ExpensePaymentOperation? PaymentOperation = null);

public sealed record UpdateExpenseRequestCommand(
    Guid ExpectedVersion,
    Guid? OrganizationId,
    Guid? DefaultCostCenterId,
    DateOnly? PlannedPaymentDate,
    string? CurrencyCode,
    Guid? CounterpartyId,
    Guid? ContractId,
    string? Purpose,
    string? Comment,
    string? BusinessJustification,
    bool IsUrgent,
    string? UrgencyReason,
    bool IsNewCounterparty,
    bool IsNewContract,
    IReadOnlyList<UpdateExpenseRequestLineCommand>? Lines,
    PaymentBasisType? PaymentBasisCode = null,
    string? InvoiceNumber = null,
    DateOnly? InvoiceDate = null,
    string? PublicOfferName = null,
    string? PublicOfferIdentifier = null,
    string? OtherBasisExplanation = null,
    bool SupportingDocumentMissing = false,
    string? SupportingDocumentMissingExplanation = null,
    string? CandidateCounterpartyName = null,
    CounterpartyResidency? CandidateResidencyCode = null,
    string? CandidateBinIin = null,
    string? CandidateCountry = null,
    string? CandidateForeignRegistrationNumber = null,
    bool CandidateRegistrationNumberMissing = false,
    string? CandidateRegistrationNumberMissingExplanation = null,
    string? CandidateContractNumber = null,
    bool CandidateContractNumberMissing = false,
    DateOnly? CandidateContractDate = null,
    string? CandidateContractSubject = null,
    string? CandidateContractTerms = null,
    bool NewContractDocumentMissing = false,
    string? NewContractDocumentMissingExplanation = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ExpensePaymentOperation? PaymentOperation = null);

public sealed record ExpenseRequestLineDto(
    Guid Id,
    Guid? CashFlowItemId,
    Guid? CostCenterId,
    DateOnly? AccrualPeriod,
    decimal Amount,
    ExpenseVatRate? VatRateCode,
    decimal? VatAmount,
    decimal? AmountExcludingVat,
    decimal? AmountKzt,
    decimal? VatAmountKzt,
    decimal? AmountExcludingVatKzt,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? AccrualCostCenterId = null);

public sealed record BudgetReservationPositionDto(
    Guid ExpenseRequestLineId,
    Guid CostCenterId,
    Guid CashFlowItemId,
    DateOnly BudgetMonth,
    decimal ReservedAmountKzt);

public sealed record BudgetReservationDto(
    bool IsComplete,
    decimal ReservedAmountKzt,
    IReadOnlyList<string> MissingRequirements,
    IReadOnlyList<BudgetReservationPositionDto> Positions);

public sealed record SubmissionReadinessDto(
    bool IsReady,
    IReadOnlyList<string> MissingRequirements);

public sealed record ExpenseRequestEditAuthorityDto(bool CanEdit);

public sealed record ExpenseRequestDetailDto(
    ExpenseRequestDto ExpenseRequest,
    ExpenseRequestEditAuthorityDto EditAuthority,
    string? InitiatorDisplayName = null);

public sealed record ExpenseRequestDto(
    Guid Id,
    string Status,
    string InitiatorExternalId,
    Guid? OrganizationId,
    Guid? DefaultCostCenterId,
    DateOnly? PlannedPaymentDate,
    string CurrencyCode,
    DateOnly? CurrencyRateDate,
    decimal? CurrencyRate,
    decimal? CurrencyMultiplicity,
    Guid? CounterpartyId,
    Guid? ContractId,
    string? Purpose,
    string? Comment,
    string? BusinessJustification,
    bool IsUrgent,
    string? UrgencyReason,
    bool IsNewCounterparty,
    bool IsNewContract,
    PaymentBasisType? PaymentBasisCode,
    string? InvoiceNumber,
    DateOnly? InvoiceDate,
    string? PublicOfferName,
    string? PublicOfferIdentifier,
    string? OtherBasisExplanation,
    bool SupportingDocumentMissing,
    string? SupportingDocumentMissingExplanation,
    string? CandidateCounterpartyName,
    CounterpartyResidency? CandidateResidencyCode,
    string? CandidateBinIin,
    string? CandidateCountry,
    string? CandidateForeignRegistrationNumber,
    bool CandidateRegistrationNumberMissing,
    string? CandidateRegistrationNumberMissingExplanation,
    string? CandidateContractNumber,
    bool CandidateContractNumberMissing,
    DateOnly? CandidateContractDate,
    string? CandidateContractSubject,
    string? CandidateContractTerms,
    bool NewContractDocumentMissing,
    string? NewContractDocumentMissingExplanation,
    decimal TotalAmount,
    decimal? TotalAmountKzt,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid ConcurrencyStamp,
    IReadOnlyList<ExpenseRequestLineDto> Lines,
    BudgetReservationDto BudgetReservation,
    SubmissionReadinessDto SubmissionReadiness,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? RequestNumber = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ExpensePaymentOperation? PaymentOperation = null)
{
    public static ExpenseRequestDto FromDomain(
        ExpenseRequest request,
        IReadOnlyCollection<BudgetRequestProjection>? persistedProjections = null,
        SubmissionAttachmentFacts? attachmentFacts = null)
    {
        var completeness = ExpenseRequestBudgetProjectionCalculator.Calculate(request);
        var submissionReadiness = SubmissionReadinessEvaluator.Evaluate(request, attachmentFacts ?? SubmissionAttachmentFacts.None);
        var projections = persistedProjections ?? [];
        return new ExpenseRequestDto(
            request.Id,
            request.Status.ToCode(),
            request.InitiatorExternalId,
            request.OrganizationId,
            request.DefaultCostCenterId,
            request.PlannedPaymentDate,
            request.CurrencyCode,
            request.CurrencyRateDate,
            request.CurrencyRate,
            request.CurrencyMultiplicity,
            request.CounterpartyId,
            request.ContractId,
            request.Purpose,
            request.Comment,
            request.BusinessJustification,
            request.IsUrgent,
            request.UrgencyReason,
            request.IsNewCounterparty,
            request.IsNewContract,
            request.PaymentBasisCode,
            request.InvoiceNumber,
            request.InvoiceDate,
            request.PublicOfferName,
            request.PublicOfferIdentifier,
            request.OtherBasisExplanation,
            request.SupportingDocumentMissing,
            request.SupportingDocumentMissingExplanation,
            request.CandidateCounterpartyName,
            request.CandidateResidencyCode,
            request.CandidateBinIin,
            request.CandidateCountry,
            request.CandidateForeignRegistrationNumber,
            request.CandidateRegistrationNumberMissing,
            request.CandidateRegistrationNumberMissingExplanation,
            request.CandidateContractNumber,
            request.CandidateContractNumberMissing,
            request.CandidateContractDate,
            request.CandidateContractSubject,
            request.CandidateContractTerms,
            request.NewContractDocumentMissing,
            request.NewContractDocumentMissingExplanation,
            request.TotalAmount,
            request.TotalAmountKzt,
            request.CreatedAtUtc,
            request.UpdatedAtUtc,
            request.ConcurrencyStamp,
            request.Lines.Select(line => new ExpenseRequestLineDto(
                line.Id,
                line.CashFlowItemId,
                line.CostCenterId,
                line.AccrualPeriod,
                line.Amount,
                line.VatRateCode,
                line.VatAmount,
                line.AmountExcludingVat,
                line.AmountKzt,
                line.VatAmountKzt,
                line.AmountExcludingVatKzt, line.AccrualCostCenterId)).ToArray(),
            new BudgetReservationDto(
                completeness.IsComplete,
                request.PaymentOperation?.IsPayrollAdvance == true ? 0m : projections.Sum(projection => projection.ReservedAmountKzt),
                completeness.MissingRequirements,
                (request.PaymentOperation?.IsPayrollAdvance == true ? [] : projections).Select(projection => new BudgetReservationPositionDto(
                    projection.ExpenseRequestLineId,
                    projection.CostCenterId,
                    projection.CashFlowItemId,
                    projection.BudgetMonth,
                    projection.ReservedAmountKzt)).ToArray()),
            new SubmissionReadinessDto(
                submissionReadiness.IsReady,
                submissionReadiness.MissingRequirements),
            request.RequestNumber, request.PaymentOperation);
    }
}

public sealed record ExpenseRequestPageDto(
    IReadOnlyList<ExpenseRequestListItemDto> Items,
    int Skip,
    int Take,
    int Total)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? BusinessColumnsIncluded { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? BudgetIndicatorsUnavailable { get; init; }
}

public sealed record ExpenseRequestListItemDto(
    Guid Id,
    string Status,
    string InitiatorExternalId,
    Guid? OrganizationId,
    Guid? DefaultCostCenterId,
    DateOnly? PlannedPaymentDate,
    string CurrencyCode,
    Guid? CounterpartyId,
    string? Purpose,
    bool IsUrgent,
    bool IsNewCounterparty,
    string? CandidateCounterpartyName,
    decimal TotalAmount,
    decimal? TotalAmountKzt,
    DateTimeOffset CreatedAtUtc,
    string? CurrentStage,
    bool? IsOverBudget,
    bool CanOpen,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? RequestNumber = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? RequestDateUtc { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? InitiatorDisplayName { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ExpenseRequestListBusinessColumnsDto? BusinessColumns { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? EmployeeNames { get; init; }

    public static ExpenseRequestListItemDto FromDomain(
        ExpenseRequest request,
        bool? isOverBudget,
        bool canOpen,
        Bms.Domain.Workflow.ApprovalRouteSnapshot? activeSnapshot = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new ExpenseRequestListItemDto(
            request.Id,
            request.Status.ToCode(),
            request.InitiatorExternalId,
            request.OrganizationId,
            request.DefaultCostCenterId,
            request.PlannedPaymentDate,
            request.CurrencyCode,
            request.CounterpartyId,
            request.Purpose,
            request.IsUrgent,
            request.IsNewCounterparty,
            request.CandidateCounterpartyName,
            request.TotalAmount,
            request.TotalAmountKzt,
            request.CreatedAtUtc,
            ExpenseRequestCurrentStage.Resolve(request, activeSnapshot),
            request.PaymentOperation?.IsPayrollAdvance == true ? null : isOverBudget,
            canOpen,
            request.RequestNumber)
        {
            EmployeeNames = canOpen && request.PaymentOperation is { IsBusinessTrip: true } trip
                ? trip.EmployeeNames?.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray() ?? [] : null
        };
    }
}

public sealed record CreateExpenseRequestResult(
    ExpenseRequestDto ExpenseRequest,
    bool IsReplay,
    int ResponseStatusCode);

public sealed record UpdateExpenseRequestResult(
    ExpenseRequestDto ExpenseRequest,
    bool IsReplay,
    int ResponseStatusCode);

public sealed record CopyExpenseRequestResult(
    ExpenseRequestDto ExpenseRequest,
    bool IsReplay,
    int ResponseStatusCode);
