using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

public sealed record RefreshExpenseRequestCurrencyCommand(Guid ExpectedVersion);

public sealed partial class ExpenseRequestService
{
    // An explicit, CSRF-protected command. GETs stay read-only; no client fields
    // are accepted or saved. Uses the same authority and document lock as save.
    public async Task<ExpenseRequestDto> RefreshCurrencyAsync(Guid id,
        RefreshExpenseRequestCurrencyCommand command, string idempotencyKey, CancellationToken cancellationToken)
    {
        var actor = RequireExistingRequestCommandActor();
        ArgumentNullException.ThrowIfNull(command);
        ValidateExpectedVersion(command.ExpectedVersion);
        ValidateIdempotencyKey(idempotencyKey);
        if (id == Guid.Empty) throw new DomainRuleViolationException("A request identity is required.");
        return await transactionRunner.ExecuteAsync(async token =>
        {
            var scope = BuildIdempotencyScope("refresh-currency", id, actor);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{id:D}:{command.ExpectedVersion:D}:currency-v1")));
            var stored = await idempotencyStore.FindAsync(scope, idempotencyKey, token);
            if (stored is not null && stored.RequestHash != hash) throw new IdempotencyConflictException();
            var request = await repository.FindForWorkflowUpdateAsync(id, token)
                ?? throw new ExpenseRequestNotFoundException();
            var now = clock.UtcNow;
            var authority = await RequireExistingRequestAuthorityAsync(request, actor, now, token);
            await EnsureBudgetOwnerMayEditExistingAsync(authority.InitiatorExternalId,
                request.DefaultCostCenterId, request.Lines.Select(line => line.CostCenterId), token);
            if (stored is not null)
            {
                var replay = DeserializeStoredResponse(stored.ResponseJson);
                await RequireDocumentReadScopeAsync(replay.DefaultCostCenterId, replay.Lines.Select(line => line.CostCenterId), token);
                return replay with { RequestNumber = request.RequestNumber };
            }
            if (request.ConcurrencyStamp != command.ExpectedVersion) throw new ExpenseRequestVersionConflictException();
            if (!request.Status.IsEditableDraft()) throw new DomainRuleViolationException("Only editable drafts may refresh currency.");
            // Preserve the normal document -> budget lock order. Do not take
            // Serializable projection reads before ReplaceForRequest's locks.
            var before = ExpenseRequestDto.FromDomain(request);
            await draftCurrencyService.RefreshAsync(request, token);
            // Do not reserve/release budget or touch the version if nothing changed.
            var changed = before.ConcurrencyStamp != request.ConcurrencyStamp;
            if (!changed)
            {
                var existing = await budgetRepository.ListRequestProjectionsAsync([id], token);
                var unchanged = await ToCurrentDtoAsync(request, existing, token);
                // Persist only replay evidence, not the aggregate or budget.
                idempotencyStore.Add(scope, idempotencyKey, hash, StatusCodes.Ok,
                    JsonSerializer.Serialize(unchanged, JsonOptions), now);
                await unitOfWork.SaveChangesAsync(token);
                return unchanged;
            }
            var projection = ExpenseRequestBudgetProjectionCalculator.Calculate(request);
            var previous = await budgetRepository.ReplaceForRequestAsync(id, projection.Projections, token);
            before = before with { BudgetReservation = before.BudgetReservation with {
                ReservedAmountKzt = previous.Sum(item => item.ReservedAmountKzt),
                Positions = previous.OrderBy(item => item.ExpenseRequestLineId, Comparer<Guid>.Create(CompareAuditProjectionLineIds))
                    .Select(item => new BudgetReservationPositionDto(item.ExpenseRequestLineId, item.CostCenterId,
                        item.CashFlowItemId, item.BudgetMonth, item.ReservedAmountKzt)).ToArray() } };
            var response = await ToCurrentDtoAsync(request, projection.Projections, token);
            var json = JsonSerializer.Serialize(response, JsonOptions);
            var correlation = Guid.NewGuid().ToString("N");
            auditSink.Append(Guid.NewGuid(), now, actor, "ExpenseRequest", id.ToString("D"),
                "DraftCurrencyRefreshed", JsonSerializer.Serialize(before, JsonOptions), json, correlation);
            ExpenseRequestSubstitutionAudit.Append(auditSink, id, now, authority, "DraftCurrencyRefreshedBySubstitute", correlation);
            outboxSink.Enqueue(Guid.NewGuid(), now, "expense-request.draft-currency-refreshed.v1", json, correlation);
            idempotencyStore.Add(scope, idempotencyKey, hash, StatusCodes.Ok, json, now);
            await unitOfWork.SaveChangesAsync(token);
            return response;
        }, cancellationToken);
    }
}
