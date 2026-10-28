using System.Data;
using System.Globalization;
using Bms.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

/// <summary>Shared by both creation routes; the unique code index remains the final guard.</summary>
internal static class ExpenseItemCodeAllocator
{
    internal static async Task<IReadOnlyList<string>> LockCodesAsync(BmsDbContext db, CancellationToken token)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Expense-item numbering requires a Serializable transaction.");
        // Update range locks cover even an empty catalogue and survive until commit.
        // Maintenance writers take the same lock before their duplicate lookup.
        return await db.ExpenseItems.FromSqlRaw("""
            SELECT [item].* FROM [bms_web].[ExpenseItems] AS [item]
                WITH (UPDLOCK, HOLDLOCK, INDEX([UQ_bms_web_ExpenseItems_Code]))
            """).AsNoTracking().Select(item => item.Code).ToListAsync(token);
    }

    internal static string Next(IEnumerable<string> codes)
    {
        var maximum = 0;
        foreach (var code in codes)
        {
            if (code.Length <= 3 || !code.StartsWith("PL-", StringComparison.OrdinalIgnoreCase)
                || code.AsSpan(3).ContainsAnyExceptInRange('0', '9')) continue;
            if (!int.TryParse(code.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                throw new DomainRuleViolationException("The expense-item number range is exhausted.");
            maximum = Math.Max(maximum, number);
        }
        // Code has a pre-existing nine-character storage contract. Never wrap.
        if (maximum >= 999999)
            throw new DomainRuleViolationException("The expense-item number range is exhausted.");
        return "PL-" + (maximum + 1).ToString("D3", CultureInfo.InvariantCulture);
    }

    internal static async Task<string> NextAsync(BmsDbContext db, CancellationToken token)
    {
        var codes = await LockCodesAsync(db, token);
        return Next(codes.Concat(db.ExpenseItems.Local.Select(item => item.Code)));
    }
}
