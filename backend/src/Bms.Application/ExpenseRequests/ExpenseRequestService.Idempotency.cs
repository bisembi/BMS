using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// Idempotency: key validation, scope construction and the resolution of a
/// command that was already accepted. Scope, hash and stored status code are
/// part of the wire contract and must stay byte-identical.
public sealed partial class ExpenseRequestService
{
    private const string InvalidStoredResponseMessage =
        "Stored idempotent response is invalid.";

    public async Task<ExpenseRequestDto?> ResolveCreateDraftIdempotencyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actorExternalId = await RequireBusinessCommandActorAsync(cancellationToken);
        ValidateIdempotencyKey(idempotencyKey);
        return await ResolveIdempotencyAsync(
            BuildIdempotencyScope("create-draft", null, actorExternalId),
            idempotencyKey,
            actorExternalId,
            allowSubstitution: false,
            cancellationToken);
    }

    public async Task<ExpenseRequestDto?> ResolveUpdateDraftIdempotencyAsync(
        Guid id,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actorExternalId = RequireExistingRequestCommandActor();
        ValidateIdempotencyKey(idempotencyKey);
        return await ResolveIdempotencyAsync(
            BuildIdempotencyScope("update-draft", id, actorExternalId),
            idempotencyKey,
            actorExternalId,
            allowSubstitution: true,
            cancellationToken);
    }

    public async Task<ExpenseRequestDto?> ResolveCopyDraftIdempotencyAsync(
        Guid sourceId,
        string idempotencyKey,
        CancellationToken cancellationToken,
        bool fullCopy = false)
    {
        var actorExternalId = await RequireCopyCommandActorAsync(cancellationToken);
        RequireCopyAuthoringRole351();
        ValidateIdempotencyKey(idempotencyKey);
        return await ResolveIdempotencyAsync(
            BuildIdempotencyScope(fullCopy ? "copy-complete" : "copy-draft", sourceId, actorExternalId),
            idempotencyKey,
            actorExternalId,
            allowSubstitution: false,
            cancellationToken);
    }

    private Task<ExpenseRequestDto?> ResolveIdempotencyAsync(
        string scope,
        string idempotencyKey,
        string actorExternalId,
        bool allowSubstitution,
        CancellationToken cancellationToken) => transactionRunner.ExecuteAsync(
        async transactionCancellationToken =>
        {
            var stored = await idempotencyStore.FindAsync(
                scope,
                idempotencyKey,
                transactionCancellationToken);
            if (stored is null)
            {
                return null;
            }

            var response = DeserializeStoredResponse(stored.ResponseJson);
            response = await RevalidateStoredResponseAuthorityAsync(
                actorExternalId,
                response,
                allowSubstitution,
                transactionCancellationToken);
            return response;
        },
        cancellationToken);

    private static ExpenseRequestDto DeserializeStoredResponse(string responseJson)
    {
        try
        {
            var response = JsonSerializer.Deserialize<ExpenseRequestDto>(responseJson, JsonOptions)
                ?? throw new InvalidOperationException(InvalidStoredResponseMessage);
            return ValidateStoredResponseShape(response);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(InvalidStoredResponseMessage, exception);
        }
    }

    private static ExpenseRequestDto ValidateStoredResponseShape(ExpenseRequestDto response)
    {
        if (response.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(response.Status)
            || !ActorIdentity.IsCanonical(response.InitiatorExternalId)
            || string.IsNullOrWhiteSpace(response.CurrencyCode)
            || response.ConcurrencyStamp == Guid.Empty
            || response.Lines is null
            || response.Lines.Any(line => line is null || line.Id == Guid.Empty)
            || response.BudgetReservation is null
            || response.BudgetReservation.MissingRequirements is null
            || response.BudgetReservation.Positions is null
            || response.BudgetReservation.Positions.Any(position =>
                position is null
                || position.ExpenseRequestLineId == Guid.Empty
                || position.CostCenterId == Guid.Empty
                || position.CashFlowItemId == Guid.Empty)
            || response.SubmissionReadiness is null
            || response.SubmissionReadiness.MissingRequirements is null)
        {
            throw new InvalidOperationException(InvalidStoredResponseMessage);
        }

        return response;
    }

    private async Task<ExpenseRequestDto> RevalidateStoredResponseAuthorityAsync(
        string actorExternalId,
        ExpenseRequestDto response,
        bool allowSubstitution,
        CancellationToken cancellationToken)
    {
        response = await RequireStoredResultReadScopeAsync(response, cancellationToken);
        if (!ActorIdentity.IsCanonical(actorExternalId))
        {
            throw new ActorForbiddenException();
        }

        ExpenseRequestActionAuthority actionAuthority;
        if (string.Equals(
                response.InitiatorExternalId,
                actorExternalId,
                StringComparison.Ordinal))
        {
            actionAuthority = ExpenseRequestActionAuthority.Direct(actorExternalId);
        }
        else if (!allowSubstitution || substitutionAuthority is null)
        {
            throw new ActorForbiddenException();
        }
        else
        {
            actionAuthority = await substitutionAuthority.ResolveAsync(
                response.InitiatorExternalId,
                actorExternalId,
                clock.UtcNow,
                cancellationToken);
            if (!actionAuthority.IsAuthorized)
            {
                throw new ActorForbiddenException();
            }
        }

        await ValidateAccessScopeAsync(
            actionAuthority.InitiatorExternalId,
            response.OrganizationId,
            response.DefaultCostCenterId,
            response.Lines.Select(line => line.CostCenterId),
            response.PlannedPaymentDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
            cancellationToken,
            await PrincipalBudgetAuthorityAsync(actionAuthority.InitiatorExternalId, cancellationToken));

        await EnsureBudgetOwnerMayEditExistingAsync(
            actionAuthority.InitiatorExternalId,
            response.DefaultCostCenterId,
            response.Lines.Select(line => line.CostCenterId),
            cancellationToken);
        return response;
    }

    private static void ValidateIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidIdempotencyKeyException("Idempotency-Key header is required.");
        }

        if (idempotencyKey.Length > MaximumIdempotencyKeyLength)
        {
            throw new InvalidIdempotencyKeyException("Idempotency-Key cannot exceed 127 characters.");
        }

        if (!string.Equals(idempotencyKey, idempotencyKey.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidIdempotencyKeyException(
                "Idempotency-Key cannot contain leading or trailing whitespace.");
        }

        if (idempotencyKey.Any(char.IsControl))
        {
            throw new InvalidIdempotencyKeyException(
                "Idempotency-Key cannot contain control characters.");
        }
    }

    private static string BuildIdempotencyScope(
        string operation,
        Guid? requestId,
        string actorExternalId)
    {
        var scope = requestId.HasValue
            ? $"expense-requests:{operation}:{requestId.Value:D}:{actorExternalId}"
            : $"expense-requests:{operation}:{actorExternalId}";
        if (scope.Length > MaximumIdempotencyScopeLength
            || !string.Equals(scope, scope.Trim(), StringComparison.Ordinal)
            || scope.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "The canonical idempotency scope exceeds the persisted contract.");
        }

        return scope;
    }
}
