using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.ReferenceData;

namespace Bms.Application.ReferenceData;

/// <summary>
/// Applies only the article's settings. Current authority, version comparison,
/// audit, outbox and the recoverable response share one Serializable commit.
/// Submitted request snapshots are never loaded or rewritten by this service.
/// </summary>
public sealed partial class CashFlowAccrualSettingsService(
    ICashFlowAccrualSettingsStore store, CashFlowAccrualSettingsAuthority authority,
    ICurrentActor actor, IBusinessTransactionRunner transactions,
    IIdempotencyStore idempotency, IBusinessAuditSink audit, IOutboxSink outbox,
    IUnitOfWork unitOfWork, IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CashFlowAccrualSettingsDto> ReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        try
        {
            return await transactions.ExecuteAsync(async token =>
            {
                var current = await ReadVisibleAsync(id, token);
                var editor = await authority.ResolveAsync(externalId, token);
                return ToDto(current, editor is not null && !current.IsGroup);
            }, cancellationToken);
        }
        catch (Exception exception) when (MustWrap(exception))
        {
            throw new CashFlowAccrualSettingsUnavailableException(exception);
        }
    }

    public async Task<CashFlowAccrualSettingsCommandResult> UpdateAsync(Guid id,
        UpdateCashFlowAccrualSettingsCommand command, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        ArgumentNullException.ThrowIfNull(command);
        if (id == Guid.Empty) throw new CashFlowAccrualSettingsNotFoundException();
        RequireKey(idempotencyKey);
        if (command.ExpectedVersion == Guid.Empty)
            throw new DomainRuleViolationException("An expected settings version is required.");
        if (command.NameSpecified
            ? !IsText(command.Name, 150) || command.Name != command.Name?.Trim()
            : command.Name is not null)
            throw new DomainRuleViolationException("The article name command is invalid.");
        if (command.FlowDirectionSpecified
            ? command.FlowDirection is not (null or "Payment" or "Receipt")
            : command.FlowDirection is not null)
            throw new DomainRuleViolationException("The catalog flow direction command is invalid.");
        var settings = new CashFlowAccrualSettings(command.FormsManagerialAccrual, command.ExpenseItemId);
        if (command.CostCenterId == Guid.Empty || (command.CostCenterId is not null && !command.CostCenterIdSpecified))
            throw new DomainRuleViolationException("The article cost center command is invalid.");
        if (command.ClassificationSpecified
            ? !CashFlowActivityType.IsDefined(command.ActivityType)
                || (command.GroupName is not null && (!IsText(command.GroupName, 150) || command.GroupName != command.GroupName.Trim()))
            : command.ActivityType is not null || command.GroupName is not null)
            throw new DomainRuleViolationException("The article classification command is invalid.");
        // Preserve all deployed v1, CFO-v2 and classification-v3 bytes. Only
        // a name command uses the extended record; legacy v3 must not acquire
        // new null/false members, even during recovery after an upgrade.
        var requestBytes = command.FlowDirectionSpecified
            ? JsonSerializer.SerializeToUtf8Bytes(command, JsonOptions)
            : command.NameSpecified
            ? JsonSerializer.SerializeToUtf8Bytes(new
            {
                command.ExpectedVersion, command.FormsManagerialAccrual, command.ExpenseItemId,
                command.CostCenterId, command.CostCenterIdSpecified,
                command.ActivityType, command.GroupName, command.ClassificationSpecified,
                command.Name, command.NameSpecified
            }, JsonOptions)
            : command.ClassificationSpecified
            ? JsonSerializer.SerializeToUtf8Bytes(new
            {
                command.ExpectedVersion, command.FormsManagerialAccrual, command.ExpenseItemId,
                command.CostCenterId, command.CostCenterIdSpecified,
                command.ActivityType, command.GroupName, command.ClassificationSpecified
            }, JsonOptions)
            : command.CostCenterIdSpecified
            ? JsonSerializer.SerializeToUtf8Bytes(new
            {
                command.ExpectedVersion, command.FormsManagerialAccrual, command.ExpenseItemId,
                command.CostCenterId, command.CostCenterIdSpecified
            }, JsonOptions)
            : JsonSerializer.SerializeToUtf8Bytes(new
            {
                command.ExpectedVersion,
                command.FormsManagerialAccrual,
                command.ExpenseItemId
            }, JsonOptions);
        var requestHash = Convert.ToHexString(SHA256.HashData(requestBytes));
        try
        {
            return await transactions.ExecuteAsync(async token =>
            {
                // An actor-to-employee reassignment must not recover another
                // employee's command. Roles are checked anew even on a replay.
                var editor = await authority.ResolveAsync(externalId, token) ?? throw new ActorForbiddenException();
                var current = await ReadVisibleAsync(id, token);
                if (current.IsGroup)
                    throw new DomainRuleViolationException("A group has no accrual settings command.");
                var actorHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(externalId)));
                var scope = $"cashflow-accrual:v1:{id:N}:{editor.EmployeeId:N}:{actorHash}";
                var stored = await idempotency.FindAsync(scope, idempotencyKey, token);
                if (stored is not null)
                {
                    if (!string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal))
                        throw new IdempotencyConflictException();
                    return ReadReplay(stored, id, editor.EmployeeId, command);
                }
                if (current.Version != command.ExpectedVersion)
                    throw new CashFlowAccrualSettingsVersionConflictException();

                var costCenterId = command.CostCenterIdSpecified ? command.CostCenterId : current.CostCenterId;
                var costCenter = current.CostCenter;
                if (command.CostCenterIdSpecified)
                {
                    costCenter = costCenterId is { } requestedCenter
                        ? await store.FindCostCenterAsync(requestedCenter, token)
                            ?? throw new DomainRuleViolationException("Select an active non-group cost center.") : null;
                    if (costCenter?.Id != costCenterId) throw new CashFlowAccrualSettingsUnavailableException();
                }
                var resolvedSettings = new CashFlowAccrualSettings(settings.FormsManagerialAccrual,
                    settings.ExpenseItemId, costCenterId);

                ReferenceDataItemDto? expenseItem = null;
                if (settings.ExpenseItemId is { } expenseId)
                {
                    expenseItem = await store.FindExpenseItemAsync(expenseId, token)
                        ?? throw new DomainRuleViolationException("The selected expense item does not exist.");
                    if (expenseItem.Id != expenseId) throw new CashFlowAccrualSettingsUnavailableException();
                }
                var changed = current.FormsManagerialAccrual != settings.FormsManagerialAccrual
                    || (command.FlowDirectionSpecified && current.FlowDirection != command.FlowDirection)
                    || current.ExpenseItemId != settings.ExpenseItemId || current.CostCenterId != costCenterId
                    || (command.NameSpecified && !string.Equals(current.Name, command.Name, StringComparison.Ordinal))
                    || (command.ClassificationSpecified && (current.ActivityType != command.ActivityType
                        || !string.Equals(current.GroupName, command.GroupName, StringComparison.OrdinalIgnoreCase)));
                var after = current with
                {
                    Name = command.NameSpecified ? command.Name! : current.Name,
                    FlowDirection = command.FlowDirectionSpecified ? command.FlowDirection : current.FlowDirection,
                    FormsManagerialAccrual = settings.FormsManagerialAccrual,
                    ExpenseItemId = settings.ExpenseItemId,
                    ExpenseItem = expenseItem,
                    CostCenterId = costCenterId,
                    CostCenter = costCenter,
                    Version = changed ? Guid.NewGuid() : current.Version
                };
                var now = clock.UtcNow;
                if (now == default || now.Offset != TimeSpan.Zero)
                    throw new CashFlowAccrualSettingsUnavailableException();
                if (changed)
                {
                    await store.StageChangeAsync(current, resolvedSettings, after.Version, token);
                    if (command.FlowDirectionSpecified)
                        await store.StageFlowDirectionAsync(current, after.Version, after.FlowDirection, token);
                    if (command.NameSpecified)
                        await store.StageNameAsync(current, after.Version, after.Name, token);
                    if (command.ClassificationSpecified)
                    {
                        var parent = await store.StageClassificationAsync(current, after.Version,
                            command.ActivityType ?? throw new DomainRuleViolationException("Activity type is required."),
                            command.GroupName, token);
                        after = after with { ActivityType = command.ActivityType, ParentId = parent?.Id, GroupName = parent?.Name };
                    }
                    var correlationId = new Guid(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                        new { scope, key = idempotencyKey }, JsonOptions)).AsSpan(0, 16)).ToString("N");
                    var payload = JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        executorEmployeeId = editor.EmployeeId,
                        functionalRole = editor.RoleCode,
                        settings = after
                    }, JsonOptions);
                    audit.Append(Guid.NewGuid(), now, externalId, "CashFlowItem", id.ToString("D"),
                        "CashFlowAccrualSettingsChanged", JsonSerializer.Serialize(current, JsonOptions),
                        payload, correlationId);
                    outbox.Enqueue(Guid.NewGuid(), now, "CashFlowAccrualSettingsChanged", payload, correlationId);
                }
                var response = ToDto(after, canEdit: true);
                idempotency.Add(scope, idempotencyKey, requestHash, 200,
                    JsonSerializer.Serialize(new StoredSettingsResponse(editor.EmployeeId,
                        command.ExpectedVersion, response, changed), JsonOptions), now);
                try { await unitOfWork.SaveChangesAsync(token); }
                catch (ExpenseRequestVersionConflictException exception)
                {
                    // The shared unit-of-work maps EF optimistic concurrency
                    // to this legacy type; expose the article-specific conflict.
                    throw new CashFlowAccrualSettingsVersionConflictException(exception);
                }
                return new CashFlowAccrualSettingsCommandResult(response, changed, false);
            }, cancellationToken);
        }
        catch (Exception exception) when (MustWrap(exception))
        {
            throw new CashFlowAccrualSettingsUnavailableException(exception);
        }
    }
}
