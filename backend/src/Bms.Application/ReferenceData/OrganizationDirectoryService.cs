using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Common;

namespace Bms.Application.ReferenceData;

/// <summary>
/// Updates the shared organization directory for the reviewed reference-data
/// managers. The write is transactional and emits audit/outbox evidence.
/// </summary>
public sealed class OrganizationDirectoryService(
    IOrganizationDirectoryRepository repository,
    ICurrentActor actor,
    IBusinessTransactionRunner transactions,
    IIdempotencyStore idempotency,
    IBusinessAuditSink audit,
    IOutboxSink outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    internal const int MaximumCodeLength = 100;
    internal const int MaximumNameLength = 500;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ReferenceDataItemDto> CreateAsync(
        CreateOrganizationCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireReferenceDataManagement(actor);
        var normalized = ValidateCommand(command);
        var key = RequireIdempotencyKey(idempotencyKey);
        var actorHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(externalId)));
        var scope = $"reference-data:organization-create:v1:{actorHash}";
        var requestHash = Convert.ToHexString(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalized, JsonOptions)));

        try
        {
            return await transactions.ExecuteAsync(async token =>
            {
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
                    return replayed ?? throw new OrganizationDirectoryUnavailableException();
                }

                var after = await repository.CreateAsync(normalized.Code, normalized.Name, token);
                var now = clock.UtcNow;
                if (now == default || now.Offset != TimeSpan.Zero)
                {
                    throw new OrganizationDirectoryUnavailableException();
                }

                var correlationId = new Guid(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                    new { scope, key }, JsonOptions)).AsSpan(0, 16)).ToString("N");
                var afterRecord = new OrganizationDirectoryRecord(
                    after.Id,
                    after.Code,
                    after.Name,
                    true);
                var afterJson = JsonSerializer.Serialize(afterRecord, JsonOptions);
                audit.Append(
                    Guid.NewGuid(),
                    now,
                    externalId,
                    "Organization",
                    after.Id.ToString("D"),
                    "OrganizationCreated",
                    "{}",
                    afterJson,
                    correlationId);
                outbox.Enqueue(
                    Guid.NewGuid(),
                    now,
                    "OrganizationCreated",
                    afterJson,
                    correlationId);
                idempotency.Add(scope, key, requestHash, 200, JsonSerializer.Serialize(after, JsonOptions), now);
                await unitOfWork.SaveChangesAsync(token);
                return after;
            }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (ActorNotAuthenticatedException) { throw; }
        catch (ActorForbiddenException) { throw; }
        catch (OrganizationCodeConflictException) { throw; }
        catch (IdempotencyConflictException) { throw; }
        catch (InvalidIdempotencyKeyException) { throw; }
        catch (DomainRuleViolationException) { throw; }
        catch (OrganizationDirectoryUnavailableException) { throw; }
        catch (Exception exception)
        {
            throw new OrganizationDirectoryUnavailableException(exception);
        }
    }

    public async Task<ReferenceDataItemDto> UpdateAsync(
        Guid id,
        UpdateOrganizationCommand command,
        CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireReferenceDataManagement(actor);
        ArgumentNullException.ThrowIfNull(command);
        if (id == Guid.Empty)
        {
            throw new OrganizationDirectoryNotFoundException();
        }

        var normalized = ValidateCommand(command);
        try
        {
            return await transactions.ExecuteAsync(async token =>
            {
                await ActorIdentity.RequireCurrentReferenceDataManagementAsync(actor, externalId, token);
                var before = await repository.FindAsync(id, token)
                    ?? throw new OrganizationDirectoryNotFoundException();
                var after = await repository.UpdateAsync(
                    id,
                    normalized.Code,
                    normalized.Name,
                    token);
                var now = clock.UtcNow;
                if (now == default || now.Offset != TimeSpan.Zero)
                {
                    throw new OrganizationDirectoryUnavailableException();
                }

                var correlationId = Guid.NewGuid().ToString("N");
                var beforeJson = JsonSerializer.Serialize(before, JsonOptions);
                var afterRecord = new OrganizationDirectoryRecord(
                    after.Id,
                    after.Code,
                    after.Name,
                    before.IsActive);
                var afterJson = JsonSerializer.Serialize(afterRecord, JsonOptions);
                audit.Append(
                    Guid.NewGuid(),
                    now,
                    externalId,
                    "Organization",
                    after.Id.ToString("D"),
                    "OrganizationUpdated",
                    beforeJson,
                    afterJson,
                    correlationId);
                outbox.Enqueue(
                    Guid.NewGuid(),
                    now,
                    "OrganizationUpdated",
                    afterJson,
                    correlationId);
                await unitOfWork.SaveChangesAsync(token);
                return after;
            }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (ActorNotAuthenticatedException) { throw; }
        catch (ActorForbiddenException) { throw; }
        catch (OrganizationDirectoryNotFoundException) { throw; }
        catch (OrganizationCodeConflictException) { throw; }
        catch (OrganizationDirectoryUnavailableException) { throw; }
        catch (DomainRuleViolationException) { throw; }
        catch (Exception exception)
        {
            throw new OrganizationDirectoryUnavailableException(exception);
        }
    }

    internal static UpdateOrganizationCommand ValidateCommand(UpdateOrganizationCommand command)
    {
        var code = command.Code?.Trim() ?? string.Empty;
        var name = command.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code)
            || code.Length > MaximumCodeLength
            || code.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(name)
            || name.Length > MaximumNameLength
            || name.Any(char.IsControl))
        {
            throw new DomainRuleViolationException(
                "The organization code or name is invalid.");
        }

        return new UpdateOrganizationCommand(code, name);
    }

    internal static CreateOrganizationCommand ValidateCommand(CreateOrganizationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var code = command.Code?.Trim() ?? string.Empty;
        var name = command.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code)
            || code.Length > MaximumCodeLength
            || code.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(name)
            || name.Length > MaximumNameLength
            || name.Any(char.IsControl))
        {
            throw new DomainRuleViolationException(
                "The organization code or name is invalid.");
        }

        return new CreateOrganizationCommand(code, name);
    }

    private static string RequireIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 127)
        {
            throw new InvalidIdempotencyKeyException("One idempotency key is required.");
        }

        return value;
    }
}
