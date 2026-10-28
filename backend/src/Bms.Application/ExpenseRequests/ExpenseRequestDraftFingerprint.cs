using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// The fingerprint of a draft command.
///
/// Two representations coexist and both must keep working: the legacy hash of
/// the serialized command, and canonical V1, whose JSON is byte-stable. The
/// canonical writer, the normalisation, the primitives and the evidence
/// mapping live in the sibling partial files.

internal sealed record CanonicalExpenseRequestFingerprint(string Hash, string Json);

internal static partial class ExpenseRequestDraftFingerprint
{
    private const string Schema = "bms.expense-request-draft-command";
    private const int SchemaVersion = 1;
    private const string PolicyVersion = "draft-normalization-v1";
    private const string CreateOperation = "create-draft";
    private const string UpdateOperation = "update-draft";
    private const string FingerprintActor = "canonical-fingerprint";
    private static readonly Guid FingerprintRequestId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly JsonSerializerOptions LegacyJsonOptions =
        new(JsonSerializerDefaults.Web);
    private static readonly JsonWriterOptions CanonicalWriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
        SkipValidation = false
    };

    internal static string ComputeLegacyHash<TCommand>(TCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ComputeHash(JsonSerializer.SerializeToUtf8Bytes(command, LegacyJsonOptions));
    }

    internal static bool TryComputeCanonicalV1(
        CreateExpenseRequestCommand command,
        out CanonicalExpenseRequestFingerprint? fingerprint)
    {
        try
        {
            fingerprint = ComputeCanonicalV1(command);
            return true;
        }
        catch (DomainRuleViolationException)
        {
            fingerprint = null;
            return false;
        }
        catch (ArgumentException)
        {
            fingerprint = null;
            return false;
        }
        catch (InvalidOperationException)
        {
            fingerprint = null;
            return false;
        }
    }

    internal static bool TryComputeCanonicalV1(
        Guid requestId,
        UpdateExpenseRequestCommand command,
        out CanonicalExpenseRequestFingerprint? fingerprint)
    {
        try
        {
            fingerprint = ComputeCanonicalV1(requestId, command);
            return true;
        }
        catch (DomainRuleViolationException)
        {
            fingerprint = null;
            return false;
        }
        catch (ArgumentException)
        {
            fingerprint = null;
            return false;
        }
        catch (InvalidOperationException)
        {
            fingerprint = null;
            return false;
        }
    }

    internal static CanonicalExpenseRequestFingerprint ComputeCanonicalV1(
        CreateExpenseRequestCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalized = CreateNormalizedDraft(
            command.OrganizationId,
            command.DefaultCostCenterId,
            command.PlannedPaymentDate,
            command.CurrencyCode,
            command.CounterpartyId,
            command.ContractId,
            command.Purpose,
            command.Comment,
            command.BusinessJustification,
            command.IsUrgent,
            command.UrgencyReason,
            command.IsNewCounterparty,
            command.IsNewContract,
            command.Lines?.Select(line => new ExpenseRequestLineDraft(
                line.CashFlowItemId,
                line.CostCenterId,
                line.AccrualPeriod,
                line.Amount,
                Id: null,
                line.VatRateCode, line.AccrualCostCenterId)),
            ToEvidence(command));

        var lines = normalized.Lines
            .Select(line => new CanonicalLine(
                Id: null,
                line.CashFlowItemId,
                line.CostCenterId,
                line.AccrualPeriod,
                line.Amount,
                line.VatRateCode, line.AccrualCostCenterId))
            .ToArray();

        return BuildCanonical(
            CreateOperation,
            requestId: null,
            expectedVersion: null,
            normalized,
            lines);
    }

    internal static CanonicalExpenseRequestFingerprint ComputeCanonicalV1(
        Guid requestId,
        UpdateExpenseRequestCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sourceLines = command.Lines ?? [];
        var normalized = CreateNormalizedDraft(
            command.OrganizationId,
            command.DefaultCostCenterId,
            command.PlannedPaymentDate,
            command.CurrencyCode,
            command.CounterpartyId,
            command.ContractId,
            command.Purpose,
            command.Comment,
            command.BusinessJustification,
            command.IsUrgent,
            command.UrgencyReason,
            command.IsNewCounterparty,
            command.IsNewContract,
            sourceLines.Select(line => new ExpenseRequestLineDraft(
                line.CashFlowItemId,
                line.CostCenterId,
                line.AccrualPeriod,
                line.Amount,
                Id: null,
                line.VatRateCode, line.AccrualCostCenterId)),
            ToEvidence(command));

        var normalizedLines = normalized.Lines.ToArray();
        if (normalizedLines.Length != sourceLines.Count)
        {
            throw new InvalidOperationException(
                "Canonical draft normalization changed the command line count.");
        }

        var pairedLines = sourceLines
            .Select((source, index) => new IndexedCanonicalLine(
                index,
                new CanonicalLine(
                    source.Id,
                    normalizedLines[index].CashFlowItemId,
                    normalizedLines[index].CostCenterId,
                    normalizedLines[index].AccrualPeriod,
                    normalizedLines[index].Amount,
                    normalizedLines[index].VatRateCode, normalizedLines[index].AccrualCostCenterId)))
            .ToArray();
        var lines = pairedLines
            .Where(item => item.Line.Id.HasValue)
            .OrderBy(
                item => FormatGuid(item.Line.Id!.Value),
                StringComparer.Ordinal)
            .Concat(pairedLines
                .Where(item => !item.Line.Id.HasValue)
                .OrderBy(item => item.InputIndex))
            .Select(item => item.Line)
            .ToArray();

        return BuildCanonical(
            UpdateOperation,
            requestId,
            command.ExpectedVersion,
            normalized,
            lines);
    }
}
