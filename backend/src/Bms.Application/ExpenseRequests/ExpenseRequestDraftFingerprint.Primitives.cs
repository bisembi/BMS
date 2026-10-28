using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// The strict primitive encodings behind the canonical JSON: lowercase
/// dashed GUIDs, invariant decimals and SHA-256.
internal static partial class ExpenseRequestDraftFingerprint
{

    private static string FormatGuid(Guid value) => value.ToString("D").ToLowerInvariant();

    private static string FormatDecimal(decimal value) => value.ToString(
        "0.#############################",
        CultureInfo.InvariantCulture);

    private static string ComputeHash(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value));
}
