using Bms.Application.Abstractions;
using Bms.Domain.Common;

namespace Bms.Application.ExpenseRequests;

public sealed record ExpenseRequestNumberLeaseCommand(Guid CostCenterId, Guid LeaseId);

public sealed partial class ExpenseRequestService
{
    public async Task<ExpenseRequestNumberLease?> ChangeNumberLeaseAsync(
        ExpenseRequestNumberLeaseCommand command, bool release, CancellationToken token)
    {
        var actor = await RequireBusinessCommandActorAsync(token);
        if (command.CostCenterId == Guid.Empty || command.LeaseId == Guid.Empty)
            throw new DomainRuleViolationException("CFO and number reservation are required.");
        return await transactionRunner.ExecuteAsync(async ct =>
        {
            await requestNumberAllocator.AcquireAsync(ct);
            await RequireDocumentReadScopeAsync(command.CostCenterId, [], ct);
            await ValidateAccessScopeAsync(actor, null, command.CostCenterId, [],
                DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), ct);
            await EnsureBudgetOwnerControlsHeaderCostCenterAsync(actor, command.CostCenterId, [], ct);
            ExpenseRequestNumberLease? result = null;
            if (release) await requestNumberAllocator.ReleaseAsync(command.CostCenterId, command.LeaseId, actor, clock.UtcNow, ct);
            else result = await requestNumberAllocator.ReserveAsync(command.CostCenterId, command.LeaseId, actor, clock.UtcNow, ct);
            auditSink.Append(Guid.NewGuid(), clock.UtcNow, actor, "ExpenseRequestNumberLease", command.LeaseId.ToString("D"),
                release ? "NumberReleased" : "NumberReservedOrRenewed", null,
                System.Text.Json.JsonSerializer.Serialize(new { command.CostCenterId, Number = result?.Number }),
                Guid.NewGuid().ToString("N"));
            await unitOfWork.SaveChangesAsync(ct);
            return result;
        }, token);
    }
}
