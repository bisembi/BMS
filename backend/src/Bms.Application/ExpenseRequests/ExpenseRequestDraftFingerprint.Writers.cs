using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// The canonical JSON writer. Property names, their order and the encoding of
/// every value type are the contract: a change here changes every stored
/// fingerprint, so nothing may be reordered or reformatted.
internal static partial class ExpenseRequestDraftFingerprint
{

    private static void WriteDraft(Utf8JsonWriter writer, ExpenseRequest draft)
    {
        writer.WritePropertyName("draft");
        writer.WriteStartObject();
        WriteGuid(writer, "organizationId", draft.OrganizationId);
        WriteGuid(writer, "defaultCostCenterId", draft.DefaultCostCenterId);
        WriteDate(writer, "plannedPaymentDate", draft.PlannedPaymentDate);
        writer.WriteString("currencyCode", draft.CurrencyCode);
        WriteGuid(writer, "counterpartyId", draft.CounterpartyId);
        WriteGuid(writer, "contractId", draft.ContractId);
        WriteString(writer, "purpose", draft.Purpose);
        WriteString(writer, "comment", draft.Comment);
        WriteString(writer, "businessJustification", draft.BusinessJustification);
        writer.WriteBoolean("isUrgent", draft.IsUrgent);
        WriteString(writer, "urgencyReason", draft.UrgencyReason);
        writer.WriteBoolean("isNewCounterparty", draft.IsNewCounterparty);
        writer.WriteBoolean("isNewContract", draft.IsNewContract);
        WritePaymentBasis(writer, "paymentBasisCode", draft.PaymentBasisCode);
        WriteString(writer, "invoiceNumber", draft.InvoiceNumber);
        WriteDate(writer, "invoiceDate", draft.InvoiceDate);
        WriteString(writer, "publicOfferName", draft.PublicOfferName);
        WriteString(writer, "publicOfferIdentifier", draft.PublicOfferIdentifier);
        WriteString(writer, "otherBasisExplanation", draft.OtherBasisExplanation);
        writer.WriteBoolean("supportingDocumentMissing", draft.SupportingDocumentMissing);
        WriteString(
            writer,
            "supportingDocumentMissingExplanation",
            draft.SupportingDocumentMissingExplanation);
        WriteString(writer, "candidateCounterpartyName", draft.CandidateCounterpartyName);
        WriteResidency(writer, "candidateResidencyCode", draft.CandidateResidencyCode);
        WriteString(writer, "candidateBinIin", draft.CandidateBinIin);
        WriteString(writer, "candidateCountry", draft.CandidateCountry);
        WriteString(
            writer,
            "candidateForeignRegistrationNumber",
            draft.CandidateForeignRegistrationNumber);
        writer.WriteBoolean(
            "candidateRegistrationNumberMissing",
            draft.CandidateRegistrationNumberMissing);
        WriteString(
            writer,
            "candidateRegistrationNumberMissingExplanation",
            draft.CandidateRegistrationNumberMissingExplanation);
        WriteString(writer, "candidateContractNumber", draft.CandidateContractNumber);
        writer.WriteBoolean("candidateContractNumberMissing", draft.CandidateContractNumberMissing);
        WriteDate(writer, "candidateContractDate", draft.CandidateContractDate);
        WriteString(writer, "candidateContractSubject", draft.CandidateContractSubject);
        WriteString(writer, "candidateContractTerms", draft.CandidateContractTerms);
        writer.WriteBoolean("newContractDocumentMissing", draft.NewContractDocumentMissing);
        WriteString(
            writer,
            "newContractDocumentMissingExplanation",
            draft.NewContractDocumentMissingExplanation);
        // Absence is byte-compatible with every existing supplier fingerprint.
        if (draft.PaymentOperation is not null)
        {
            writer.WritePropertyName("paymentOperation");
            writer.WriteRawValue(ExpensePaymentOperation.Serialize(draft.PaymentOperation));
        }
        writer.WriteEndObject();
    }

    private static void WriteLines(
        Utf8JsonWriter writer,
        IReadOnlyList<CanonicalLine> lines)
    {
        writer.WritePropertyName("lines");
        writer.WriteStartArray();
        foreach (var line in lines)
        {
            writer.WriteStartObject();
            WriteGuid(writer, "id", line.Id);
            WriteGuid(writer, "cashFlowItemId", line.CashFlowItemId);
            WriteGuid(writer, "costCenterId", line.CostCenterId);
            WriteDate(writer, "accrualPeriod", line.AccrualPeriod);
            writer.WriteString("amount", FormatDecimal(line.Amount));
            WriteVatRate(writer, "vatRateCode", line.VatRateCode);
            // Preserve the canonical bytes of legacy commands without this optional dimension.
            if (line.AccrualCostCenterId.HasValue)
                WriteGuid(writer, "accrualCostCenterId", line.AccrualCostCenterId);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteString(name, value);
    }

    private static void WriteGuid(Utf8JsonWriter writer, string name, Guid? value)
    {
        if (!value.HasValue)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteString(name, FormatGuid(value.Value));
    }

    private static void WriteDate(Utf8JsonWriter writer, string name, DateOnly? value)
    {
        if (!value.HasValue)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteString(
            name,
            value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static void WritePaymentBasis(
        Utf8JsonWriter writer,
        string name,
        PaymentBasisType? value)
    {
        if (!value.HasValue)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteString(name, value.Value switch
        {
            PaymentBasisType.Contract => "Contract",
            PaymentBasisType.InvoiceWithoutContract => "InvoiceWithoutContract",
            PaymentBasisType.PublicOffer => "PublicOffer",
            PaymentBasisType.OtherBasis => "OtherBasis",
            _ => throw new InvalidOperationException("Unsupported payment basis code.")
        });
    }

    private static void WriteResidency(
        Utf8JsonWriter writer,
        string name,
        CounterpartyResidency? value)
    {
        if (!value.HasValue)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteString(name, value.Value switch
        {
            CounterpartyResidency.ResidentKz => "ResidentKz",
            CounterpartyResidency.NonResident => "NonResident",
            _ => throw new InvalidOperationException("Unsupported counterparty residency code.")
        });
    }

    private static void WriteVatRate(
        Utf8JsonWriter writer,
        string name,
        ExpenseVatRate? value)
    {
        if (!value.HasValue)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteString(name, value.Value switch
        {
            ExpenseVatRate.WithoutVat => "WithoutVat",
            ExpenseVatRate.Vat16 => "Vat16",
            _ => throw new InvalidOperationException("Unsupported VAT rate code.")
        });
    }
}
