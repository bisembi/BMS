using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// Copying a draft. The copy hash covers the source identifier and the copy
/// policy version, so a policy change cannot silently reuse an old command.
public sealed partial class ExpenseRequestService
{

    public async Task<CopyExpenseRequestResult> CopyDraftAsync(
        Guid sourceId,
        string idempotencyKey,
        CancellationToken cancellationToken,
        bool fullCopy = false)
    {
        var actorExternalId = await RequireCopyCommandActorAsync(cancellationToken);
        RequireCopyAuthoringRole351();
        ValidateIdempotencyKey(idempotencyKey);

        return await transactionRunner.ExecuteAsync(async transactionCancellationToken =>
        {
            // Recheck current creation authority inside every transaction attempt.
            await RequireCopyCommandActorAsync(transactionCancellationToken);
            RequireCopyAuthoringRole351();
            await requestNumberAllocator.AcquireAsync(transactionCancellationToken);
            var scope = BuildIdempotencyScope(fullCopy ? "copy-complete" : "copy-draft", sourceId, actorExternalId);
            var requestHash = ComputeCopyHash(sourceId, fullCopy);
            var stored = await idempotencyStore.FindAsync(
                scope,
                idempotencyKey,
                transactionCancellationToken);
            if (stored is not null
                && !string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new IdempotencyConflictException();
            }

            // Copying reads a source; it does not edit it. Authorship is not a
            // prerequisite, but the canonical current header/line read scope is.
            var source = await repository.FindVisibleAsync(
                sourceId,
                actorExternalId,
                includeAll: false,
                transactionCancellationToken);
            if (source is null)
            {
                throw new ExpenseRequestNotFoundException();
            }

            var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
            await RequireDocumentReadScopeAsync(source.DefaultCostCenterId,
                source.Lines.Select(line => line.CostCenterId), transactionCancellationToken);
            await ValidateAccessScopeAsync(
                actorExternalId,
                source.OrganizationId,
                source.DefaultCostCenterId,
                source.Lines.Select(line => line.CostCenterId),
                today,
                transactionCancellationToken);
            await EnsureBudgetOwnerControlsHeaderCostCenterAsync(
                actorExternalId,
                source.DefaultCostCenterId,
                source.Lines.Select(line => line.CostCenterId),
                transactionCancellationToken);

            if (stored is not null)
            {
                var replayed = DeserializeStoredResponse(stored.ResponseJson);
                replayed = await RequireStoredResultReadScopeAsync(replayed, transactionCancellationToken);
                return new CopyExpenseRequestResult(replayed, true, stored.ResponseStatusCode);
            }

            var now = clock.UtcNow;
            if (source.PaymentOperation is { IsPayrollAdvance: true } advance)
            {
                if (payrollAdvanceCreationPolicy372?.Enabled == false) throw new ActorForbiddenException();
                _ = await PrepareAdvance372Async(advance, transactionCancellationToken);
            }
            await cashFlowAssignments.ValidateAsync(source.PaymentOperation?.IsPayrollAdvance == true
                    ? source.PaymentOperation.BudgetCostCenterId : source.DefaultCostCenterId,
                source.Lines.Select(line => line.CashFlowItemId), transactionCancellationToken);
            // The production allocator rejects Guid.Empty with the same domain
            // rule, while a test double can still exercise copy/idempotency
            // ordering for legacy fixtures that omit a CFO.
            var requestNumber = await requestNumberAllocator.AllocateAsync(
                source.DefaultCostCenterId.GetValueOrDefault(),
                transactionCancellationToken);
            var copy = fullCopy ? ExpenseRequest.CopyCompleteDraft(Guid.NewGuid(), actorExternalId, now,
                source, ExpenseRequestCopyDefaults.NextPaymentDate(now), requestNumber) : ExpenseRequest.CopyDraft(
                Guid.NewGuid(),
                actorExternalId,
                now,
                source,
                requestNumber);
            await ValidateContractAsync(copy, transactionCancellationToken);
            if (fullCopy) await draftCurrencyService.RefreshAsync(copy, transactionCancellationToken);
            repository.Add(copy);

            IReadOnlyCollection<BudgetRequestProjection> desiredProjections = fullCopy
                ? ExpenseRequestBudgetProjectionCalculator.Calculate(copy).Projections : [];
            await budgetRepository.ReplaceForRequestAsync(
                copy.Id,
                desiredProjections,
                transactionCancellationToken);

            var response = ExpenseRequestDto.FromDomain(copy, desiredProjections);
            var responseJson = JsonSerializer.Serialize(response, JsonOptions);
            var correlationId = Guid.NewGuid().ToString("N");
            auditSink.Append(
                Guid.NewGuid(),
                now,
                actorExternalId,
                "ExpenseRequest",
                copy.Id.ToString("D"),
                "DraftCopied",
                null,
                responseJson,
                correlationId);
            outboxSink.Enqueue(
                Guid.NewGuid(),
                now,
                "expense-request.draft-copied.v1",
                responseJson,
                correlationId);
            idempotencyStore.Add(
                scope,
                idempotencyKey,
                requestHash,
                StatusCodes.Created,
                responseJson,
                now);

            await unitOfWork.SaveChangesAsync(transactionCancellationToken);
            return new CopyExpenseRequestResult(response, false, StatusCodes.Created);
        }, cancellationToken);
    }

    private static string ComputeCopyHash(Guid sourceId, bool fullCopy = false)
    {
        var canonicalCommand = $"{sourceId:D}:{(fullCopy ? "copy-complete-v1" : CopyPolicyVersion)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalCommand)));
    }
}
