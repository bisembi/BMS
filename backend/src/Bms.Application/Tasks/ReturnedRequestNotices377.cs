using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Common;

namespace Bms.Application.Tasks;

public sealed record ReturnedRequestNotice377(Guid Id, Guid ExpenseRequestId, string Number,
    string OrganizationName, string CostCenterName, DateTimeOffset ReturnedAtUtc, string? Reason);
public sealed record ReturnedRequestNoticePage377(IReadOnlyList<ReturnedRequestNotice377> Items, int Skip, int Take, int Total);
public interface IReturnedRequestNoticeReader377
{
    Task<ReturnedRequestNoticePage377> ReadAsync(string actor, BusinessDataReadScope scope, int skip, int take, CancellationToken token);
}
public sealed class ReturnedRequestNotices377(ICurrentActor actor, IBusinessTransactionRunner transactions,
    IReturnedRequestNoticeReader377 reader)
{
    public async Task<ReturnedRequestNoticePage377> ListAsync(int skip, int take, CancellationToken token)
    {
        var external = ActorIdentity.RequireBusinessDataAccess(actor);
        if (skip < 0 || take is < 1 or > 100 || skip > int.MaxValue-take)
            throw new DomainRuleViolationException("Notification paging is invalid.");
        if (actor.IsBmsAdministrator) return new([],skip,take,0);
        return await transactions.ExecuteAsync(async ct =>
            await reader.ReadAsync(external, await actor.ResolveBusinessDataReadScopeAsync(ct),skip,take,ct), token);
    }
}
