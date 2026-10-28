using System.Security.Cryptography;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.Overpayments;

namespace Bms.Application.OverpaymentDeficits;

public sealed partial class ManualDeficitCorrectionService
{
    private static string BuildScope(Guid id, ManualDeficitCorrectionOperation operation,
        string actor, string idempotencyKey)
    {
        if (id == Guid.Empty)
        {
            throw new ManualDeficitCorrectionNotFoundException();
        }
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 127
            || idempotencyKey != idempotencyKey.Trim() || idempotencyKey.Any(char.IsControl))
        {
            throw new InvalidIdempotencyKeyException("Idempotency-Key is invalid.");
        }
        var code = operation switch
        {
            ManualDeficitCorrectionOperation.Create => 'C',
            ManualDeficitCorrectionOperation.Approve => 'A',
            ManualDeficitCorrectionOperation.Reject => 'R',
            _ => throw new DomainRuleViolationException("The correction command is not available.")
        };
        var scope = $"manual-deficit:{code}:{id:D}:{actor}";
        if (scope.Length > 323)
        {
            throw new ManualDeficitCorrectionUnavailableException();
        }
        return scope;
    }

    // The persisted command identity binds a recovered result to this exact
    // actor/resource/operation/key, even when two corrections have equal values.
    // This does not generate or stand in for a payment-event identity.
    private static Guid CommandId(string scope, string key)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            new { version = "manual-deficit-command-v1", scope, key }, JsonOptions);
        return new Guid(SHA256.HashData(bytes).AsSpan(0, 16));
    }

    private static string RequiredText(string? value)
    {
        var normalized = OptionalText(value);
        return normalized ?? throw new DomainRuleViolationException("A correction reason is required.");
    }

    private static string? OptionalText(string? value)
    {
        if (value is null)
        {
            return null;
        }
        var normalized = value.Trim();
        // Human reasons may contain line breaks; other control characters are
        // not part of the public text contract. Never treat these as diagnostics.
        if (normalized.Length > 1_000
            || normalized.Any(character => char.IsControl(character)
                && character is not ('\r' or '\n' or '\t')))
        {
            throw new DomainRuleViolationException("The correction reason is invalid.");
        }
        return normalized.Length == 0 ? null : normalized;
    }

    private DateTimeOffset RequireUtcNow()
    {
        var now = clock.UtcNow;
        if (now == default || now.Offset != TimeSpan.Zero
            || now.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            throw new ManualDeficitCorrectionUnavailableException();
        }
        return now;
    }

    internal static void RequireMatchingContext(ManualDeficitCorrectionContext context, Guid id)
    {
        if (context.Correction?.Proposal is not { } proposal || context.Deficit is not { } deficit
            || proposal.Scope is not { } scope
            || proposal.CorrectionId != id || id == Guid.Empty
            || proposal.DeficitId != deficit.DeficitId
            || scope.OrganizationId != deficit.OrganizationId
            || scope.CounterpartyId != deficit.CounterpartyId
            || scope.CurrencyCode != deficit.CurrencyCode
            || context.Correction.CreationCommandId == Guid.Empty
            || !Enum.IsDefined(proposal.State))
        {
            throw new ManualDeficitCorrectionUnavailableException();
        }

        ManualDeficitSnapshotValidation.Require(deficit);
        try
        {
            var expectedProposal = ManualOverpaymentDeficitCorrectionPlanner.CreateProposal(
                id, deficit with
                {
                    CurrentAmount = proposal.DeficitAmountAtCreation,
                    CurrentAmountKzt = proposal.DeficitAmountKztAtCreation
                }, proposal.CorrectionAmount, proposal.Basis, proposal.ResponsibleAccountantId, false);
            if (expectedProposal with { State = proposal.State } != proposal)
            {
                throw new ManualDeficitCorrectionUnavailableException();
            }
            _ = RequiredText(proposal.Basis);
            var snapshot = context.Correction;
            _ = OptionalText(snapshot.DecisionComment);
            if (proposal.State == ManualOverpaymentDeficitCorrectionState.PendingApproval)
            {
                if (snapshot.DecisionCommandId is not null || snapshot.FinancialDirectorId is not null
                    || snapshot.DecidedAtUtc is not null || snapshot.DecisionComment is not null
                    || snapshot.RejectionReason is not null)
                {
                    throw new ManualDeficitCorrectionUnavailableException();
                }
            }
            else if (snapshot.DecisionCommandId is not { } decisionId || decisionId == Guid.Empty
                || snapshot.FinancialDirectorId is not { } directorId || directorId == Guid.Empty
                || snapshot.DecidedAtUtc is not { } decidedAt || decidedAt == default
                || decidedAt.Offset != TimeSpan.Zero
                || decidedAt.Ticks % TimeSpan.TicksPerMillisecond != 0
                || (proposal.State == ManualOverpaymentDeficitCorrectionState.Rejected
                    ? string.IsNullOrWhiteSpace(snapshot.RejectionReason)
                    : snapshot.RejectionReason is not null))
            {
                throw new ManualDeficitCorrectionUnavailableException();
            }
            if (proposal.State == ManualOverpaymentDeficitCorrectionState.Rejected)
            {
                _ = RequiredText(snapshot.RejectionReason);
            }
        }
        catch (DomainRuleViolationException exception)
        {
            // A malformed persisted proposal is not a bad request by the caller.
            throw new ManualDeficitCorrectionUnavailableException(exception);
        }
    }

    private async Task<ManualDeficitCorrectionDto> ReadReplayAsync(
        StoredIdempotentResponse stored, Guid resourceId,
        ManualDeficitCorrectionOperation operation, Guid commandId,
        AuthorizedContext authorized,
        CancellationToken cancellationToken)
    {
        ValidateStoredEnvelope(stored, operation);
        var response = JsonSerializer.Deserialize<ManualDeficitCorrectionDto>(stored.ResponseJson, JsonOptions)
            ?? throw new ManualDeficitCorrectionUnavailableException();
        if (response.Id == Guid.Empty
            || (operation == ManualDeficitCorrectionOperation.Create
                ? response.DeficitId != resourceId : response.Id != resourceId))
        {
            throw new ManualDeficitCorrectionUnavailableException();
        }
        var current = await store.FindCorrectionAsync(response.Id, cancellationToken)
            ?? throw new ManualDeficitCorrectionUnavailableException();
        RequireMatchingContext(current, response.Id);
        if (current.Deficit != authorized.Deficit
            || (authorized.Correction is not null && current.Correction != authorized.Correction))
        {
            throw new ManualDeficitCorrectionUnavailableException();
        }
        var snapshot = current.Correction;
        var expected = ToDto(snapshot);
        if (operation == ManualDeficitCorrectionOperation.Create)
        {
            if (snapshot.CreationCommandId != commandId || current.Deficit.DeficitId != resourceId)
            {
                throw new ManualDeficitCorrectionUnavailableException();
            }
            // Recovery returns the original creation result, even after a later
            // valid director decision; it never resets the persisted state.
            expected = expected with
            {
                State = nameof(ManualOverpaymentDeficitCorrectionState.PendingApproval),
                DecidedAtUtc = null,
                DecisionComment = null,
                RejectionReason = null
            };
        }
        else if (snapshot.DecisionCommandId != commandId)
        {
            throw new ManualDeficitCorrectionUnavailableException();
        }
        if (response != expected)
        {
            throw new ManualDeficitCorrectionUnavailableException();
        }
        return response;
    }

    private static void ValidateStoredEnvelope(
        StoredIdempotentResponse stored, ManualDeficitCorrectionOperation operation)
    {
        var expectedStatus = operation == ManualDeficitCorrectionOperation.Create ? 201 : 200;
        if (stored.ResponseStatusCode != expectedStatus
            || stored.RequestHash is not { Length: 64 }
            || stored.RequestHash.Any(character => !Uri.IsHexDigit(character))
            || stored.ResponseJson is not { Length: > 0 and <= 16_384 })
        {
            throw new ManualDeficitCorrectionUnavailableException();
        }
    }

    internal static ManualDeficitCorrectionDto ToDto(ManualDeficitCorrectionSnapshot snapshot)
    {
        var proposal = snapshot.Proposal;
        return new(proposal.CorrectionId, proposal.DeficitId, proposal.Scope.CurrencyCode,
            proposal.State.ToString(), proposal.CorrectionAmount, proposal.CorrectionAmountKzt,
            proposal.DeficitAmountAtCreation, proposal.DeficitAmountKztAtCreation, proposal.Basis,
            snapshot.DecidedAtUtc, snapshot.DecisionComment, snapshot.RejectionReason);
    }

    private static bool IsExpected(Exception exception) => exception is
        OperationCanceledException or ActorNotAuthenticatedException or ActorForbiddenException
        or ManualDeficitCorrectionNotFoundException or ManualDeficitCorrectionConflictException
        or ManualDeficitCorrectionUnavailableException or IdempotencyConflictException
        or InvalidIdempotencyKeyException or DomainRuleViolationException;
}
