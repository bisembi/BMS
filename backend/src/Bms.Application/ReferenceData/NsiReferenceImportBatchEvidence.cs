using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Bms.Application.ReferenceData;

/// Canonical, transport-neutral evidence for exactly the supplied import batch.
/// It does not claim that the batch is a complete source snapshot or a watermark.
internal sealed class NsiReferenceImportBatchEvidence
{
    private const string CanonicalVersion = "bms-nsi-reference-batch-v1";

    private NsiReferenceImportBatchEvidence(
        string canonicalSha256,
        int counterpartyCount,
        int activeCounterpartyCount,
        int contractCount,
        int activeContractCount)
    {
        CanonicalSha256 = canonicalSha256;
        CounterpartyCount = counterpartyCount;
        ActiveCounterpartyCount = activeCounterpartyCount;
        ContractCount = contractCount;
        ActiveContractCount = activeContractCount;
    }

    internal string CanonicalSha256 { get; }

    internal int CounterpartyCount { get; }

    internal int ActiveCounterpartyCount { get; }

    internal int ContractCount { get; }

    internal int ActiveContractCount { get; }

    internal static NsiReferenceImportBatchEvidence Capture(NsiReferenceImportBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, CanonicalVersion);

        var counterparties = batch.Counterparties
            .OrderBy(item => item.Id.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        AppendText(hash, "counterparties");
        AppendInt32(hash, counterparties.Length);
        foreach (var item in counterparties)
        {
            AppendGuid(hash, item.Id);
            AppendText(hash, item.Code);
            AppendText(hash, item.Name);
            AppendBoolean(hash, item.IsActive);
        }

        var contracts = batch.Contracts
            .OrderBy(item => item.Id.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        AppendText(hash, "contracts");
        AppendInt32(hash, contracts.Length);
        foreach (var item in contracts)
        {
            AppendGuid(hash, item.Id);
            AppendGuid(hash, item.CounterpartyId);
            AppendGuid(hash, item.OrganizationId);
            AppendText(hash, item.Code);
            AppendText(hash, item.Name);
            AppendText(hash, item.ContractDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            AppendGuid(hash, item.CurrencyId);
            AppendBoolean(hash, item.IsActive);
        }

        return new NsiReferenceImportBatchEvidence(
            Convert.ToHexString(hash.GetHashAndReset()),
            counterparties.Length,
            counterparties.Count(item => item.IsActive),
            contracts.Length,
            contracts.Count(item => item.IsActive));
    }

    private static void AppendGuid(IncrementalHash hash, Guid value) =>
        AppendText(hash, value.ToString("D"));

    private static void AppendText(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        hash.AppendData(buffer);
    }

    private static void AppendBoolean(IncrementalHash hash, bool value)
    {
        Span<byte> buffer = stackalloc byte[1];
        buffer[0] = value ? (byte)1 : (byte)0;
        hash.AppendData(buffer);
    }
}
