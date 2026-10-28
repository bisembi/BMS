using Bms.Domain.ExpenseRequests;
using Bms.Application.Authorization;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Active accruals use the shared canonical CFO visibility policy. Technical
/// administration and authorship do not bypass it. Keep the predicate in SQL
/// before any totals,
/// payment-evidence capture, paging or export is evaluated.
/// </summary>
internal static class ActiveAccrualVisibilityQuery
{
    // The caller resolves this immutable authority inside its Serializable
    // capture transaction. Do not cache it across reads or use it for writes.
    internal static IQueryable<ExpenseRequest> Apply(
        BmsDbContext dbContext, IQueryable<ExpenseRequest> source, BusinessDataReadScope capturedScope)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(capturedScope);
        if (capturedScope.CanReadAll) return source;
        var owned = capturedScope.CostCenterIds.ToArray();
        if (owned.Length == 0) return source.Where(_ => false);
        return source.Where(request => request.DefaultCostCenterId.HasValue
            && owned.Contains(request.DefaultCostCenterId.Value)
            && !request.Lines.Any(line => line.CostCenterId.HasValue
                && !owned.Contains(line.CostCenterId.Value)));
    }

    internal static IQueryable<ExpenseRequest> Apply(
        BmsDbContext dbContext,
        IQueryable<ExpenseRequest> source,
        string actorExternalId,
        bool includeAll) =>
        Apply(dbContext, source, actorExternalId, includeAll, DateTimeOffset.UtcNow);

    internal static IQueryable<ExpenseRequest> Apply(
        BmsDbContext dbContext,
        IQueryable<ExpenseRequest> source,
        string actorExternalId,
        bool includeAll,
        DateTimeOffset authorizationAtUtc)
    {
        return BusinessDataReadScopeQuery.ApplyRequests(dbContext, source, actorExternalId, includeNsiQueue: false);
    }
}
