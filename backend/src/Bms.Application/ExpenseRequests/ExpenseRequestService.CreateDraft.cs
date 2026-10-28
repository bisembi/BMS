using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// Creating a draft. State, audit and outbox are written inside one business
/// transaction, and the idempotency record is stored with it.
public sealed partial class ExpenseRequestService
{

    public async Task<CreateExpenseRequestResult> CreateDraftAsync(
        CreateExpenseRequestCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actorExternalId = await RequireBusinessCommandActorAsync(cancellationToken);

        ValidateIdempotencyKey(idempotencyKey);
        ExpenseRequestCommandShapeValidator.EnsureCreateShape(command);
        cancellationToken.ThrowIfCancellationRequested();
        return await transactionRunner.ExecuteAsync(async transactionCancellationToken =>
        {
            await requestNumberAllocator.AcquireAsync(transactionCancellationToken);
            var scope = BuildIdempotencyScope("create-draft", null, actorExternalId);
            var legacyRequestHash = ExpenseRequestDraftFingerprint.ComputeLegacyHash(command);
            var stored = await idempotencyStore.FindAsync(
                scope,
                idempotencyKey,
                transactionCancellationToken);

            if (stored is not null &&
                !string.Equals(
                    stored.RequestHash,
                    legacyRequestHash,
                    StringComparison.Ordinal) &&
                (command.AttachmentUploadSessionId is not null || !ExpenseRequestDraftFingerprint.TryComputeCanonicalV1(
                    command,
                    out var canonicalFingerprint) ||
                 !string.Equals(
                     stored.RequestHash,
                     canonicalFingerprint!.Hash,
                     StringComparison.Ordinal)))
            {
                throw new IdempotencyConflictException();
            }

            var writeCommand = await PrepareAdvance372Async(command, transactionCancellationToken);
            await RequireDocumentReadScopeAsync(writeCommand.DefaultCostCenterId,
                writeCommand.Lines?.Select(line => line.CostCenterId) ?? [], transactionCancellationToken);
            await ValidateAccessScopeAsync(
                actorExternalId,
                writeCommand.OrganizationId,
                writeCommand.DefaultCostCenterId,
                writeCommand.Lines?.Select(line => line.CostCenterId),
                writeCommand.PlannedPaymentDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
                transactionCancellationToken);
            await EnsureBudgetOwnerControlsHeaderCostCenterAsync(
                actorExternalId,
                writeCommand.DefaultCostCenterId,
                writeCommand.Lines?.Select(line => line.CostCenterId),
                transactionCancellationToken);

            if (stored is not null)
            {
                var replayed = DeserializeStoredResponse(stored.ResponseJson);
                replayed = await RequireStoredResultReadScopeAsync(replayed, transactionCancellationToken);
                return new CreateExpenseRequestResult(replayed, true, stored.ResponseStatusCode);
            }

            ExpenseRequestDraftPaymentPurposePolicy.EnsureCreateDoesNotSetPurpose(
                writeCommand.Purpose);
            await cashFlowAssignments.ValidateAsync(writeCommand.PaymentOperation?.IsPayrollAdvance == true
                    ? writeCommand.PaymentOperation.BudgetCostCenterId : writeCommand.DefaultCostCenterId,
                writeCommand.Lines?.Select(line => line.CashFlowItemId) ?? [], transactionCancellationToken);

            var now = clock.UtcNow;
            var accrualIds=(writeCommand.Lines??[]).Where(l=>l.AccrualCostCenterId.HasValue).Select(l=>l.AccrualCostCenterId!.Value).Distinct().ToArray();
            if(accrualIds.Length>0 && (accrualDepartments is null || !await accrualDepartments.AreActiveAsync(accrualIds,transactionCancellationToken)))
                throw new DomainRuleViolationException("Choose an active accrual department.");
            var requestId = Guid.NewGuid();
            var requestNumber = writeCommand.NumberLeaseId is { } numberLeaseId
                ? await requestNumberAllocator.ConsumeAsync(writeCommand.DefaultCostCenterId.GetValueOrDefault(),
                    numberLeaseId, actorExternalId, now, transactionCancellationToken)
                : await requestNumberAllocator.AllocateAsync(writeCommand.DefaultCostCenterId.GetValueOrDefault(),
                    transactionCancellationToken);
            var request = ExpenseRequest.CreateDraft(
                requestId,
                actorExternalId,
                now,
                writeCommand.OrganizationId,
                writeCommand.DefaultCostCenterId,
                writeCommand.PlannedPaymentDate,
                writeCommand.CurrencyCode,
                writeCommand.CounterpartyId,
                writeCommand.ContractId,
                null,
                writeCommand.Comment,
                writeCommand.BusinessJustification,
                writeCommand.IsUrgent,
                writeCommand.UrgencyReason,
                writeCommand.IsNewCounterparty,
                writeCommand.IsNewContract,
                writeCommand.Lines?.Select(line => new ExpenseRequestLineDraft(
                    line.CashFlowItemId,
                    line.CostCenterId,
                    line.AccrualPeriod,
                    line.Amount,
                    null,
                    line.VatRateCode, line.AccrualCostCenterId)),
                ToEvidence(writeCommand),
                requestNumber);

            await ValidateContractAsync(request, transactionCancellationToken);
            await draftCurrencyService.RefreshAsync(request, transactionCancellationToken);
            var projection = ExpenseRequestBudgetProjectionCalculator.Calculate(request);
            repository.Add(request);
            if (writeCommand.AttachmentUploadSessionId is { } uploadSessionId)
            {
                if (uploadSessionId == Guid.Empty || attachmentBinder is null)
                    throw new DomainRuleViolationException("Attachment upload session is unavailable.", DomainRuleViolationReason.ExpenseDraftUploadUnavailable);
                await attachmentBinder.BindAsync(uploadSessionId, requestId, actorExternalId, transactionCancellationToken);
            }
            await budgetRepository.ReplaceForRequestAsync(
                requestId,
                projection.Projections,
                transactionCancellationToken);

            var response = await ToCurrentDtoAsync(request, projection.Projections, transactionCancellationToken);
            var responseJson = JsonSerializer.Serialize(response, JsonOptions);
            var correlationId = Guid.NewGuid().ToString("N");

            auditSink.Append(
                Guid.NewGuid(),
                now,
                actorExternalId,
                "ExpenseRequest",
                requestId.ToString("D"),
                "DraftCreated",
                null,
                responseJson,
                correlationId);
            outboxSink.Enqueue(
                Guid.NewGuid(),
                now,
                "expense-request.draft-created.v1",
                responseJson,
                correlationId);
            idempotencyStore.Add(
                scope,
                idempotencyKey,
                legacyRequestHash,
                StatusCodes.Created,
                responseJson,
                now);

            await unitOfWork.SaveChangesAsync(transactionCancellationToken);
            return new CreateExpenseRequestResult(response, false, StatusCodes.Created);
        }, cancellationToken);
    }
}
