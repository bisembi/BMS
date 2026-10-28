using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Common;

namespace Bms.Application.Authorization;

public sealed record CostCenterOwner(Guid EmployeeId, string Name, bool EmployeeIsActive, bool RoleIsActive);
public sealed record CostCenterEditorState(Guid Id, string Code, string Name, string? GroupingName,
    IReadOnlyList<CostCenterOwner> Owners, string ConcurrencyStamp, bool OnlyCostDepartment = false);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateCostCenterEditorCommand(
    [property: JsonRequired] string Code, [property: JsonRequired] string Name,
    [property: JsonRequired] string? GroupingName, [property: JsonRequired] bool OwnerSpecified,
    [property: JsonRequired] Guid? OwnerEmployeeId, [property: JsonRequired] string ExpectedConcurrencyStamp);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateCostCenterEditorCommand(
    [property: JsonRequired] string Code, [property: JsonRequired] string Name,
    [property: JsonRequired] string? GroupingName, [property: JsonRequired] Guid? OwnerEmployeeId,
    bool OnlyCostDepartment = false);

public interface ICostCenterEditorStore
{
    Task LockAsync(Guid id, Guid? ownerEmployeeId, CancellationToken token);
    Task<CostCenterEditorState> ReadAsync(Guid id, CancellationToken token);
    Task WriteAsync(Guid id, UpdateCostCenterEditorCommand command, CancellationToken token);
    Task<Guid> CreateAsync(CreateCostCenterEditorCommand command, CancellationToken token);
}

/// One CAS command owns metadata and the canonical employee/CFO assignment.
/// GroupingName is presentation metadata, never an access-scope hierarchy.
public sealed class CostCenterEditorService(ICostCenterEditorStore store, BudgetDocumentAuthority authority,
    ICurrentActor actor, IBusinessTransactionRunner transactions, IIdempotencyStore idempotency,
    IBusinessAuditSink audit, IOutboxSink outbox, IUnitOfWork unitOfWork, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private async Task<string> RequireAsync(CancellationToken token)
    {
        var external = ActorIdentity.RequireReferenceDataManagement(actor);
        if (!await authority.CanMaintainDirectoryAsync(actor, token)) throw new ActorForbiddenException();
        return external;
    }
    public async Task<CostCenterEditorState> GetAsync(Guid id, CancellationToken token)
    {
        await RequireAsync(token);
        return await transactions.ExecuteAsync(async ct =>
        {
            await RequireAsync(ct);
            return await store.ReadAsync(id, ct);
        }, token);
    }
    public async Task<CostCenterEditorState> WriteAsync(Guid id, UpdateCostCenterEditorCommand command,
        string key, CancellationToken token)
    {
        var external = await RequireAsync(token);
        if (id == Guid.Empty || command is null || !ValidText(command.Code, 100, true)
            || !ValidText(command.Name, 500, true) || !ValidText(command.GroupingName, 500, false)
            || command.ExpectedConcurrencyStamp is null || command.ExpectedConcurrencyStamp.Length != 64
            || !command.ExpectedConcurrencyStamp.All(Uri.IsHexDigit)
            || (command.OwnerSpecified && (command.OwnerEmployeeId is null || command.OwnerEmployeeId == Guid.Empty))
            || (!command.OwnerSpecified && command.OwnerEmployeeId is not null))
            throw new DomainRuleViolationException("Choose a CFO name, grouping and owner.");
        if (string.IsNullOrWhiteSpace(key) || key.Length > 127 || key != key.Trim() || key.Any(char.IsControl))
            throw new InvalidIdempotencyKeyException("Invalid idempotency key.");
        var normalized = command with { Code = command.Code.Trim(), Name = command.Name.Trim(),
            GroupingName = string.IsNullOrWhiteSpace(command.GroupingName) ? null : command.GroupingName.Trim().Normalize(NormalizationForm.FormC) };
        if (normalized.GroupingName?.Length > 500) throw new DomainRuleViolationException("Grouping is too long.");
        var scope = $"cost-center-editor:v1:{id:N}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(external)))}";
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalized, Json)));
        return await transactions.ExecuteOwnerAssignmentMutationAsync(async ct =>
        {
            await store.LockAsync(id, normalized.OwnerSpecified ? normalized.OwnerEmployeeId : null, ct);
            await RequireAsync(ct);
            var replay = await idempotency.FindAsync(scope, key, ct);
            if (replay is not null)
            {
                if (replay.RequestHash != hash) throw new IdempotencyConflictException();
                return JsonSerializer.Deserialize<CostCenterEditorState>(replay.ResponseJson, Json)
                    ?? throw new AuthorizationDirectoryUnavailableException();
            }
            var before = await store.ReadAsync(id, ct);
            if (before.ConcurrencyStamp != normalized.ExpectedConcurrencyStamp)
                throw new AuthorizationDirectoryStateConflictException();
            await store.WriteAsync(id, normalized, ct);
            await unitOfWork.SaveChangesAsync(ct);
            var after = await store.ReadAsync(id, ct);
            var response = JsonSerializer.Serialize(after, Json);
            var eventId = Guid.NewGuid(); var correlation = eventId.ToString("N");
            audit.Append(eventId, clock.UtcNow, external, "CostCenter", id.ToString("D"),
                "CostCenterOwnerAndGroupingChanged", JsonSerializer.Serialize(before, Json), response, correlation);
            outbox.Enqueue(Guid.NewGuid(), clock.UtcNow, "CostCenterOwnerAndGroupingChanged", response, correlation);
            idempotency.Add(scope, key, hash, 200, response, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return after;
        }, token);
    }
    public async Task<CostCenterEditorState> CreateAsync(CreateCostCenterEditorCommand command, string key, CancellationToken token)
    {
        var external = await RequireAsync(token);
        if (command is null || !ValidText(command.Code, 100, true) || !ValidText(command.Name, 500, true)
            || !ValidText(command.GroupingName, 500, false) || command.OwnerEmployeeId == Guid.Empty
            || (command.OnlyCostDepartment && command.OwnerEmployeeId is not null))
            throw new DomainRuleViolationException("Choose a CFO code, name, grouping and active owner.");
        if (string.IsNullOrWhiteSpace(key) || key.Length > 127 || key != key.Trim() || key.Any(char.IsControl))
            throw new InvalidIdempotencyKeyException("Invalid idempotency key.");
        var normalized = command with { Code = command.Code.Trim(), Name = command.Name.Trim(),
            GroupingName = string.IsNullOrWhiteSpace(command.GroupingName) ? null : command.GroupingName.Trim().Normalize(NormalizationForm.FormC) };
        if (normalized.GroupingName?.Length > 500) throw new DomainRuleViolationException("Grouping is too long.");
        var scope = $"cost-center-editor-create:v1:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(external)))}";
        // Keep the legacy four-field hash for ordinary CFO creates: a retry of
        // an in-flight pre-upgrade command must not conflict after this release.
        object hashPayload = normalized.OnlyCostDepartment ? normalized : new
            { normalized.Code, normalized.Name, normalized.GroupingName, normalized.OwnerEmployeeId };
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(hashPayload, Json)));
        return await transactions.ExecuteOwnerAssignmentMutationAsync(async ct =>
        {
            if (await RequireAsync(ct) != external) throw new ActorForbiddenException();
            var replay = await idempotency.FindAsync(scope, key, ct);
            if (replay is not null)
            {
                if (replay.RequestHash != hash) throw new IdempotencyConflictException();
                return JsonSerializer.Deserialize<CostCenterEditorState>(replay.ResponseJson, Json)
                    ?? throw new AuthorizationDirectoryUnavailableException();
            }
            var id = await store.CreateAsync(normalized, ct);
            await unitOfWork.SaveChangesAsync(ct);
            var after = await store.ReadAsync(id, ct);
            var response = JsonSerializer.Serialize(after, Json);
            var eventId = Guid.NewGuid(); var correlation = eventId.ToString("N");
            audit.Append(eventId, clock.UtcNow, external, "CostCenter", id.ToString("D"),
                "CostCenterCreatedWithOwnerAndGrouping", "{}", response, correlation);
            outbox.Enqueue(Guid.NewGuid(), clock.UtcNow, "CostCenterCreatedWithOwnerAndGrouping", response, correlation);
            idempotency.Add(scope, key, hash, 200, response, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return after;
        }, token);
    }
    private static bool ValidText(string? value, int max, bool required) =>
        value is null ? !required : value.Length <= max && !value.Any(char.IsControl)
            && (!required || !string.IsNullOrWhiteSpace(value));
}
