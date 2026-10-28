using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// Mapping a create or update command onto the evidence draft the domain
/// stores. The two overloads stay side by side so a field added to one and
/// forgotten in the other is visible in a single screen.
public sealed partial class ExpenseRequestService
{

    private static ExpenseRequestEvidenceDraft ToEvidence(CreateExpenseRequestCommand command) => new(
        command.PaymentBasisCode,
        command.InvoiceNumber,
        command.InvoiceDate,
        command.PublicOfferName,
        command.PublicOfferIdentifier,
        command.OtherBasisExplanation,
        command.SupportingDocumentMissing,
        command.SupportingDocumentMissingExplanation,
        command.CandidateCounterpartyName,
        command.CandidateResidencyCode,
        command.CandidateBinIin,
        command.CandidateCountry,
        command.CandidateForeignRegistrationNumber,
        command.CandidateRegistrationNumberMissing,
        command.CandidateRegistrationNumberMissingExplanation,
        command.CandidateContractNumber,
        command.CandidateContractNumberMissing,
        command.CandidateContractDate,
        command.CandidateContractSubject,
        command.CandidateContractTerms,
        command.NewContractDocumentMissing,
        command.NewContractDocumentMissingExplanation, command.PaymentOperation);

    private static ExpenseRequestEvidenceDraft ToEvidence(UpdateExpenseRequestCommand command) => new(
        command.PaymentBasisCode,
        command.InvoiceNumber,
        command.InvoiceDate,
        command.PublicOfferName,
        command.PublicOfferIdentifier,
        command.OtherBasisExplanation,
        command.SupportingDocumentMissing,
        command.SupportingDocumentMissingExplanation,
        command.CandidateCounterpartyName,
        command.CandidateResidencyCode,
        command.CandidateBinIin,
        command.CandidateCountry,
        command.CandidateForeignRegistrationNumber,
        command.CandidateRegistrationNumberMissing,
        command.CandidateRegistrationNumberMissingExplanation,
        command.CandidateContractNumber,
        command.CandidateContractNumberMissing,
        command.CandidateContractDate,
        command.CandidateContractSubject,
        command.CandidateContractTerms,
        command.NewContractDocumentMissing,
        command.NewContractDocumentMissingExplanation, command.PaymentOperation);
}

