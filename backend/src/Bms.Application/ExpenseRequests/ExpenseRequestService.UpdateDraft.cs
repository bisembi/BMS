using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// Updating a draft. The expected version is checked before anything is
/// written, so a stale caller can never overwrite a newer state.
public sealed partial class ExpenseRequestService
{

    public async Task<UpdateExpenseRequestResult> UpdateDraftAsync(
        Guid id,
        UpdateExpenseRequestCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actorExternalId = RequireExistingRequestCommandActor();

        ValidateIdempotencyKey(idempotencyKey);
        ExpenseRequestCommandShapeValidator.EnsureUpdateShape(command);
        ValidateExpectedVersion(command.ExpectedVersion);
        cancellationToken.ThrowIfCancellationRequested();
        return await transactionRunner.ExecuteAsync(async transactionCancellationToken =>
        {
            var scope = BuildIdempotencyScope("update-draft", id, actorExternalId);
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
                 (!ExpenseRequestDraftFingerprint.TryComputeCanonicalV1(
                     id,
                     command,
                     out var canonicalFingerprint) ||
                 !string.Equals(
                     stored.RequestHash,
                     canonicalFingerprint!.Hash,
                     StringComparison.Ordinal)))
            {
                throw new IdempotencyConflictException();
            }

            var authorizationAtUtc = clock.UtcNow;
            var request = await repository.FindForWorkflowUpdateAsync(
                id,
                transactionCancellationToken);
            if (request is null)
            {
                throw new ExpenseRequestNotFoundException();
            }

            var actionAuthority = await RequireExistingRequestAuthorityAsync(
                request,
                actorExternalId,
                authorizationAtUtc,
                transactionCancellationToken);

            var writeCommand = await PrepareAdvance372Async(command, transactionCancellationToken);
            await RequireDocumentReadScopeAsync(writeCommand.DefaultCostCenterId,
                writeCommand.Lines?.Select(line => line.CostCenterId) ?? [], transactionCancellationToken);
            await ValidateAccessScopeAsync(
                actionAuthority.InitiatorExternalId,
                writeCommand.OrganizationId,
                writeCommand.DefaultCostCenterId,
                writeCommand.Lines?.Select(line => line.CostCenterId),
                writeCommand.PlannedPaymentDate ?? DateOnly.FromDateTime(authorizationAtUtc.UtcDateTime),
                transactionCancellationToken,
                await PrincipalBudgetAuthorityAsync(actionAuthority.InitiatorExternalId, transactionCancellationToken));
            await EnsureBudgetOwnerMayEditExistingAsync(
                actionAuthority.InitiatorExternalId,
                writeCommand.DefaultCostCenterId,
                writeCommand.Lines?.Select(line => line.CostCenterId),
                transactionCancellationToken);

            if (stored is not null)
            {
                var replayed = DeserializeStoredResponse(stored.ResponseJson);
                await RequireDocumentReadScopeAsync(replayed.DefaultCostCenterId,
                    replayed.Lines.Select(line => line.CostCenterId), transactionCancellationToken);
                return new UpdateExpenseRequestResult(replayed with { RequestNumber = request.RequestNumber }, true, stored.ResponseStatusCode);
            }

            if (request.ConcurrencyStamp != writeCommand.ExpectedVersion)
            {
                throw new ExpenseRequestVersionConflictException();
            }

            var paymentPurpose = ExpenseRequestDraftPaymentPurposePolicy.ResolveUpdatePurpose(
                request,
                actorExternalId,
                writeCommand.Purpose);
            await cashFlowAssignments.ValidateAsync(writeCommand.PaymentOperation?.IsPayrollAdvance == true
                    ? writeCommand.PaymentOperation.BudgetCostCenterId : writeCommand.DefaultCostCenterId,
                writeCommand.Lines?.Select(line => line.CashFlowItemId) ?? [], transactionCancellationToken);

            // Freeze the old aggregate fields before mutation without taking a
            // Serializable projection read ahead of the repository's budget
            // locks. Replacement returns the authoritative old composition.
            var before = ExpenseRequestDto.FromDomain(request);
            var accrualIds = AccrualDepartmentsToValidate387(
                writeCommand.Lines ?? [], request.Lines, transactionCancellationToken);
            if(accrualIds.Length>0 && (accrualDepartments is null || !await accrualDepartments.AreActiveAsync(accrualIds,transactionCancellationToken)))
                throw new DomainRuleViolationException("Choose an active accrual department.");
            var now = authorizationAtUtc;
            request.UpdateDraft(
                now,
                writeCommand.OrganizationId,
                writeCommand.DefaultCostCenterId,
                writeCommand.PlannedPaymentDate,
                writeCommand.CurrencyCode,
                writeCommand.CounterpartyId,
                writeCommand.ContractId,
                paymentPurpose,
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
                    line.Id,
                    line.VatRateCode, line.AccrualCostCenterId)),
                ToEvidence(writeCommand));

            await ValidateContractAsync(request, transactionCancellationToken);
            await draftCurrencyService.RefreshAsync(request, transactionCancellationToken);
            var projection = ExpenseRequestBudgetProjectionCalculator.Calculate(request);
            var existingProjections = await budgetRepository.ReplaceForRequestAsync(
                id,
                projection.Projections,
                transactionCancellationToken);

            var beforeJson = JsonSerializer.Serialize(before with
            {
                BudgetReservation = before.BudgetReservation with
                {
                    ReservedAmountKzt = existingProjections.Sum(item => item.ReservedAmountKzt),
                    // Preserve the old SQL uniqueidentifier ordering; Guid's
                    // default comparer orders these identifiers differently.
                    Positions = existingProjections.OrderBy(item => item.ExpenseRequestLineId,
                        Comparer<Guid>.Create(CompareAuditProjectionLineIds))
                        .Select(item => new BudgetReservationPositionDto(
                        item.ExpenseRequestLineId,
                        item.CostCenterId,
                        item.CashFlowItemId,
                        item.BudgetMonth,
                        item.ReservedAmountKzt)).ToArray(),
                },
            }, JsonOptions);

            var response = await ToCurrentDtoAsync(request, projection.Projections, transactionCancellationToken);
            var responseJson = JsonSerializer.Serialize(response, JsonOptions);
            var correlationId = Guid.NewGuid().ToString("N");

            auditSink.Append(
                Guid.NewGuid(),
                now,
                actorExternalId,
                "ExpenseRequest",
                id.ToString("D"),
                "DraftUpdated",
                beforeJson,
                responseJson,
                correlationId);
            ExpenseRequestSubstitutionAudit.Append(
                auditSink,
                id,
                now,
                actionAuthority,
                "DraftUpdatedBySubstitute",
                correlationId);
            outboxSink.Enqueue(
                Guid.NewGuid(),
                now,
                "expense-request.draft-updated.v1",
                responseJson,
                correlationId);
            idempotencyStore.Add(
                scope,
                idempotencyKey,
                legacyRequestHash,
                StatusCodes.Ok,
                responseJson,
                now);

            await unitOfWork.SaveChangesAsync(transactionCancellationToken);
            return new UpdateExpenseRequestResult(response, false, StatusCodes.Ok);
        }, cancellationToken);
    }

    internal static Guid[] AccrualDepartmentsToValidate387(
        IEnumerable<UpdateExpenseRequestLineCommand> lines,
        IEnumerable<ExpenseRequestLine> existingLines,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Lines is an ordered snapshot getter: enumerate it once, not per input.
        var departments = existingLines.ToDictionary(line => line.Id,
            line => line.AccrualCostCenterId ?? line.CostCenterId);
        var changed = new HashSet<Guid>();
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.AccrualCostCenterId is { } department
                && (line.Id is not { } id || !departments.TryGetValue(id, out var old)
                    || old != department))
                changed.Add(department);
        }
        return changed.ToArray();
    }

    private static int CompareAuditProjectionLineIds(Guid left, Guid right)
    {
        // Historical audit order is the persisted uniqueidentifier byte order.
        // Keep this value-only comparison free of database-provider assemblies.
        ReadOnlySpan<byte> order = [10, 11, 12, 13, 14, 15, 8, 9, 6, 7, 4, 5, 0, 1, 2, 3];
        Span<byte> leftBytes = stackalloc byte[16];
        Span<byte> rightBytes = stackalloc byte[16];
        left.TryWriteBytes(leftBytes);
        right.TryWriteBytes(rightBytes);
        foreach (var index in order)
        {
            var comparison = leftBytes[index].CompareTo(rightBytes[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }
}
