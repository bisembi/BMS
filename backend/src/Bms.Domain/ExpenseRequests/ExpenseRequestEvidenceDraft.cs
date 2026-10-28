namespace Bms.Domain.ExpenseRequests;

public sealed record ExpenseRequestEvidenceDraft(
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
