using Bms.Application.Treasury;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class GoodsCalendarArticles458(BmsDbContext db) : IGoodsCalendarArticles458
{
    public async Task<IReadOnlyDictionary<Guid, string>> ReadAsync(CancellationToken token)
    {
        // Stable catalog identity, not a fuzzy match on mutable display names.
        // Historical inactive articles must still classify existing payments.
        var rows = await db.CashFlowItems.AsNoTracking().Where(i => !i.IsGroup &&
            (i.Code == "CF-0066" || i.Code == "CF-0064" || i.Code == "CF-0063"))
            .Select(i => new { i.Id, i.Code }).Take(4).ToArrayAsync(token);
        if (rows.Length != 3 || rows.Select(i => i.Code).Distinct(StringComparer.Ordinal).Count() != 3)
            throw new PaymentCalendarUnavailableException();
        return rows.ToDictionary(i => i.Id, i => i.Code switch
        { "CF-0066" => "PREPAYMENT", "CF-0064" => "CONSIGNMENT", "CF-0063" => "REALIZATION", _ => throw new PaymentCalendarUnavailableException() });
    }
}
