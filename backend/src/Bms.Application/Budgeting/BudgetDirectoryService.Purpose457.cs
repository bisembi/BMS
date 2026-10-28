using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;

namespace Bms.Application.Budgeting;

public sealed partial class BudgetDirectoryService
{
    public async Task<BudgetDirectorySettingsDto> SetPurpose457Async(Guid id,
        SetCostCenterPurpose457Command command, string key, CancellationToken cancellationToken)
    {
        var external = ActorIdentity.RequireBusinessDataAccess(actor);
        await RequireMaintainerAsync(cancellationToken);
        if (id == Guid.Empty || command is null || command.ExpectedConcurrencyStamp == Guid.Empty)
            throw new DomainRuleViolationException("Invalid cost department purpose.");
        if (string.IsNullOrWhiteSpace(key) || key.Length > 127 || key != key.Trim() || key.Any(char.IsControl))
            throw new InvalidIdempotencyKeyException("One idempotency key is required.");
        var scope = $"cost-center-purpose457:{id:N}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(external)))}";
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(command, JsonOptions)));
        return await transactions.ExecuteAsync(async token =>
        {
            var exists = await store.LockCostCenterAsync(id, token);
            await RequireMaintainerAsync(token);
            if (!exists) throw new BudgetDirectoryNotFoundException();
            var replay = await idempotency.FindAsync(scope, key, token);
            if (replay is not null)
            {
                if (replay.RequestHash != hash) throw new IdempotencyConflictException();
                var value = JsonSerializer.Deserialize<BudgetDirectorySettingsDto>(replay.ResponseJson, JsonOptions);
                if (replay.ResponseStatusCode != 200 || value is null || value.CostCenterId != id
                    || value.PurposeConcurrencyStamp is null || value.PurposeConcurrencyStamp == Guid.Empty)
                    throw new DomainRuleViolationException("Invalid purpose replay.");
                return value;
            }
            var before = await store.ReadAsync(id, token) ?? throw new BudgetDirectoryNotFoundException();
            await store.SetPurpose457Async(id, command, token);
            await unitOfWork.SaveChangesAsync(token);
            var after = await store.ReadAsync(id, token) ?? throw new BudgetDirectoryNotFoundException();
            var now = clock.UtcNow;
            if (now == default || now.Offset != TimeSpan.Zero) throw new InvalidOperationException("UTC clock required.");
            var response = JsonSerializer.Serialize(after, JsonOptions);
            audit.Append(Guid.NewGuid(), now, external, "BudgetCostCenterSettings", id.ToString("D"),
                "CostCenterPurposeChanged457", JsonSerializer.Serialize(before, JsonOptions), response, Guid.NewGuid().ToString("N"));
            idempotency.Add(scope, key, hash, 200, response, now);
            await unitOfWork.SaveChangesAsync(token);
            return after;
        }, cancellationToken);
    }
}
