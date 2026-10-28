using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Common;

namespace Bms.Application.Tasks;

public sealed record MyApprovalTask355
{
    public string Kind { get; init; } = "";
    public Guid Id { get; init; }
    public string Number { get; init; } = "";
    public string Title { get; init; } = "";
    public string CostCenterName { get; init; } = "";
    public string RoleCode { get; init; } = "";
    public DateTimeOffset ReceivedAtUtc { get; init; }
}
public sealed record MyApprovalTaskPage355(IReadOnlyList<MyApprovalTask355> Items, int Skip, int Take, int Total);

public interface IMyApprovalTaskReader355
{
    Task<MyApprovalTaskPage355> ReadAsync(string actorExternalId, Guid employeeId,
        BusinessDataReadScope scope, int skip, int take, CancellationToken token);
    Task<MyApprovalTaskPage355> ReadAdjustments448Async(string actorExternalId, Guid employeeId,
        BusinessDataReadScope scope, int skip, int take, CancellationToken token);
}

/// <summary>Current actionable approvals, never authored drafts or read-only visibility.</summary>
public sealed class MyApprovalTasks355(ICurrentActor actor, CfoWorkflowAuthority authority,
    IBusinessTransactionRunner transactions, IMyApprovalTaskReader355 reader)
{
    public Task<MyApprovalTaskPage355> ListAsync(int skip, int take, CancellationToken token) => ListCoreAsync(skip, take, false, token);
    public Task<MyApprovalTaskPage355> ListAdjustments448Async(int skip, int take, CancellationToken token) => ListCoreAsync(skip, take, true, token);
    private async Task<MyApprovalTaskPage355> ListCoreAsync(int skip, int take, bool adjustmentsOnly, CancellationToken token)
    {
        var external = ActorIdentity.RequireBusinessDataAccess(actor);
        if (skip < 0 || take is < 1 or > 100)
            throw new DomainRuleViolationException("Task paging is invalid.");
        if (actor.IsBmsAdministrator) return new([], skip, take, 0);
        return await transactions.ExecuteAsync(async ct =>
        {
            var employee = await authority.RequireEmployeeAsync(actor, ct);
            var scope = await actor.ResolveBusinessDataReadScopeAsync(ct);
            return adjustmentsOnly
                ? await reader.ReadAdjustments448Async(external, employee, scope, skip, take, ct)
                : await reader.ReadAsync(external, employee, scope, skip, take, ct);
        }, token);
    }
}
