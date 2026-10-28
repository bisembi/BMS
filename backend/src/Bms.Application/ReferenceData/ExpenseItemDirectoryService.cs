using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Common;

namespace Bms.Application.ReferenceData;

/// <summary>
/// Audited, idempotent maintenance of the expense-item directory. The
/// catalogue is shared by all business areas, so only the reviewed reference
/// data-maintenance capability (FIN_CONTROLLER, FIN_DIRECTOR,
/// GENERAL_DIRECTOR or SYSTEM_ADMIN in the HTTP actor) may create a row. The
/// capability is checked again inside the transaction before replay lookup and
/// persistence.
/// </summary>
public sealed class ExpenseItemDirectoryService(
    IReferenceDataRepository repository,
    ICurrentActor actor,
    IBusinessTransactionRunner transactions,
    IIdempotencyStore idempotency,
    IBusinessAuditSink audit,
    IOutboxSink outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    internal const int MaximumCodeLength = 9;
    internal const int MaximumNameLength = 250;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ReferenceDataItemDto> CreateAsync(
        CreateExpenseItemCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireReferenceDataManagement(actor);
        EnsureWriteAuthority();
        var normalized = ValidateCommand(command);
        var key = RequireIdempotencyKey(idempotencyKey);
        var actorHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(externalId)));
        var scope = $"reference-data:expense-item-create:v1:{actorHash}";
        var requestHash = Convert.ToHexString(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalized, JsonOptions)));

        try
        {
            return await transactions.ExecuteAsync(async token =>
            {
                // Do not let a role revocation or profile switch turn a saved
                // idempotency response into an authorization bypass.
                await ActorIdentity.RequireCurrentReferenceDataManagementAsync(actor, externalId, token);
                var replay = await idempotency.FindAsync(scope, key, token);
                if (replay is not null)
                {
                    if (!string.Equals(replay.RequestHash, requestHash, StringComparison.Ordinal))
                    {
                        throw new IdempotencyConflictException();
                    }

                    var replayed = JsonSerializer.Deserialize<ReferenceDataItemDto>(
                        replay.ResponseJson, JsonOptions);
                    return replayed ?? throw new ExpenseItemDirectoryUnavailableException();
                }

                var after = await repository.CreateExpenseItemAsync(
                    normalized.Code!,
                    normalized.Name,
                    token);
                var now = clock.UtcNow;
                if (now == default || now.Offset != TimeSpan.Zero)
                {
                    throw new ExpenseItemDirectoryUnavailableException();
                }

                var correlationId = new Guid(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                    new { scope, key }, JsonOptions)).AsSpan(0, 16)).ToString("N");
                var afterJson = JsonSerializer.Serialize(after, JsonOptions);
                audit.Append(
                    Guid.NewGuid(),
                    now,
                    externalId,
                    "ExpenseItem",
                    after.Id.ToString("D"),
                    "ExpenseItemCreated",
                    "{}",
                    afterJson,
                    correlationId);
                outbox.Enqueue(
                    Guid.NewGuid(),
                    now,
                    "ExpenseItemCreated",
                    afterJson,
                    correlationId);
                idempotency.Add(scope, key, requestHash, 200, afterJson, now);
                await unitOfWork.SaveChangesAsync(token);
                return after;
            }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (ActorNotAuthenticatedException) { throw; }
        catch (ActorForbiddenException) { throw; }
        catch (ExpenseItemCodeConflictException) { throw; }
        catch (IdempotencyConflictException) { throw; }
        catch (InvalidIdempotencyKeyException) { throw; }
        catch (DomainRuleViolationException) { throw; }
        catch (ExpenseItemDirectoryUnavailableException) { throw; }
        catch (Exception exception)
        {
            throw new ExpenseItemDirectoryUnavailableException(exception);
        }
    }

    internal static CreateExpenseItemCommand ValidateCommand(CreateExpenseItemCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var code = command.Code?.Trim() ?? string.Empty;
        var name = command.Name?.Trim() ?? string.Empty;
        // Code is retained only in the v1 fingerprint so that cached clients
        // can replay their old receipts. New numbers are always server assigned.
        if (code.Length > MaximumCodeLength
            || code.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(name)
            || name.Length > MaximumNameLength
            || name.Any(char.IsControl))
        {
            throw new DomainRuleViolationException(
                "The expense-item code or name is invalid.");
        }

        return new CreateExpenseItemCommand(code, name);
    }

    private void EnsureWriteAuthority()
    {
        if (!actor.CanManageReferenceData)
        {
            throw new ActorForbiddenException();
        }
    }

    private static string RequireIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 127)
        {
            throw new InvalidIdempotencyKeyException(
                "One idempotency key is required.");
        }

        return value;
    }
}
