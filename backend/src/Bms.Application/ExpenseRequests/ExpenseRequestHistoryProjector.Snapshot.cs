using System.Globalization;
using System.Text.Json;
using Bms.Application.Abstractions;

namespace Bms.Application.ExpenseRequests;

/// The shape the audit snapshot JSON is deserialized into.
internal static partial class ExpenseRequestHistoryProjector
{

    private sealed class AuditSnapshot
    {
        public string? Id { get; init; }
        public string? Status { get; init; }
        public Bms.Domain.ExpenseRequests.ExpensePaymentOperation? PaymentOperation { get; init; }
        public string? InitiatorExternalId { get; init; }
        public string? OrganizationId { get; init; }
        public string? DefaultCostCenterId { get; init; }
        public DateOnly? PlannedPaymentDate { get; init; }
        public string? CurrencyCode { get; init; }
        public string? CounterpartyId { get; init; }
        public string? ContractId { get; init; }
        public string? Purpose { get; init; }
        public string? Comment { get; init; }
        public string? BusinessJustification { get; init; }
        public bool? IsUrgent { get; init; }
        public string? UrgencyReason { get; init; }
        public bool? IsNewCounterparty { get; init; }
        public bool? IsNewContract { get; init; }
        public string? PaymentBasisCode { get; init; }
        public string? InvoiceNumber { get; init; }
        public DateOnly? InvoiceDate { get; init; }
        public string? PublicOfferName { get; init; }
        public string? PublicOfferIdentifier { get; init; }
        public string? OtherBasisExplanation { get; init; }
        public bool? SupportingDocumentMissing { get; init; }
        public string? SupportingDocumentMissingExplanation { get; init; }
        public string? CandidateCounterpartyName { get; init; }
        public string? CandidateResidencyCode { get; init; }
        public string? CandidateBinIin { get; init; }
        public string? CandidateCountry { get; init; }
        public string? CandidateForeignRegistrationNumber { get; init; }
        public bool? CandidateRegistrationNumberMissing { get; init; }
        public string? CandidateRegistrationNumberMissingExplanation { get; init; }
        public string? CandidateContractNumber { get; init; }
        public bool? CandidateContractNumberMissing { get; init; }
        public DateOnly? CandidateContractDate { get; init; }
        public string? CandidateContractSubject { get; init; }
        public string? CandidateContractTerms { get; init; }
        public bool? NewContractDocumentMissing { get; init; }
        public string? NewContractDocumentMissingExplanation { get; init; }
        public decimal? TotalAmount { get; init; }
        public AuditBudgetReservationSnapshot? BudgetReservation { get; init; }
        public IReadOnlyList<AuditLineSnapshot>? Lines { get; init; }
    }

    private sealed class AuditBudgetReservationSnapshot
    {
        public decimal? ReservedAmountKzt { get; init; }
    }

    private sealed class AuditLineSnapshot
    {
        public string? Id { get; init; }
        public string? CashFlowItemId { get; init; }
        public string? CostCenterId { get; init; }
        public string? AccrualCostCenterId { get; init; }
        public DateOnly? AccrualPeriod { get; init; }
        public decimal? Amount { get; init; }
        public string? VatRateCode { get; init; }
        public decimal? VatAmount { get; init; }
        public decimal? AmountExcludingVat { get; init; }
        public decimal? AmountKzt { get; init; }
    }
}
