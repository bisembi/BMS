using System.Security.Cryptography;
using System.Text;
using Bms.Application.ReferenceData;

namespace Bms.Application.Budgeting;

// Display-only runs over the WHOLE current catalogue, before report/CFO filtering.
// An opaque run identity exposes no other CFO's article identity or caption.
public static class BddsCatalogueDisplay
{
    public static IReadOnlyDictionary<Guid, string> Blocks(IReadOnlyList<CashFlowCatalogOrderRow> rows)
    {
        var result = new Dictionary<Guid, string>();
        (string Activity, Guid? Group, bool UnknownDirection)? prior = null;
        string? block = null;
        foreach (var row in CashFlowCatalogOrderService.OrderRowsForDisplay(rows))
        {
            var family = (row.ActivityType, row.ParentId, row.FlowDirection is null);
            if (prior != family || row.ParentId is null)
                block = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    $"catalogue-run-v2|{row.ActivityType}|{row.ParentId:D}|{row.Id:D}")));
            result.Add(row.Id, block!);
            prior = family;
        }
        return result;
    }
}
