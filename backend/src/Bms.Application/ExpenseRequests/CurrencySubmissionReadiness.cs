using Bms.Application.Abstractions;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// <summary>Only new submission is checked; historical approved conversion snapshots stay immutable.</summary>
public sealed class CurrencySubmissionReadiness(ICurrencyRateUsagePolicy policy, ICurrencyRateResolver resolver)
{
    public async Task<bool> IsCurrentAsync(ExpenseRequest request, CancellationToken token)
    {
        if (request.CurrencyCode == "KZT") return true;
        var context = policy.GetCurrentContext();
        if (context is null || request.CurrencyRateDate is not { } date || date > context.BusinessDate) return false;
        // Submission must still match the latest loaded quote exactly. Yesterday's
        // quote is valid if it remains latest; a newer quote requires an explicit save.
        var rate = await resolver.ResolveAsync(request.CurrencyCode, context.BusinessDate, token);
        return rate.IsResolved && rate.Quote is { } quote && quote.CurrencyCode == request.CurrencyCode
            && quote.RateDate == date && quote.Rate == request.CurrencyRate && quote.Multiplicity == request.CurrencyMultiplicity;
    }
}
