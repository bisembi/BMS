using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

public sealed record DeleteExpenseRequestDraftCommand(Guid ExpectedVersion);

public sealed partial class ExpenseRequestService
{
    private sealed record DeletedDraft(Guid Id, Guid ExpectedVersion, string InitiatorExternalId,
        Guid? CostCenterId, Guid?[] LineCostCenterIds, Guid? OrganizationId, bool Deleted);

    public async Task<bool> ResolveDeleteDraftIdempotencyAsync(Guid id, string key, CancellationToken cancellationToken)
    {
        var actor = await RequireCopyCommandActorAsync(cancellationToken);
        ValidateIdempotencyKey(key);
        return await transactionRunner.ExecuteAsync(async token =>
        {
            var stored = await idempotencyStore.FindAsync(BuildIdempotencyScope("delete-draft", id, actor), key, token);
            if (stored is null) return false;
            var replay = JsonSerializer.Deserialize<DeletedDraft>(stored.ResponseJson, JsonOptions);
            if (stored.ResponseStatusCode != 204 || replay is null || !replay.Deleted || replay.Id != id
                || replay.ExpectedVersion == Guid.Empty || replay.InitiatorExternalId != actor || replay.LineCostCenterIds is null)
                throw new DomainRuleViolationException("Invalid deletion replay.");
            await RequireDocumentReadScopeAsync(replay.CostCenterId, replay.LineCostCenterIds, token);
            await EnsureBudgetOwnerMayEditExistingAsync(actor, replay.CostCenterId, replay.LineCostCenterIds, token);
            return true;
        }, cancellationToken);
    }

    public async Task DeleteDraftAsync(Guid id, DeleteExpenseRequestDraftCommand command,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var actor = await RequireCopyCommandActorAsync(cancellationToken);
        ValidateIdempotencyKey(idempotencyKey);
        ValidateExpectedVersion(command.ExpectedVersion);
        if (id == Guid.Empty) throw new DomainRuleViolationException("A request identity is required.");
        await transactionRunner.ExecuteAsync(async token =>
        {
            var scope = BuildIdempotencyScope("delete-draft", id, actor);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{id:D}:{command.ExpectedVersion:D}:delete-draft-v1")));
            var stored = await idempotencyStore.FindAsync(scope, idempotencyKey, token);
            if (stored is not null)
            {
                if (!string.Equals(stored.RequestHash, hash, StringComparison.Ordinal)) throw new IdempotencyConflictException();
                var replay = JsonSerializer.Deserialize<DeletedDraft>(stored.ResponseJson, JsonOptions);
                if (stored.ResponseStatusCode != 204 || replay is null || !replay.Deleted || replay.Id != id
                    || replay.ExpectedVersion != command.ExpectedVersion || replay.InitiatorExternalId != actor
                    || replay.LineCostCenterIds is null)
                    throw new DomainRuleViolationException("Invalid deletion replay.");
                await RequireDocumentReadScopeAsync(replay.CostCenterId, replay.LineCostCenterIds, token);
                await EnsureBudgetOwnerMayEditExistingAsync(actor, replay.CostCenterId, replay.LineCostCenterIds, token);
                return true;
            }

            // Same exclusive document lock as save/submit. A concurrent submit
            // cannot turn this check into deletion of a non-draft document.
            var request = await repository.FindForWorkflowUpdateAsync(id, token)
                ?? throw new ExpenseRequestNotFoundException();
            if (!string.Equals(request.InitiatorExternalId, actor, StringComparison.Ordinal))
                throw new ExpenseRequestNotFoundException();
            await RequireDocumentReadScopeAsync(request.DefaultCostCenterId, request.Lines.Select(l => l.CostCenterId), token);
            await EnsureBudgetOwnerMayEditExistingAsync(actor, request.DefaultCostCenterId, request.Lines.Select(l => l.CostCenterId), token);
            if (request.ConcurrencyStamp != command.ExpectedVersion) throw new ExpenseRequestVersionConflictException();
            if (request.Status != ExpenseRequestStatus.Draft)
                throw new DomainRuleViolationException("Only draft expense requests may be deleted.");
            var before = JsonSerializer.Serialize(ExpenseRequestDto.FromDomain(request), JsonOptions);
            // Release the draft's budget projections atomically. Neither source
            // rows, number allocation, attachments nor history are removed.
            await budgetRepository.ReplaceForRequestAsync(id, [], token);
            var result = new DeletedDraft(id, command.ExpectedVersion, actor, request.DefaultCostCenterId,
                request.Lines.Select(l => l.CostCenterId).ToArray(), request.OrganizationId, true);
            var after = JsonSerializer.Serialize(result, JsonOptions);
            var now = clock.UtcNow;
            auditSink.Append(Guid.NewGuid(), now, actor, "ExpenseRequest", id.ToString("D"),
                "ExpenseRequestDraftDeleted", before, after, Guid.NewGuid().ToString("N"));
            idempotencyStore.Add(scope, idempotencyKey, hash, 204, after, now);
            await unitOfWork.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
    }
}
