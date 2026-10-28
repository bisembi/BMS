using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// Building the canonical representation: a normalised draft plus the
/// canonical line records. Line order is decided here and nowhere else.
internal static partial class ExpenseRequestDraftFingerprint
{

    private static CanonicalExpenseRequestFingerprint BuildCanonical(
        string operation,
        Guid? requestId,
        Guid? expectedVersion,
        ExpenseRequest normalized,
        IReadOnlyList<CanonicalLine> lines)
    {
        var canonicalBuffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(canonicalBuffer, CanonicalWriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", Schema);
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("policyVersion", PolicyVersion);
            writer.WriteString("operation", operation);
            WriteGuid(writer, "requestId", requestId);
            WriteGuid(writer, "expectedVersion", expectedVersion);
            WriteDraft(writer, normalized);
            WriteLines(writer, lines);
            writer.WriteEndObject();
        }

        var canonicalBytes = canonicalBuffer.WrittenSpan;
        return new CanonicalExpenseRequestFingerprint(
            ComputeHash(canonicalBytes),
            Encoding.UTF8.GetString(canonicalBytes));
    }

    private static ExpenseRequest CreateNormalizedDraft(
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
        ExpenseRequestEvidenceDraft evidence) => ExpenseRequest.CreateDraft(
            FingerprintRequestId,
            FingerprintActor,
            DateTimeOffset.UnixEpoch,
            organizationId,
            defaultCostCenterId,
            plannedPaymentDate,
            currencyCode,
            counterpartyId,
            contractId,
            purpose,
            comment,
            businessJustification,
            isUrgent,
            urgencyReason,
            isNewCounterparty,
            isNewContract,
            lines,
            evidence);

    private sealed record CanonicalLine(
        Guid? Id,
        Guid? CashFlowItemId,
        Guid? CostCenterId,
        DateOnly? AccrualPeriod,
        decimal Amount,
        ExpenseVatRate? VatRateCode,
        Guid? AccrualCostCenterId);

    private sealed record IndexedCanonicalLine(int InputIndex, CanonicalLine Line);
}
