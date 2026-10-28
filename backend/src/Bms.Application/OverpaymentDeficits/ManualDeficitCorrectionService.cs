using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.Overpayments;

namespace Bms.Application.OverpaymentDeficits;

/// <summary>
/// Creates and decides corrections of existing deficits without creating a
/// payment or changing requests, budgets or accruals. The transaction owns
/// current authority, W150 persistence, private audit/outbox and the recoverable
/// result together. Every retry and replay re-evaluates current authority.
/// </summary>
public sealed partial class ManualDeficitCorrectionService(
    IManualDeficitCorrectionStore store,
    ManualDeficitCorrectionAuthority authority,
    ICurrentActor currentActor,
    IBusinessTransactionRunner transactionRunner,
    IIdempotencyStore idempotencyStore,
    IBusinessAuditSink auditSink,
    IOutboxSink outboxSink,
    IUnitOfWork unitOfWork,
    IClock clock) : IManualDeficitCorrectionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<ManualDeficitCorrectionCommandResult> CreateAsync(
        Guid deficitId, CreateManualDeficitCorrectionCommand command,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        MoneyAmountPolicy.EnsurePositive(command.Amount, "Manual correction amount");
        return ExecuteAsync(deficitId, ManualDeficitCorrectionOperation.Create,
            new CommandPayload(command.Amount, RequiredText(command.Basis), null, null),
            idempotencyKey, cancellationToken);
    }

    public Task<ManualDeficitCorrectionCommandResult> ApproveAsync(
        Guid correctionId, ApproveManualDeficitCorrectionCommand command,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteAsync(correctionId, ManualDeficitCorrectionOperation.Approve,
            new CommandPayload(null, null, OptionalText(command.Comment), null),
            idempotencyKey, cancellationToken);
    }

    public Task<ManualDeficitCorrectionCommandResult> RejectAsync(
        Guid correctionId, RejectManualDeficitCorrectionCommand command,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteAsync(correctionId, ManualDeficitCorrectionOperation.Reject,
            new CommandPayload(null, null, OptionalText(command.Comment), RequiredText(command.Reason)),
            idempotencyKey, cancellationToken);
    }

    public async Task<ManualDeficitCorrectionDto?> ResolveIdempotencyAsync(
        Guid resourceId, ManualDeficitCorrectionOperation operation,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var actor = ActorIdentity.RequireBusinessDataAccess(currentActor);
        var scope = BuildScope(resourceId, operation, actor, idempotencyKey);
        try
        {
            return await transactionRunner.ExecuteAsync<ManualDeficitCorrectionDto?>(async token =>
            {
                await store.AcquireCommandGateAsync(resourceId, operation, token);
                var stored = await idempotencyStore.FindAsync(scope, idempotencyKey, token);
                // Even an absent result cannot be used to inspect another organization's key.
                var before = await RequireContextAsync(resourceId, operation, actor, RequireUtcNow(), token);
                return stored is null ? null : await ReadReplayAsync(
                    stored, resourceId, operation, CommandId(scope, idempotencyKey), before, token);
            }, cancellationToken);
        }
        catch (Exception exception) when (!ProcessFatalExceptionClassifier.IsFatal(exception) && !IsExpected(exception))
        {
            throw new ManualDeficitCorrectionUnavailableException(exception);
        }
    }

    private async Task<ManualDeficitCorrectionCommandResult> ExecuteAsync(
        Guid resourceId, ManualDeficitCorrectionOperation operation, CommandPayload payload,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var actor = ActorIdentity.RequireBusinessDataAccess(currentActor);
        var scope = BuildScope(resourceId, operation, actor, idempotencyKey);
        // Amount was already validated as decimal(18,2). Equivalent decimal
        // scales must identify the same request, without rounding its value.
        var canonicalPayload = new
        {
            amount = payload.Amount?.ToString("0.00", CultureInfo.InvariantCulture),
            payload.Basis,
            payload.Comment,
            payload.RejectionReason
        };
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { version = 1, resourceId, operation, payload = canonicalPayload }, JsonOptions)));
        try
        {
            return await transactionRunner.ExecuteAsync(async token =>
            {
                await store.AcquireCommandGateAsync(resourceId, operation, token);
                var stored = await idempotencyStore.FindAsync(scope, idempotencyKey, token);
                var now = RequireUtcNow();
                var before = await RequireContextAsync(resourceId, operation, actor, now, token);
                var commandId = CommandId(scope, idempotencyKey);
                if (stored is not null)
                {
                    ValidateStoredEnvelope(stored, operation);
                    if (!string.Equals(stored.RequestHash, hash, StringComparison.Ordinal))
                    {
                        throw new IdempotencyConflictException();
                    }
                    return new ManualDeficitCorrectionCommandResult(
                        await ReadReplayAsync(stored, resourceId, operation, commandId, before, token),
                        true, stored.ResponseStatusCode);
                }

                var result = await ApplyAsync(commandId, operation, before, payload, now, token);
                var response = ToDto(result.Correction);
                var responseStatus = operation == ManualDeficitCorrectionOperation.Create ? 201 : 200;
                var action = operation == ManualDeficitCorrectionOperation.Create
                    ? "ManualDeficitCorrectionCreated"
                    : "ManualDeficitCorrection" + result.Correction.Proposal.State;
                var audit = JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    source = "Http",
                    operation,
                    commandId,
                    executorEmployeeId = before.Actor.EmployeeId,
                    functionalRole = before.Actor.RoleCode,
                    correction = result.Correction,
                    deficit = result.Deficit
                }, JsonOptions);
                var correlationId = commandId.ToString("N");
                auditSink.Append(Guid.NewGuid(), now, actor, "ManualDeficitCorrection",
                    response.Id.ToString("D"), action,
                    JsonSerializer.Serialize(new { before.Correction, before.Deficit }, JsonOptions),
                    audit, correlationId);
                outboxSink.Enqueue(Guid.NewGuid(), now, action, audit, correlationId);
                idempotencyStore.Add(scope, idempotencyKey, hash, responseStatus,
                    JsonSerializer.Serialize(response, JsonOptions), now);
                await unitOfWork.SaveChangesAsync(token);
                return new ManualDeficitCorrectionCommandResult(response, false, responseStatus);
            }, cancellationToken);
        }
        catch (Exception exception) when (!ProcessFatalExceptionClassifier.IsFatal(exception) && !IsExpected(exception))
        {
            throw new ManualDeficitCorrectionUnavailableException(exception);
        }
    }

    private async Task<AuthorizedContext> RequireContextAsync(
        Guid resourceId, ManualDeficitCorrectionOperation operation, string actor,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        ManualDeficitCorrectionSnapshot? correction = null;
        OverpaymentDeficitSnapshot deficit;
        if (operation == ManualDeficitCorrectionOperation.Create)
        {
            deficit = await store.FindDeficitAsync(resourceId, cancellationToken)
                ?? throw new ManualDeficitCorrectionNotFoundException();
            if (deficit.DeficitId != resourceId)
            {
                throw new ManualDeficitCorrectionUnavailableException();
            }
        }
        else
        {
            var context = await store.FindCorrectionAsync(resourceId, cancellationToken)
                ?? throw new ManualDeficitCorrectionNotFoundException();
            RequireMatchingContext(context, resourceId);
            correction = context.Correction;
            deficit = context.Deficit;
        }
        var authorizedActor = await authority.RequireAsync(
            actor, deficit.OrganizationId, operation, DateOnly.FromDateTime(now.UtcDateTime),
            cancellationToken);
        ManualDeficitSnapshotValidation.Require(deficit);
        return new AuthorizedContext(deficit, correction, authorizedActor);
    }

    private async Task<ManualDeficitCorrectionContext> ApplyAsync(
        Guid commandId, ManualDeficitCorrectionOperation operation, AuthorizedContext before,
        CommandPayload payload, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ManualDeficitCorrectionContext actual;
        ManualDeficitCorrectionSnapshot expectedCorrection;
        var expectedDeficit = before.Deficit;
        if (operation == ManualDeficitCorrectionOperation.Create)
        {
            if (await store.HasPendingCorrectionAsync(before.Deficit.DeficitId, cancellationToken))
            {
                throw new ManualDeficitCorrectionConflictException();
            }
            var correctionId = Guid.NewGuid();
            var amount = payload.Amount ?? throw new ManualDeficitCorrectionUnavailableException();
            var basis = payload.Basis ?? throw new ManualDeficitCorrectionUnavailableException();
            var proposal = ManualOverpaymentDeficitCorrectionPlanner.CreateProposal(
                correctionId, before.Deficit, amount, basis, before.Actor.EmployeeId, false);
            expectedCorrection = new(proposal, commandId, null, null, null, null, null);
            actual = await store.CreateAsync(commandId, correctionId, before.Deficit.DeficitId,
                amount, basis, before.Actor.EmployeeId, cancellationToken);
        }
        else
        {
            var correction = before.Correction ?? throw new ManualDeficitCorrectionUnavailableException();
            if (correction.Proposal.State != ManualOverpaymentDeficitCorrectionState.PendingApproval)
            {
                throw new ManualDeficitCorrectionConflictException();
            }
            var approve = operation == ManualDeficitCorrectionOperation.Approve;
            var plan = approve
                ? ManualOverpaymentDeficitCorrectionPlanner.Approve(
                    correction.Proposal, before.Deficit, before.Actor.EmployeeId, payload.Comment)
                : ManualOverpaymentDeficitCorrectionPlanner.Reject(
                    correction.Proposal, before.Actor.EmployeeId,
                    payload.RejectionReason ?? throw new ManualDeficitCorrectionUnavailableException(),
                    payload.Comment);
            expectedCorrection = correction with
            {
                Proposal = correction.Proposal with { State = plan.State },
                DecisionCommandId = commandId,
                FinancialDirectorId = before.Actor.EmployeeId,
                DecidedAtUtc = now,
                DecisionComment = payload.Comment,
                RejectionReason = payload.RejectionReason
            };
            expectedDeficit = before.Deficit with
            {
                CurrentAmount = plan.DeficitAmountAfter ?? before.Deficit.CurrentAmount,
                CurrentAmountKzt = plan.DeficitAmountKztAfter ?? before.Deficit.CurrentAmountKzt
            };
            actual = await store.DecideAsync(commandId, correction.Proposal.CorrectionId, approve,
                before.Actor.EmployeeId, payload.Comment, payload.RejectionReason, now, cancellationToken);
        }
        if (actual.Correction != expectedCorrection || actual.Deficit != expectedDeficit)
        {
            throw new ManualDeficitCorrectionUnavailableException();
        }
        return actual;
    }

    private sealed record CommandPayload(decimal? Amount, string? Basis, string? Comment, string? RejectionReason);
    private sealed record AuthorizedContext(
        OverpaymentDeficitSnapshot Deficit,
        ManualDeficitCorrectionSnapshot? Correction,
        ManualDeficitCorrectionActor Actor);
}
