using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Bms.Domain.Common;

namespace Bms.Application.Budgeting;

/// <summary>
/// Canonical, transport-neutral evidence for exactly one explicit budget-limit
/// patch. It does not claim source completeness, authority, freshness or a
/// watermark.
/// </summary>
internal sealed class BudgetLimitImportBatchEvidence
{
    private const string CanonicalVersion = "bms-budget-limit-patch-v1";

    private BudgetLimitImportBatchEvidence(
        string canonicalSha256,
        int itemCount,
        int limitValueCount,
        int nullLimitCount,
        decimal explicitLimitTotalKzt)
    {
        CanonicalSha256 = canonicalSha256;
        ItemCount = itemCount;
        LimitValueCount = limitValueCount;
        NullLimitCount = nullLimitCount;
        ExplicitLimitTotalKzt = explicitLimitTotalKzt;
    }

    internal string CanonicalSha256 { get; }

    internal int ItemCount { get; }

    internal int LimitValueCount { get; }

    internal int NullLimitCount { get; }

    internal decimal ExplicitLimitTotalKzt { get; }

    internal static BudgetLimitImportBatchEvidence Capture(BudgetLimitImportBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, CanonicalVersion);
        AppendText(hash, "items");
        AppendInt32(hash, batch.Items.Count);

        var limitValueCount = 0;
        var nullLimitCount = 0;
        var explicitLimitTotalKzt = 0m;
        foreach (var item in batch.Items)
        {
            AppendGuid(hash, item.Key.CostCenterId);
            AppendGuid(hash, item.Key.CashFlowItemId);
            AppendText(
                hash,
                item.Key.BudgetMonth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            AppendNullableMoney(hash, item.LimitAmountKzt);

            if (item.LimitAmountKzt is decimal limit)
            {
                limitValueCount++;
                explicitLimitTotalKzt = AddTotal(explicitLimitTotalKzt, limit);
            }
            else
            {
                nullLimitCount++;
            }
        }

        return new BudgetLimitImportBatchEvidence(
            Convert.ToHexString(hash.GetHashAndReset()),
            batch.Items.Count,
            limitValueCount,
            nullLimitCount,
            explicitLimitTotalKzt);
    }

    private static decimal AddTotal(decimal total, decimal value)
    {
        try
        {
            return checked(total + value);
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException(
                "Budget-limit import totals exceed the supported aggregate range.");
        }
    }

    private static void AppendNullableMoney(IncrementalHash hash, decimal? value)
    {
        Span<byte> marker = stackalloc byte[1];
        marker[0] = value.HasValue ? (byte)1 : (byte)0;
        hash.AppendData(marker);
        if (value.HasValue)
        {
            AppendText(hash, value.Value.ToString("0.00", CultureInfo.InvariantCulture));
        }
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
}
