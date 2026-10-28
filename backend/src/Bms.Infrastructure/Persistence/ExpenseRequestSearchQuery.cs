using Bms.Domain.ExpenseRequests;

namespace Bms.Infrastructure.Persistence;

/// <summary>Composed after authorization and before count/paging, also for Excel.</summary>
internal static class ExpenseRequestSearchQuery
{
    public static IQueryable<ExpenseRequest> Apply(IQueryable<ExpenseRequest> query, string? search)
    {
        if (string.IsNullOrEmpty(search)) return query;
        var text = search.ToUpperInvariant();
        // EF parameterizes this value. Contains means a literal substring,
        // not a user-supplied SQL LIKE pattern.
        return query.Where(request =>
            (request.RequestNumber != null && request.RequestNumber.ToUpper().Contains(text))
            || (request.Purpose != null && request.Purpose.ToUpper().Contains(text))
            || (request.BusinessJustification != null && request.BusinessJustification.ToUpper().Contains(text)));
    }
}
