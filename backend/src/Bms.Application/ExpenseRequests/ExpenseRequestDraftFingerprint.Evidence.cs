using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// Mapping a create or update command onto the evidence draft used while
/// normalising. The two overloads stay side by side so a field added to one
/// and forgotten in the other is visible in a single screen.
internal static partial class ExpenseRequestDraftFingerprint
{

    private static ExpenseRequestEvidenceDraft ToEvidence(
        CreateExpenseRequestCommand command) => new(
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

    private static ExpenseRequestEvidenceDraft ToEvidence(
        UpdateExpenseRequestCommand command) => new(
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

