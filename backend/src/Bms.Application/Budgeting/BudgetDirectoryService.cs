using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;

namespace Bms.Application.Budgeting;

/// <summary>Directory-only administration; does not grant expense rights or edit budget amounts.</summary>
public sealed partial class BudgetDirectoryService(
    IBudgetDirectoryStore store,
    BudgetDocumentAuthority authority,
    ICurrentActor actor,
    IBusinessTransactionRunner transactions,
    IIdempotencyStore idempotency,
    IBusinessAuditSink audit,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BudgetDirectorySettingsDto> GetAsync(Guid costCenterId, CancellationToken cancellationToken)
    {
        await RequireReaderAsync(cancellationToken);
        return await transactions.ExecuteAsync(async token =>
        {
            await RequireReaderAsync(token);
            return await store.ReadAsync(costCenterId, token) ?? throw new BudgetDirectoryNotFoundException();
        }, cancellationToken);
    }

    public async Task<BudgetDirectoryOptionPageDto> ListCostCentersAsync(
        string? query, int skip, int take, CancellationToken cancellationToken)
    {
        await RequireReaderAsync(cancellationToken);
        var search = RequirePage(query, skip, take);
        return await transactions.ExecuteAsync(async token =>
        {
            await RequireReaderAsync(token);
            return await store.ListCostCentersAsync(search, skip, take, token);
        }, cancellationToken);
    }

    public async Task<BudgetDirectoryOptionPageDto> ListEmployeesAsync(
        string? query, int skip, int take, CancellationToken cancellationToken)
    {
        await RequireReaderAsync(cancellationToken);
        var search = RequirePage(query, skip, take);
        return await transactions.ExecuteAsync(async token =>
        {
            await RequireReaderAsync(token);
            return await store.ListEmployeesAsync(search, skip, take, token);
        }, cancellationToken);
    }

    public async Task<BudgetDirectorySettingsDto> UpdateAsync(
        Guid costCenterId, UpdateBudgetDirectoryCommand command, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        await RequireMaintainerAsync(cancellationToken);
        if (costCenterId == Guid.Empty)
        {
            throw new BudgetDirectoryNotFoundException();
        }
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 127)
        {
            throw new InvalidIdempotencyKeyException("One idempotency key is required.");
        }
        var normalized = ValidateCommand(command);
        var actorHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(externalId)));
        var scope = $"budget-directory:update:v1:{costCenterId:N}:{actorHash}";
        var requestHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalized, JsonOptions)));

        return await transactions.ExecuteAsync(async token =>
        {
            // Preflight was outside this transaction. The first retained lock
            // is the CFO anchor; fresh authority reads follow on every retry.
            var exists = await store.LockCostCenterAsync(costCenterId, token);
            await RequireMaintainerAsync(token);
            if (!exists) { throw new BudgetDirectoryNotFoundException(); }
            var before = await store.ReadAsync(costCenterId, token) ?? throw new BudgetDirectoryNotFoundException();
            var replay = await idempotency.FindAsync(scope, idempotencyKey, token);
            if (replay is not null)
            {
                if (!string.Equals(replay.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    throw new IdempotencyConflictException();
                }
                var result = JsonSerializer.Deserialize<BudgetDirectorySettingsDto>(replay.ResponseJson, JsonOptions);
                if (replay.ResponseStatusCode != 200 || result is null
                    || result.CostCenterId != costCenterId || result.ConcurrencyStamp is null
                    || result.ConcurrencyStamp == Guid.Empty)
                {
                    throw new DomainRuleViolationException("Stored budget-directory replay is invalid.");
                }
                return result;
            }

            var after = await store.ReplaceAsync(costCenterId, normalized, token);
            var now = clock.UtcNow;
            if (now == default || now.Offset != TimeSpan.Zero)
            {
                throw new InvalidOperationException("A UTC clock is required for budget-directory audit.");
            }
            var correlation = new Guid(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                new { scope, idempotencyKey }, JsonOptions)).AsSpan(0, 16)).ToString("N");
            var json = JsonSerializer.Serialize(after, JsonOptions);
            audit.Append(Guid.NewGuid(), now, externalId, "BudgetCostCenterSettings", costCenterId.ToString("D"),
                "BudgetDirectoryUpdated", JsonSerializer.Serialize(before, JsonOptions), json, correlation);
            idempotency.Add(scope, idempotencyKey, requestHash, 200, json, now);
            try { await unitOfWork.SaveChangesAsync(token); }
            catch (ExpenseRequestVersionConflictException) { throw new BudgetInstanceConcurrencyConflictException(); }
            return after;
        }, cancellationToken);
    }

    public static UpdateBudgetDirectoryCommand ValidateCommand(UpdateBudgetDirectoryCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ExpectedConcurrencyStamp == Guid.Empty || command.Members is null
            || command.Members.Count > 1000 || command.Members.Any(member => member is null)
            || command.Members.Any(member => member.EmployeeId == Guid.Empty
                || member.Kind is not (BudgetCostCenterMembershipKinds.Owner or BudgetCostCenterMembershipKinds.Participant))
            || command.Members.Select(member => member.EmployeeId).Distinct().Count() != command.Members.Count)
        {
            throw new DomainRuleViolationException("Budget directory membership or version is invalid.");
        }
        return command with
        {
            Abbreviation = BudgetDocumentNumber.CanonicalizePrefix(command.Abbreviation),
            Members = command.Members.OrderBy(member => member.EmployeeId).ToArray()
        };
    }

    private async Task RequireReaderAsync(CancellationToken token)
    {
        if (!await authority.CanReadDirectoryAsync(actor, token)) throw new ActorForbiddenException();
    }

    private async Task RequireMaintainerAsync(CancellationToken cancellationToken)
    {
        if (!await authority.CanMaintainDirectoryAsync(actor, cancellationToken)) { throw new ActorForbiddenException(); }
    }

    private static string RequirePage(string? query, int skip, int take)
    {
        var text = query?.Trim() ?? string.Empty;
        if (skip < 0 || take is < 1 or > 100 || text.Length > 200 || text.Any(char.IsControl))
        {
            throw new DomainRuleViolationException("Budget directory search or paging is invalid.");
        }
        return text;
    }
}
