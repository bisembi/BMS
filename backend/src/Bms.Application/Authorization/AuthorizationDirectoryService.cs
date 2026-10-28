using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Common;

namespace Bms.Application.Authorization;

/// <summary>
/// Audited, idempotent maintenance of the AD actor-to-employee projection.
/// SYSTEM_ADMIN is intentionally not assignable here; it is an AD claim.
/// </summary>
public sealed class AuthorizationDirectoryService(
    IAuthorizationDirectoryStore store,
    BudgetDocumentAuthority authority,
    ICurrentActor actor,
    IBusinessTransactionRunner transactions,
    IIdempotencyStore idempotency,
    IBusinessAuditSink audit,
    IOutboxSink outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AuthorizationDirectoryEmployee> CreateEmployeeAsync(
        CreateAuthorizationDirectoryEmployeeCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireReferenceDataManagement(actor);
        await RequireAdministratorAsync(cancellationToken);

        var normalized = ValidateCreateCommand(command);
        var key = RequireIdempotencyKey(idempotencyKey);
        var actorHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(externalId)));
        var scope = $"authorization-directory:employee-create:v1:{actorHash}";
        var requestHash = Convert.ToHexString(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalized, JsonOptions)));

        try
        {
            return await transactions.ExecuteAsync(async token =>
            {
                await RequireAdministratorAsync(token);
                var replay = await idempotency.FindAsync(scope, key, token);
                if (replay is not null)
                {
                    if (!string.Equals(replay.RequestHash, requestHash, StringComparison.Ordinal))
                    {
                        throw new IdempotencyConflictException();
                    }

                    var replayed = JsonSerializer.Deserialize<AuthorizationDirectoryEmployee>(
                        replay.ResponseJson, JsonOptions);
                    return CanonicalizeReplay(replayed);
                }

                var after = await store.CreateEmployeeAsync(
                    normalized.Code,
                    normalized.Name,
                    normalized.ActorExternalId,
                    normalized.IsActive,
                    token);
                var now = clock.UtcNow;
                if (now == default || now.Offset != TimeSpan.Zero)
                {
                    throw new InvalidOperationException("A UTC clock is required for directory audit.");
                }

                var correlation = new Guid(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                    new { scope, key }, JsonOptions)).AsSpan(0, 16)).ToString("N");
                var afterJson = JsonSerializer.Serialize(after, JsonOptions);
                audit.Append(Guid.NewGuid(), now, externalId,
                    "AuthorizationDirectoryEmployee", after.EmployeeId.ToString("D"),
                    "AuthorizationDirectoryEmployeeCreated", "{}", afterJson, correlation);
                outbox.Enqueue(Guid.NewGuid(), now,
                    "AuthorizationDirectoryEmployeeCreated", afterJson, correlation);
                idempotency.Add(scope, key, requestHash, 200, afterJson, now);
                await unitOfWork.SaveChangesAsync(token);
                return after;
            }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (AuthorizationDirectoryCodeConflictException) { throw; }
        catch (AuthorizationDirectoryActorConflictException) { throw; }
        catch (IdempotencyConflictException) { throw; }
        catch (ActorForbiddenException) { throw; }
        catch (DomainRuleViolationException) { throw; }
        catch (AuthorizationDirectoryUnavailableException) { throw; }
        catch (Exception exception)
        {
            throw new AuthorizationDirectoryUnavailableException(exception);
        }
    }

    public async Task<AuthorizationDirectoryEmployeePage> ListEmployeesAsync(
        string? query,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        await RequireReaderAsync(cancellationToken);
        var normalizedQuery = RequirePage(query, skip, take);
        try
        {
            return await transactions.ExecuteAsync(token =>
                store.ListEmployeesAsync(normalizedQuery, skip, take, token), cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (AuthorizationDirectoryUnavailableException) { throw; }
        catch (Exception exception)
        {
            throw new AuthorizationDirectoryUnavailableException(exception);
        }
    }

    public async Task<AuthorizationDirectoryEmployee> GetAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        if (employeeId == Guid.Empty) throw new AuthorizationDirectoryEmployeeNotFoundException();
        await RequireReaderAsync(cancellationToken);
        try
        {
            return await transactions.ExecuteAsync(async token =>
            {
                var result = await store.ReadAsync(employeeId, token);
                return result ?? throw new AuthorizationDirectoryEmployeeNotFoundException();
            }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (AuthorizationDirectoryEmployeeNotFoundException) { throw; }
        catch (AuthorizationDirectoryUnavailableException) { throw; }
        catch (Exception exception)
        {
            throw new AuthorizationDirectoryUnavailableException(exception);
        }
    }

    public async Task<AuthorizationDirectoryEmployee> UpdateActorMappingAsync(
        Guid employeeId,
        UpdateAuthorizationDirectoryActorCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireReferenceDataManagement(actor);
        await RequireAdministratorAsync(cancellationToken);
        if (employeeId == Guid.Empty) throw new AuthorizationDirectoryEmployeeNotFoundException();

        var normalized = ValidateCommand(command);
        var key = RequireIdempotencyKey(idempotencyKey);
        var actorHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(externalId)));
        var scope = $"authorization-directory:actor-mapping:v1:{employeeId:N}:{actorHash}";
        var requestHash = Convert.ToHexString(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalized, JsonOptions)));

        try
        {
            return await transactions.ExecuteAsync(async token =>
            {
                if (!await store.LockEmployeeAsync(employeeId, token))
                {
                    throw new AuthorizationDirectoryEmployeeNotFoundException();
                }

                await RequireAdministratorAsync(token);
                var before = await store.ReadAsync(employeeId, token)
                    ?? throw new AuthorizationDirectoryEmployeeNotFoundException();
                var replay = await idempotency.FindAsync(scope, key, token);
                if (replay is not null)
                {
                    if (!string.Equals(replay.RequestHash, requestHash, StringComparison.Ordinal))
                    {
                        throw new IdempotencyConflictException();
                    }

                    var replayed = JsonSerializer.Deserialize<AuthorizationDirectoryEmployee>(
                        replay.ResponseJson, JsonOptions);
                    return CanonicalizeReplay(replayed);
                }

                if (!string.Equals(before.ConcurrencyStamp, normalized.ExpectedConcurrencyStamp,
                    StringComparison.Ordinal))
                {
                    throw new AuthorizationDirectoryStateConflictException();
                }

                var after = await store.ReplaceActorMappingAsync(
                    employeeId,
                    normalized.ActorExternalId,
                    normalized.IsActive,
                    token);
                var now = clock.UtcNow;
                if (now == default || now.Offset != TimeSpan.Zero)
                {
                    throw new InvalidOperationException("A UTC clock is required for directory audit.");
                }

                var correlation = new Guid(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                    new { scope, key }, JsonOptions)).AsSpan(0, 16)).ToString("N");
                var beforeJson = JsonSerializer.Serialize(before, JsonOptions);
                var afterJson = JsonSerializer.Serialize(after, JsonOptions);
                audit.Append(Guid.NewGuid(), now, externalId,
                    "AuthorizationDirectoryEmployee", employeeId.ToString("D"),
                    "AdActorMappingUpdated", beforeJson, afterJson, correlation);
                outbox.Enqueue(Guid.NewGuid(), now,
                    "AuthorizationDirectoryActorMappingChanged", afterJson, correlation);
                idempotency.Add(scope, key, requestHash, 200, afterJson, now);
                await unitOfWork.SaveChangesAsync(token);
                return after;
            }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (AuthorizationDirectoryEmployeeNotFoundException) { throw; }
        catch (AuthorizationDirectoryActorConflictException) { throw; }
        catch (AuthorizationDirectoryStateConflictException) { throw; }
        catch (IdempotencyConflictException) { throw; }
        catch (ActorForbiddenException) { throw; }
        catch (DomainRuleViolationException) { throw; }
        catch (AuthorizationDirectoryUnavailableException) { throw; }
        catch (Exception exception)
        {
            throw new AuthorizationDirectoryUnavailableException(exception);
        }
    }

    internal static UpdateAuthorizationDirectoryActorCommand ValidateCommand(
        UpdateAuthorizationDirectoryActorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuthorizationDirectoryStateHash.Require(command.ExpectedConcurrencyStamp, nameof(command));
        var actorExternalId = command.ActorExternalId?.Trim();
        if (actorExternalId is not null && !ActorIdentity.IsCanonical(actorExternalId))
        {
            throw new DomainRuleViolationException("The Windows actor identifier is invalid.");
        }
        if (command.IsActive && actorExternalId is null)
        {
            throw new DomainRuleViolationException("An active actor mapping requires a Windows actor identifier.");
        }

        return command with { ActorExternalId = actorExternalId };
    }

    internal static CreateAuthorizationDirectoryEmployeeCommand ValidateCreateCommand(
        CreateAuthorizationDirectoryEmployeeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var rawCode = command.Code;
        var rawName = command.Name;
        if (rawCode is null || rawCode.Any(char.IsControl))
        {
            throw new DomainRuleViolationException("The employee code is invalid.");
        }

        if (rawName is null || rawName.Any(char.IsControl))
        {
            throw new DomainRuleViolationException("The employee name is invalid.");
        }

        var code = rawCode.Trim();
        var name = rawName.Trim();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 20)
        {
            throw new DomainRuleViolationException("The employee code is invalid.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 150)
        {
            throw new DomainRuleViolationException("The employee name is invalid.");
        }

        var actorExternalId = command.ActorExternalId?.Trim();
        if (actorExternalId is not null && !ActorIdentity.IsCanonical(actorExternalId))
        {
            throw new DomainRuleViolationException("The Windows actor identifier is invalid.");
        }

        if (!command.IsActive && actorExternalId is not null)
        {
            throw new DomainRuleViolationException(
                "An inactive employee cannot receive an active Windows actor mapping.");
        }

        if (command.IsActive && actorExternalId is null)
        {
            throw new DomainRuleViolationException(
                "An active employee created in the authorization directory requires a Windows actor mapping.");
        }

        return command with
        {
            Code = code,
            Name = name,
            ActorExternalId = actorExternalId,
        };
    }

    private async Task RequireReaderAsync(CancellationToken cancellationToken)
    {
        if (!await authority.CanReadDirectoryAsync(actor, cancellationToken))
        {
            throw new ActorForbiddenException();
        }
    }

    private async Task RequireAdministratorAsync(CancellationToken token)
    {
        if (!await authority.CanManageRoleAssignmentsAsync(actor, token)) throw new ActorForbiddenException();
    }

    private static string RequireIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 127)
        {
            throw new InvalidIdempotencyKeyException("One idempotency key is required.");
        }

        return value;
    }

    private static AuthorizationDirectoryEmployee CanonicalizeReplay(
        AuthorizationDirectoryEmployee? replayed) => replayed is null
        ? throw new AuthorizationDirectoryUnavailableException()
        : replayed with { Roles = replayed.Roles.ToArray() };

    private static string RequirePage(string? query, int skip, int take)
    {
        var text = query?.Trim() ?? string.Empty;
        if (skip < 0 || take is < 1 or > 100 || text.Length > 200 || text.Any(char.IsControl))
        {
            throw new DomainRuleViolationException("Authorization-directory paging is invalid.");
        }

        return text;
    }
}
