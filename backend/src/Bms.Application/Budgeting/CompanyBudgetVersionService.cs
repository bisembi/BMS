using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Application.Security;
using Bms.Domain.Workflow;

namespace Bms.Application.Budgeting;

public sealed class CompanyBudgetVersionService(ICompanyBudgetVersionStore store, ICurrentActor actor,
    EmployeeActorResolver employees, EmployeeFunctionalRoleResolver roles, IBusinessTransactionRunner transactions,
    IIdempotencyStore idempotency, IBusinessAuditSink audit, IOutboxSink outbox, IUnitOfWork work, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static void Page(int skip, int take)
    {
        if (skip < 0 || take is < 1 or > 100 || skip > int.MaxValue - take) throw new ArgumentException("Invalid pagination.");
    }
    private async Task<(Guid Employee, string External, bool Manage, bool Activate, BusinessDataReadScope Scope)> AuthorityAsync(CancellationToken token)
    {
        var external = ActorIdentity.RequireBusinessDataAccess(actor);
        if (actor.ActiveProfileCode != BmsAccessProfileCodes.Business) throw new ActorForbiddenException();
        var employee = (await employees.ResolveAsync(external, token)).EmployeeId ?? throw new ActorForbiddenException();
        var current = await roles.ResolveAsync(employee, token);
        if (current.EmployeeId != employee || !current.EmployeeExists || !current.EmployeeIsActive) throw new ActorForbiddenException();
        var fd = current.HasExactRole(ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode);
        var manage = fd || current.HasExactRole(ExpenseRequestWorkflowCodes.FinancialControllerRoleCode);
        var scope = await actor.ResolveBusinessDataReadScopeAsync(token);
        if (!scope.HasAny) throw new ActorForbiddenException();
        return (employee, external, manage, fd, scope);
    }
    public Task<CompanyBudgetVersionPageDto> ListAsync(int skip, int take, CancellationToken token) =>
        transactions.ExecuteAsync(async ct =>
        {
            var a = await AuthorityAsync(ct);
            Page(skip, take);
            return await store.ListAsync(skip, take, a.Scope, a.Manage, a.Activate, ct);
        }, token);
    public Task<CompanyBudgetOptionsDto> OptionsAsync(string query, Guid? center, int skip, int take, CancellationToken token) =>
        transactions.ExecuteAsync(async ct =>
        {
            if (query.Length > 200 || query.Any(char.IsControl) || center == Guid.Empty) throw new ArgumentException("Invalid options filter.");
            var a = await AuthorityAsync(ct);
            Page(skip, take);
            if (center.HasValue && !a.Scope.Contains(center.Value)) throw new ActorForbiddenException();
            return await store.OptionsAsync(query.Trim(), center, skip, take, a.Scope, a.Manage, a.Activate, ct);
        }, token);
    public Task<CompanyBudgetVersionDto> ReadAsync(Guid id, CancellationToken token) =>
        transactions.ExecuteAsync(async ct =>
        {
            var a = await AuthorityAsync(ct);
            return await store.ReadAsync(id, a.Scope, a.Manage, a.Activate, ct);
        }, token);

    public Task<CompanyBudgetVersionDto> CreateAsync(CreateCompanyBudgetVersionCommand command, string key, CancellationToken token)
    {
        CompanyBudgetCompositionPolicy.Validate(command.Name, command.BudgetInstanceIds);
        return MutateAsync(Guid.NewGuid(), "create", command, key, false, 201,
            (id, employee, external, now, ct) => store.CreateAsync(id, command, employee, external, now, ct), token);
    }
    public Task<IReadOnlyList<CompanyBudgetVersionHistoryDto>> HistoryAsync(Guid id, int skip, int take, CancellationToken token) =>
        transactions.ExecuteAsync(async ct =>
        {
            var a = await AuthorityAsync(ct);
            if (!a.Manage || !a.Scope.CanReadAll) throw new ActorForbiddenException();
            Page(skip, take);
            if (id == Guid.Empty) throw new ArgumentException("Invalid version identity.");
            await store.ReadAsync(id, a.Scope, a.Manage, a.Activate, ct);
            return await store.HistoryAsync(id, skip, take, ct);
        }, token);
    public Task<CompanyBudgetVersionDto> ReviseAsync(Guid sourceId, ReviseCompanyBudgetVersionCommand command,
        string key, CancellationToken token)
    {
        CompanyBudgetCompositionPolicy.Validate(command.Name, command.BudgetInstanceIds);
        if (sourceId == Guid.Empty || command.ExpectedConcurrencyStamp == Guid.Empty) throw new CompanyBudgetVersionConflictException();
        return MutateAsync(Guid.NewGuid(), "RevisionCreated", command, key, false, 201,
            (id, employee, external, now, ct) => store.CreateAsync(id, new(command.Name, command.BudgetInstanceIds), employee, external, now, ct),
            token, sourceId);
    }
    public Task<CompanyBudgetVersionDto> UpdateAsync(Guid id, UpdateCompanyBudgetVersionCommand command, string key, CancellationToken token)
    {
        CompanyBudgetCompositionPolicy.Validate(command.Name, command.BudgetInstanceIds);
        if (command.ExpectedConcurrencyStamp == Guid.Empty) throw new CompanyBudgetVersionConflictException();
        return MutateAsync(id, "update", command, key, false, 200,
            (version, _, _, _, ct) => store.UpdateAsync(version, command, ct), token);
    }
    public Task<CompanyBudgetVersionDto> ActivateAsync(Guid id, ActivateCompanyBudgetVersionCommand command, string key, CancellationToken token)
    {
        if (command.ExpectedConcurrencyStamp == Guid.Empty || command.ExpectedActiveConcurrencyStamp == Guid.Empty)
            throw new CompanyBudgetVersionConflictException();
        return MutateAsync(id, "activate", command, key, true, 200,
            (version, employee, external, now, ct) => store.ActivateAsync(version, command, employee, external, now, ct), token);
    }
    public Task<CompanyBudgetVersionDto> UpdateActiveCompositionAsync(Guid id, UpdateActiveCompanyBudgetCompositionCommand command,
        string key, CancellationToken token)
    {
        CompanyBudgetCompositionPolicy.Validate("Active composition", command.BudgetInstanceIds);
        if (command.ExpectedConcurrencyStamp == Guid.Empty || command.ExpectedActiveConcurrencyStamp == Guid.Empty)
            throw new CompanyBudgetVersionConflictException();
        return MutateAsync(id, "ActiveCompositionUpdated", command, key, true, 200,
            (version, employee, external, now, ct) => store.UpdateActiveCompositionAsync(version, command, employee, external, now, ct), token);
    }
    public Task<CompanyBudgetVersionDeletionReceipt> DeleteAsync(Guid id, DeleteCompanyBudgetVersionCommand command,
        string key, CancellationToken token)
    {
        if (command.ExpectedConcurrencyStamp == Guid.Empty) throw new CompanyBudgetVersionConflictException();
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(key) || key.Length > 127 || key != key.Trim() || key.Any(char.IsControl))
            throw new InvalidIdempotencyKeyException("Invalid company-version command identity.");
        return transactions.ExecuteCompanyBudgetMutationAsync(async ct =>
        {
            var a = await AuthorityAsync(ct);
            if (!a.Manage || !a.Scope.CanReadAll) throw new ActorForbiddenException();
            if (command.ReplacementVersionId.HasValue && !a.Activate) throw new ActorForbiddenException();
            var scope = $"company-budget:v1:delete:{id:N}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(a.External)))}";
            // Preserve the original inactive-delete idempotency bytes across upgrades.
            var hash = Convert.ToHexString(SHA256.HashData(command.ReplacementVersionId is null
                && command.ExpectedReplacementConcurrencyStamp is null && command.ExpectedActiveConcurrencyStamp is null
                ? JsonSerializer.SerializeToUtf8Bytes(new { command.ExpectedConcurrencyStamp }, Json)
                : JsonSerializer.SerializeToUtf8Bytes(command, Json)));
            var stored = await idempotency.FindAsync(scope, key, ct);
            if (stored is not null)
            {
                if (stored.RequestHash != hash) throw new IdempotencyConflictException();
                if (stored.ResponseStatusCode != 200) throw new CompanyBudgetPlanUnavailableException();
                // Successful retries cannot read the now-absent version. The
                // stored, strictly validated confirmation is the response.
                return ReadDeletionReceipt(stored.ResponseJson, id);
            }
            var before = await store.ReadAsync(id, a.Scope, a.Manage, a.Activate, ct);
            if (before.ConcurrencyStamp != command.ExpectedConcurrencyStamp || !before.CanDelete
                || before.IsPartial || before.ScopeLimited)
                throw new CompanyBudgetVersionConflictException();
            var now = clock.UtcNow;
            if (now == default || now.Offset != TimeSpan.Zero) throw new CompanyBudgetPlanUnavailableException();
            var correlation = Guid.NewGuid().ToString("N");
            if (before.IsActive)
            {
                if (!a.Activate) throw new ActorForbiddenException();
                if (command.ReplacementVersionId is not Guid replacementId || replacementId == Guid.Empty || replacementId == id
                    || command.ExpectedReplacementConcurrencyStamp is not Guid replacementStamp || replacementStamp == Guid.Empty
                    || command.ExpectedActiveConcurrencyStamp != before.ActiveConcurrencyStamp)
                    throw new CompanyBudgetVersionConflictException();
                var replacement = await store.ReadAsync(replacementId, a.Scope, a.Manage, a.Activate, ct);
                if (!replacement.IsPublished || replacement.IsActive || !replacement.CanActivate || replacement.IsPartial
                    || replacement.ScopeLimited || replacement.InstanceCount == 0 || replacement.ConcurrencyStamp != replacementStamp
                    || replacement.ActiveVersionId != id || replacement.ActiveConcurrencyStamp != before.ActiveConcurrencyStamp
                    || replacement.Instances.Any(x => x.Status != "Approved")) throw new CompanyBudgetVersionConflictException();
                await store.ActivateAsync(replacementId, new(replacementStamp, before.ActiveConcurrencyStamp), a.Employee, a.External, now, ct);
                await work.SaveChangesAsync(ct);
                var activated = await store.ReadAsync(replacementId, a.Scope, a.Manage, a.Activate, ct);
                if (!activated.IsActive) throw new CompanyBudgetPlanUnavailableException();
                var activatedJson = JsonSerializer.Serialize(activated, Json);
                audit.Append(Guid.NewGuid(), now, a.External, "CompanyBudgetVersion", replacementId.ToString("D"),
                    "CompanyBudgetVersionactivate", JsonSerializer.Serialize(replacement, Json), activatedJson, correlation);
                outbox.Enqueue(Guid.NewGuid(), now, "CompanyBudgetVersionactivate", activatedJson, correlation);
            }
            else if (command.ReplacementVersionId.HasValue || command.ExpectedReplacementConcurrencyStamp.HasValue
                || command.ExpectedActiveConcurrencyStamp.HasValue) throw new CompanyBudgetVersionConflictException();
            var receipt = await store.DeleteAsync(id, command, a.Employee, a.External, now, ct);
            var json = JsonSerializer.Serialize(receipt, Json);
            var confirmed = ReadDeletionReceipt(json, id);
            if (confirmed.Name != before.Name || confirmed.RetainedInstanceCount != before.InstanceCount)
                throw new CompanyBudgetPlanUnavailableException();
            audit.Append(Guid.NewGuid(), now, a.External, "CompanyBudgetVersion", id.ToString("D"),
                "CompanyBudgetVersionDeleted", JsonSerializer.Serialize(before, Json), json, correlation);
            outbox.Enqueue(Guid.NewGuid(), now, "CompanyBudgetVersionDeleted", json, correlation);
            idempotency.Add(scope, key, hash, 200, json, now);
            await work.SaveChangesAsync(ct);
            return confirmed;
        }, token);
    }
    private static CompanyBudgetVersionDeletionReceipt ReadDeletionReceipt(string json, Guid id)
    {
        CompanyBudgetResponseBudget.ValidateStoredJson(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new CompanyBudgetPlanUnavailableException();
            var names = document.RootElement.EnumerateObject().Select(x => x.Name).ToArray();
            if (names.Length != 4 || names.Distinct(StringComparer.Ordinal).Count() != 4
                || names.Any(x => x is not ("id" or "name" or "deleted" or "retainedInstanceCount")))
                throw new CompanyBudgetPlanUnavailableException();
            var receipt = JsonSerializer.Deserialize<CompanyBudgetVersionDeletionReceipt>(json, Json);
            if (receipt is null || receipt.Id != id || !receipt.Deleted || receipt.RetainedInstanceCount is < 0 or > 1000
                || string.IsNullOrWhiteSpace(receipt.Name) || receipt.Name.Length > 200
                || receipt.Name != receipt.Name.Trim() || receipt.Name.Any(char.IsControl))
                throw new CompanyBudgetPlanUnavailableException();
            return receipt;
        }
        catch (JsonException) { throw new CompanyBudgetPlanUnavailableException(); }
    }
    private Task<CompanyBudgetVersionDto> MutateAsync<T>(Guid id, string action, T command, string key, bool activating, int status,
        Func<Guid, Guid, string, DateTimeOffset, CancellationToken, Task> mutate, CancellationToken token, Guid? sourceId = null)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(key) || key.Length > 127 || key != key.Trim() || key.Any(char.IsControl))
            throw new InvalidIdempotencyKeyException("Invalid company-version command identity.");
        return transactions.ExecuteCompanyBudgetMutationAsync(async ct =>
        {
            var a = await AuthorityAsync(ct);
            if (!a.Manage || (activating && !a.Activate) || !a.Scope.CanReadAll) throw new ActorForbiddenException();
            var scope = $"company-budget:v1:{action}:{(sourceId.HasValue ? sourceId.Value.ToString("N") : action == "create" ? "new" : id.ToString("N"))}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(a.External)))}";
            var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(command, Json)));
            var stored = await idempotency.FindAsync(scope, key, ct);
            if (stored is not null)
            {
                if (stored.RequestHash != hash) throw new IdempotencyConflictException();
                CompanyBudgetResponseBudget.ValidateStoredJson(stored.ResponseJson);
                return JsonSerializer.Deserialize<CompanyBudgetVersionDto>(stored.ResponseJson, Json)
                    ?? throw new CompanyBudgetPlanUnavailableException();
            }
            var before = action == "create" ? null : await store.ReadAsync(sourceId ?? id, a.Scope, a.Manage, a.Activate, ct);
            if (command is UpdateActiveCompanyBudgetCompositionCommand activeCommand && (before is null
                || !before.IsActive || !before.IsPublished || !before.CanEditActiveComposition || before.IsPartial || before.ScopeLimited
                || before.ConcurrencyStamp != activeCommand.ExpectedConcurrencyStamp
                || before.ActiveConcurrencyStamp != activeCommand.ExpectedActiveConcurrencyStamp))
                throw new CompanyBudgetVersionConflictException();
            if (sourceId.HasValue && (command is not ReviseCompanyBudgetVersionCommand revision || before is null
                || before.Id != sourceId.Value || before.ConcurrencyStamp != revision.ExpectedConcurrencyStamp
                || !before.IsPublished || before.IsPartial || before.ScopeLimited))
                throw new CompanyBudgetVersionConflictException();
            var now = clock.UtcNow;
            if (now == default || now.Offset != TimeSpan.Zero) throw new CompanyBudgetPlanUnavailableException();
            await mutate(id, a.Employee, a.External, now, ct);
            await work.SaveChangesAsync(ct);
            var after = await store.ReadAsync(id, a.Scope, a.Manage, a.Activate, ct);
            CompanyBudgetResponseBudget.Validate(after, ct);
            if (command is UpdateActiveCompanyBudgetCompositionCommand composition && (before is null
                || after.Id != before.Id || after.Name != before.Name || !after.IsActive || !after.IsPublished
                || after.IsPartial || after.ScopeLimited || !after.CanEditActiveComposition
                || after.ConcurrencyStamp == before.ConcurrencyStamp || after.ActiveConcurrencyStamp == before.ActiveConcurrencyStamp
                || !after.BudgetInstanceIds.Order().SequenceEqual(composition.BudgetInstanceIds.Order())
                || after.Instances.Any(x => x.Status != "Approved") || after.Overlaps.Any(x => !x.WinnerBudgetInstanceId.HasValue)))
                throw new CompanyBudgetPlanUnavailableException();
            if (sourceId.HasValue && (after.Id == sourceId.Value || after.Id != id || after.IsPublished || after.IsActive
                || after.IsPartial || after.ScopeLimited || !after.CanEdit)) throw new CompanyBudgetPlanUnavailableException();
            var json = JsonSerializer.Serialize(after, Json);
            var correlation = Guid.NewGuid().ToString("N");
            audit.Append(Guid.NewGuid(), now, a.External, "CompanyBudgetVersion", id.ToString("D"),
                $"CompanyBudgetVersion{action}", before is null ? null : JsonSerializer.Serialize(before, Json), json, correlation);
            outbox.Enqueue(Guid.NewGuid(), now, $"CompanyBudgetVersion{action}", json, correlation);
            idempotency.Add(scope, key, hash, status, json, now);
            await work.SaveChangesAsync(ct);
            return after;
        }, token);
    }
}
