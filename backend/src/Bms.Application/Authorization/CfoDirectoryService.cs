using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.Workflow;

namespace Bms.Application.Authorization;

public sealed record EmployeeCfoAssignment(Guid EmployeeId, Guid CostCenterId, string CostCenterCode,
    string CostCenterName, string RoleCode, bool IsActive);
public sealed record CfoDirectoryOption(Guid Id, string Code, string Name);
public sealed record EmployeeCfoState(IReadOnlyList<EmployeeCfoAssignment> Assignments,
    IReadOnlyList<CfoDirectoryOption> CostCenters, string ConcurrencyStamp);
public sealed record WriteEmployeeCfoCommand(Guid CostCenterId, string RoleCode, bool IsActive,
    string ExpectedConcurrencyStamp);

public interface ICfoDirectoryStore
{
    Task<EmployeeCfoState> ReadAsync(Guid employeeId, CancellationToken token);
    Task<IReadOnlyList<EmployeeCfoAssignment>> ListAsync(CancellationToken token);
    Task LockAsync(Guid employeeId, Guid costCenterId, CancellationToken token);
    Task WriteAsync(Guid employeeId, WriteEmployeeCfoCommand command, CancellationToken token);
}

public sealed class CfoDirectoryService(ICfoDirectoryStore store, BudgetDocumentAuthority authority,
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

    public async Task<EmployeeCfoState> GetAsync(Guid employeeId, CancellationToken token)
    {
        if (!await authority.CanReadDirectoryAsync(actor, token)) throw new ActorForbiddenException();
        return await transactions.ExecuteAsync(async ct => {
            if (!await authority.CanReadDirectoryAsync(actor, ct)) throw new ActorForbiddenException();
            return await store.ReadAsync(employeeId, ct); }, token);
    }

    public async Task<IReadOnlyList<EmployeeCfoAssignment>> ListAsync(CancellationToken token)
    {
        if (!await authority.CanReadDirectoryAsync(actor, token)) throw new ActorForbiddenException();
        return await transactions.ExecuteAsync(async ct => {
            if (!await authority.CanReadDirectoryAsync(actor, ct)) throw new ActorForbiddenException();
            return await store.ListAsync(ct); }, token);
    }

    public async Task<EmployeeCfoState> WriteAsync(Guid employeeId, WriteEmployeeCfoCommand command, string key, CancellationToken token)
    {
        var external = await RequireAsync(token);
        if (employeeId == Guid.Empty || command.CostCenterId == Guid.Empty
            || command.RoleCode is not (ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode or ExpenseRequestWorkflowCodes.CfoInitiatorRoleCode)
            || string.IsNullOrWhiteSpace(command.ExpectedConcurrencyStamp))
            throw new DomainRuleViolationException("Choose an employee, CFO and exact assignment role.");
        if (string.IsNullOrWhiteSpace(key) || key.Length > 127 || key != key.Trim() || key.Any(char.IsControl))
            throw new InvalidIdempotencyKeyException("Invalid idempotency key.");
        var scope = $"employee-cfo:v1:{employeeId:N}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(external)))}";
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(command, Json)));
        return await transactions.ExecuteOwnerAssignmentMutationAsync(async ct =>
        {
            await store.LockAsync(employeeId, command.CostCenterId, ct);
            await RequireAsync(ct);
            var before = await store.ReadAsync(employeeId, ct);
            var replay = await idempotency.FindAsync(scope, key, ct);
            if (replay is not null)
            {
                if (replay.RequestHash != hash) throw new IdempotencyConflictException();
                return JsonSerializer.Deserialize<EmployeeCfoState>(replay.ResponseJson, Json)
                    ?? throw new AuthorizationDirectoryUnavailableException();
            }
            if (before.ConcurrencyStamp != command.ExpectedConcurrencyStamp) throw new AuthorizationDirectoryStateConflictException();
            await store.WriteAsync(employeeId, command, ct);
            await unitOfWork.SaveChangesAsync(ct);
            var after = await store.ReadAsync(employeeId, ct);
            var response = JsonSerializer.Serialize(after, Json);
            var auditId = Guid.NewGuid();
            var correlation = auditId.ToString("N");
            audit.Append(auditId, clock.UtcNow, external, "Employee", employeeId.ToString("D"),
                "EmployeeCfoAssignmentChanged", JsonSerializer.Serialize(before, Json), response, correlation);
            outbox.Enqueue(Guid.NewGuid(), clock.UtcNow, "EmployeeCfoAssignmentChanged", response, correlation);
            idempotency.Add(scope, key, hash, 200, response, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return after;
        }, token);
    }
}
