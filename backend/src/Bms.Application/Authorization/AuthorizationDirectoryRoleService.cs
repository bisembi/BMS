using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Common;

namespace Bms.Application.Authorization;

/// <summary>
/// Maintains the functional-role projection from the same guarded directory
/// screen. AD is only the source of an enabled account; the selected BMS role
/// is written explicitly and audited.
/// </summary>
public sealed class AuthorizationDirectoryRoleService(
    IAuthorizationDirectoryStore store,
    IActiveDirectoryEmployeeDirectory activeDirectory,
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

    public async Task<IReadOnlyList<AuthorizationDirectoryRoleOption>> ListRolesAsync(
        CancellationToken cancellationToken)
    {
        await RequireMaintainerAsync(cancellationToken);
        try
        {
            return await transactions.ExecuteAsync(
                token => store.ListRolesAsync(token), cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (AuthorizationDirectoryUnavailableException) { throw; }
        catch (Exception exception) { throw new AuthorizationDirectoryUnavailableException(exception); }
    }

    public async Task<ActiveDirectoryEmployeePage> SearchActiveDirectoryAsync(
        string? query,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        await RequireMaintainerAsync(cancellationToken);
        var normalized = RequirePage(query, skip, take);
        try
        {
            return await activeDirectory.SearchAsync(normalized, skip, take, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (AuthorizationDirectoryUnavailableException) { throw; }
        catch (Exception exception) { throw new AuthorizationDirectoryUnavailableException(exception); }
    }

    public async Task<AuthorizationDirectoryEmployee> AssignRoleAsync(
        Guid employeeId,
        AssignAuthorizationDirectoryRoleCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireReferenceDataManagement(actor);
        await RequireAdministratorAsync(cancellationToken);
        if (employeeId == Guid.Empty) throw new AuthorizationDirectoryEmployeeNotFoundException();
        var normalized = ValidateRoleCommand(command);
        var key = RequireIdempotencyKey(idempotencyKey);
        var actorHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(externalId)));
        var scope = $"authorization-directory:role-assignment:v1:{employeeId:N}:{actorHash}";
        var requestHash = Convert.ToHexString(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalized, JsonOptions)));

        try
        {
            return await transactions.ExecuteOwnerAssignmentMutationAsync(async token =>
            {
                if (!await store.LockRoleAssignmentAsync(employeeId, normalized.RoleId, token))
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
                    return CanonicalizeReplay(JsonSerializer.Deserialize<AuthorizationDirectoryEmployee>(
                        replay.ResponseJson, JsonOptions));
                }
                if (!string.Equals(before.ConcurrencyStamp, normalized.ExpectedConcurrencyStamp,
                        StringComparison.Ordinal))
                {
                    throw new AuthorizationDirectoryStateConflictException();
                }

                var after = await store.ReplaceRoleAssignmentAsync(
                    employeeId, normalized.RoleId, normalized.IsActive, token);
                var now = RequireUtcNow();
                var correlation = Correlation(scope, key);
                var beforeJson = JsonSerializer.Serialize(before, JsonOptions);
                var afterJson = JsonSerializer.Serialize(after, JsonOptions);
                audit.Append(Guid.NewGuid(), now, externalId,
                    "AuthorizationDirectoryEmployee", employeeId.ToString("D"),
                    "AuthorizationDirectoryRoleAssignmentUpdated", beforeJson, afterJson, correlation);
                outbox.Enqueue(Guid.NewGuid(), now,
                    "AuthorizationDirectoryRoleAssignmentChanged", afterJson, correlation);
                idempotency.Add(scope, key, requestHash, 200, afterJson, now);
                await unitOfWork.SaveChangesAsync(token);
                return after;
            }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (AuthorizationDirectoryEmployeeNotFoundException) { throw; }
        catch (AuthorizationDirectoryStateConflictException) { throw; }
        catch (IdempotencyConflictException) { throw; }
        catch (ActorForbiddenException) { throw; }
        catch (DomainRuleViolationException) { throw; }
        catch (AuthorizationDirectoryUnavailableException) { throw; }
        catch (Exception exception) { throw new AuthorizationDirectoryUnavailableException(exception); }
    }

    public async Task<AuthorizationDirectoryEmployee> CreateFromActiveDirectoryAsync(
        CreateAuthorizationDirectoryEmployeeFromAdCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireReferenceDataManagement(actor);
        await RequireAdministratorAsync(cancellationToken);
        var normalized = ValidateAdCommand(command);
        var key = RequireIdempotencyKey(idempotencyKey);
        var actorHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(externalId)));
        var scope = $"authorization-directory:ad-employee-create:v1:{actorHash}";
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
                    return CanonicalizeReplay(JsonSerializer.Deserialize<AuthorizationDirectoryEmployee>(
                        replay.ResponseJson, JsonOptions));
                }

                // A committed create enriches the AD candidate with EmployeeId.
                // Check an authorized exact replay before evaluating fresh-create
                // prerequisites; a retry must neither create twice nor depend on AD IO.
                var candidate = await activeDirectory.FindByActorAsync(normalized.ActorExternalId, token)
                    ?? throw new AuthorizationDirectoryAdEmployeeNotFoundException();
                if (!candidate.IsActive || candidate.EmployeeId is not null)
                {
                    throw new DomainRuleViolationException(candidate.EmployeeId is not null
                        ? "The AD employee is already registered in BMS."
                        : "Only an active AD employee can be added.");
                }
                var code = NormalizeAdCode(candidate.AccountName);
                var after = await store.CreateEmployeeFromAdAsync(
                    code, candidate.Name, candidate.ActorExternalId, normalized.RoleId, token);
                var now = RequireUtcNow();
                var correlation = Correlation(scope, key);
                var afterJson = JsonSerializer.Serialize(after, JsonOptions);
                audit.Append(Guid.NewGuid(), now, externalId,
                    "AuthorizationDirectoryEmployee", after.EmployeeId.ToString("D"),
                    "AuthorizationDirectoryEmployeeCreatedFromAd", "{}", afterJson, correlation);
                outbox.Enqueue(Guid.NewGuid(), now,
                    "AuthorizationDirectoryEmployeeCreatedFromAd", afterJson, correlation);
                idempotency.Add(scope, key, requestHash, 200, afterJson, now);
                await unitOfWork.SaveChangesAsync(token);
                return after;
            }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (AuthorizationDirectoryCodeConflictException) { throw; }
        catch (AuthorizationDirectoryActorConflictException) { throw; }
        catch (AuthorizationDirectoryAdEmployeeNotFoundException) { throw; }
        catch (IdempotencyConflictException) { throw; }
        catch (ActorForbiddenException) { throw; }
        catch (DomainRuleViolationException) { throw; }
        catch (AuthorizationDirectoryUnavailableException) { throw; }
        catch (Exception exception) { throw new AuthorizationDirectoryUnavailableException(exception); }
    }

    internal static AssignAuthorizationDirectoryRoleCommand ValidateRoleCommand(
        AssignAuthorizationDirectoryRoleCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuthorizationDirectoryStateHash.Require(command.ExpectedConcurrencyStamp, nameof(command));
        if (command.RoleId == Guid.Empty)
        {
            throw new DomainRuleViolationException("The functional role is invalid.");
        }
        return command;
    }

    internal static CreateAuthorizationDirectoryEmployeeFromAdCommand ValidateAdCommand(
        CreateAuthorizationDirectoryEmployeeFromAdCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!ActorIdentity.IsCanonical(command.ActorExternalId)
            || !command.ActorExternalId.StartsWith("S-", StringComparison.OrdinalIgnoreCase)
            || command.RoleId == Guid.Empty)
        {
            throw new DomainRuleViolationException("The AD employee selection is invalid.");
        }
        return command with { ActorExternalId = command.ActorExternalId.Trim() };
    }

    private async Task RequireMaintainerAsync(CancellationToken cancellationToken)
    {
        if (!await authority.CanMaintainDirectoryAsync(actor, cancellationToken))
        {
            throw new ActorForbiddenException();
        }
    }

    private async Task RequireAdministratorAsync(CancellationToken token)
    {
        if (!await authority.CanManageRoleAssignmentsAsync(actor, token)) throw new ActorForbiddenException();
    }

    private DateTimeOffset RequireUtcNow()
    {
        var now = clock.UtcNow;
        if (now == default || now.Offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException("A UTC clock is required for directory audit.");
        }
        return now;
    }

    private static string Correlation(string scope, string key) => new Guid(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { scope, key }, JsonOptions))
            .AsSpan(0, 16)).ToString("N");

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
            throw new DomainRuleViolationException("Active AD employee paging is invalid.");
        }
        return text;
    }

    private static string NormalizeAdCode(string accountName)
    {
        var code = accountName.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 20 || code.Any(char.IsControl))
        {
            throw new DomainRuleViolationException(
                "The selected AD account cannot be converted to a BMS employee code.");
        }
        return code;
    }
}
