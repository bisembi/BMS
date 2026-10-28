using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Common;

namespace Bms.Application.ReferenceData;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BudgetOwnerCostCenterAssignmentWriteCommand(
    [property: JsonRequired] string EmployeeCode,
    [property: JsonRequired] string CostCenterCode);

public sealed class BudgetOwnerCostCenterAssignmentNotFoundException()
    : Exception("The budget-owner assignment was not found.");

public sealed class BudgetOwnerCostCenterAssignmentConflictException()
    : Exception("The budget-owner assignment already exists.");

public sealed class BudgetOwnerCostCenterAssignmentMaintenanceService(
    IBudgetOwnerCostCenterAssignmentDirectoryWriter writer,
    ICurrentActor actor,
    IBusinessTransactionRunner transactions,
    IIdempotencyStore idempotency,
    IBusinessAuditSink audit,
    IOutboxSink outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BudgetOwnerCostCenterAssignmentDirectoryItemDto> CreateAsync(
        BudgetOwnerCostCenterAssignmentWriteCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireReferenceDataManagement(actor);
        var normalized = Normalize(command);
        var key = RequireKey(idempotencyKey);
        var actorHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actorExternalId)));
        var scope = $"reference-data:budget-owner-assignment-create:v1:{actorHash}";
        var requestHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalized, JsonOptions)));
        return await transactions.ExecuteOwnerAssignmentMutationAsync(async token =>
        {
            await ActorIdentity.RequireCurrentReferenceDataManagementAsync(actor, actorExternalId, token);
            var replay = await idempotency.FindAsync(scope, key, token);
            if (replay is not null)
            {
                if (!string.Equals(replay.RequestHash, requestHash, StringComparison.Ordinal)) throw new IdempotencyConflictException();
                return JsonSerializer.Deserialize<BudgetOwnerCostCenterAssignmentDirectoryItemDto>(replay.ResponseJson, JsonOptions)
                    ?? throw new InvalidOperationException("Invalid assignment replay.");
            }
            var after = await writer.CreateAsync(normalized, token);
            await RecordAsync(actorExternalId, "BudgetOwnerAssignmentCreated", after, token);
            var now = RequireUtcNow();
            idempotency.Add(scope, key, requestHash, 200, JsonSerializer.Serialize(after, JsonOptions), now);
            await unitOfWork.SaveChangesAsync(token);
            return after;
        }, cancellationToken);
    }

    public Task<BudgetOwnerCostCenterAssignmentDirectoryItemDto> UpdateAsync(
        string employeeCode, string costCenterCode,
        BudgetOwnerCostCenterAssignmentWriteCommand command,
        CancellationToken cancellationToken) => ExecuteUpdateAsync(employeeCode, costCenterCode, command, cancellationToken);

    private async Task<BudgetOwnerCostCenterAssignmentDirectoryItemDto> ExecuteUpdateAsync(
        string employeeCode, string costCenterCode,
        BudgetOwnerCostCenterAssignmentWriteCommand command,
        CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireReferenceDataManagement(actor);
        var normalized = Normalize(command);
        return await transactions.ExecuteOwnerAssignmentMutationAsync(async token =>
        {
            await ActorIdentity.RequireCurrentReferenceDataManagementAsync(actor, actorExternalId, token);
            var after = await writer.UpdateAsync(NormalizeCode(employeeCode), NormalizeCode(costCenterCode), normalized, token);
            await RecordAsync(actorExternalId, "BudgetOwnerAssignmentUpdated", after, token);
            await unitOfWork.SaveChangesAsync(token);
            return after;
        }, cancellationToken);
    }

    public async Task<BudgetOwnerCostCenterAssignmentDirectoryItemDto> DeleteAsync(
        string employeeCode, string costCenterCode, CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireReferenceDataManagement(actor);
        return await transactions.ExecuteOwnerAssignmentMutationAsync(async token =>
        {
            await ActorIdentity.RequireCurrentReferenceDataManagementAsync(actor, actorExternalId, token);
            var after = await writer.DeleteAsync(NormalizeCode(employeeCode), NormalizeCode(costCenterCode), token);
            await RecordAsync(actorExternalId, "BudgetOwnerAssignmentDeleted", after, token);
            await unitOfWork.SaveChangesAsync(token);
            return after;
        }, cancellationToken);
    }

    private async Task RecordAsync(string actorExternalId, string action,
        BudgetOwnerCostCenterAssignmentDirectoryItemDto after, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var now = RequireUtcNow();
        var json = JsonSerializer.Serialize(after, JsonOptions);
        var correlation = Guid.NewGuid().ToString("N");
        audit.Append(Guid.NewGuid(), now, actorExternalId, "BudgetOwnerAssignment",
            $"{after.EmployeeCode}:{after.CostCenterCode}", action, "{}", json, correlation);
        outbox.Enqueue(Guid.NewGuid(), now, action, json, correlation);
        await Task.CompletedTask;
    }

    private static BudgetOwnerCostCenterAssignmentWriteCommand Normalize(BudgetOwnerCostCenterAssignmentWriteCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new BudgetOwnerCostCenterAssignmentWriteCommand(
            NormalizeCode(command.EmployeeCode), NormalizeCode(command.CostCenterCode));
    }

    private static string NormalizeCode(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 100 || normalized.Any(char.IsControl))
            throw new DomainRuleViolationException("Employee and ЦФО codes are required.");
        return normalized;
    }

    private static string RequireKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 127) throw new InvalidIdempotencyKeyException("One idempotency key is required.");
        return value;
    }

    private DateTimeOffset RequireUtcNow()
    {
        var now = clock.UtcNow;
        if (now == default || now.Offset != TimeSpan.Zero) throw new InvalidOperationException("A UTC clock is required for directory audit.");
        return now;
    }
}
