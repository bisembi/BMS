using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.ReferenceData;

namespace Bms.Application.ReferenceData;

/// <summary>
/// Shared CRUD boundary for reference data.  Only FIN_CONTROLLER,
/// FIN_DIRECTOR, GENERAL_DIRECTOR and the active BMS system-administrator
/// profile reach this service through <see cref="ICurrentActor"/>.  Every
/// mutation is transactional and produces audit/outbox evidence.
/// </summary>
public sealed class ReferenceDataMaintenanceService(
    IReferenceDataMaintenanceStore store,
    ICurrentActor actor,
    IBusinessTransactionRunner transactions,
    IIdempotencyStore idempotency,
    IBusinessAuditSink audit,
    IOutboxSink outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Directories = new(StringComparer.OrdinalIgnoreCase)
    {
        "organizations", "cost-centers", "cash-flow-items", "expense-items",
        "counterparties", "contracts", "currencies",
    };

    public async Task<ReferenceDataMaintenanceItem> CreateAsync(
        string directoryId,
        ReferenceDataMaintenanceCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireReferenceDataManagement(actor);
        var normalized = Normalize(command, directoryId, allowHistoricalCreate: true);
        var key = RequireIdempotencyKey(idempotencyKey);
        var actorHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(externalId)));
        var scope = $"reference-data:maintenance-create:v1:{normalized.DirectoryId}:{actorHash}";
        var requestHash = Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(normalized, JsonOptions)));

        try
        {
            return await transactions.ExecuteAsync(async token =>
            {
                await ActorIdentity.RequireCurrentReferenceDataManagementAsync(actor, externalId, token);
                var replay = await idempotency.FindAsync(scope, key, token);
                if (replay is not null)
                {
                    if (!string.Equals(replay.RequestHash, requestHash, StringComparison.Ordinal))
                        throw new IdempotencyConflictException();
                    return JsonSerializer.Deserialize<ReferenceDataMaintenanceItem>(
                               replay.ResponseJson, JsonOptions)
                           ?? throw new ReferenceDataMaintenanceUnavailableException();
                }

                ReferenceDataMaintenanceItem after;
                try
                {
                    after = await store.CreateAsync(normalized, token);
                }
                catch (ReferenceDataMaintenanceNotFoundException)
                { throw; }
                catch (ReferenceDataMaintenanceConflictException)
                { throw; }

                var now = RequireUtcNow();
                var correlation = Correlation(scope, key);
                var afterJson = JsonSerializer.Serialize(after, JsonOptions);
                audit.Append(Guid.NewGuid(), now, externalId, "ReferenceData",
                    $"{after.DirectoryId}:{after.Id:D}", "ReferenceDataCreated", "{}", afterJson, correlation);
                outbox.Enqueue(Guid.NewGuid(), now, "ReferenceDataCreated", afterJson, correlation);
                idempotency.Add(scope, key, requestHash, 200, afterJson, now);
                await unitOfWork.SaveChangesAsync(token);
                return after;
            }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (ActorNotAuthenticatedException) { throw; }
        catch (ActorForbiddenException) { throw; }
        catch (ReferenceDataMaintenanceNotFoundException) { throw; }
        catch (ReferenceDataMaintenanceConflictException) { throw; }
        catch (IdempotencyConflictException) { throw; }
        catch (InvalidIdempotencyKeyException) { throw; }
        catch (DomainRuleViolationException) { throw; }
        catch (ReferenceDataMaintenanceUnavailableException) { throw; }
        catch (Exception exception) { throw new ReferenceDataMaintenanceUnavailableException(exception); }
    }

    public async Task<ReferenceDataMaintenanceItem> UpdateAsync(
        string directoryId,
        Guid id,
        ReferenceDataMaintenanceCommand command,
        CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireReferenceDataManagement(actor);
        var normalized = Normalize(command, directoryId);
        if (id == Guid.Empty) throw new ReferenceDataMaintenanceNotFoundException();
        try
        {
            return await ExecuteMaintenanceAsync(directoryId, async token =>
            {
                await ActorIdentity.RequireCurrentReferenceDataManagementAsync(actor, externalId, token);
                var before = await store.UpdateAsync(directoryId, id, normalized, token);
                var now = RequireUtcNow();
                var correlation = Guid.NewGuid().ToString("N");
                var afterJson = JsonSerializer.Serialize(before, JsonOptions);
                audit.Append(Guid.NewGuid(), now, externalId, "ReferenceData",
                    $"{before.DirectoryId}:{before.Id:D}", "ReferenceDataUpdated", "{}", afterJson, correlation);
                outbox.Enqueue(Guid.NewGuid(), now, "ReferenceDataUpdated", afterJson, correlation);
                await unitOfWork.SaveChangesAsync(token);
                return before;
            }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (ActorNotAuthenticatedException) { throw; }
        catch (ActorForbiddenException) { throw; }
        catch (ReferenceDataMaintenanceNotFoundException) { throw; }
        catch (ReferenceDataMaintenanceConflictException) { throw; }
        catch (DomainRuleViolationException) { throw; }
        catch (ReferenceDataMaintenanceUnavailableException) { throw; }
        catch (Exception exception) { throw new ReferenceDataMaintenanceUnavailableException(exception); }
    }

    public async Task<ReferenceDataMaintenanceItem> DeleteAsync(
        string directoryId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireReferenceDataManagement(actor);
        EnsureDirectory(directoryId);
        if (id == Guid.Empty) throw new ReferenceDataMaintenanceNotFoundException();
        try
        {
            return await ExecuteMaintenanceAsync(directoryId, async token =>
            {
                await ActorIdentity.RequireCurrentReferenceDataManagementAsync(actor, externalId, token);
                var after = await store.DeleteAsync(directoryId, id, token);
                var now = RequireUtcNow();
                var correlation = Guid.NewGuid().ToString("N");
                var afterJson = JsonSerializer.Serialize(after, JsonOptions);
                audit.Append(Guid.NewGuid(), now, externalId, "ReferenceData",
                    $"{after.DirectoryId}:{after.Id:D}", "ReferenceDataDeleted", "{}", afterJson, correlation);
                outbox.Enqueue(Guid.NewGuid(), now, "ReferenceDataDeleted", afterJson, correlation);
                await unitOfWork.SaveChangesAsync(token);
                return after;
            }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (ActorNotAuthenticatedException) { throw; }
        catch (ActorForbiddenException) { throw; }
        catch (ReferenceDataMaintenanceNotFoundException) { throw; }
        catch (ReferenceDataMaintenanceConflictException) { throw; }
        catch (DomainRuleViolationException) { throw; }
        catch (ReferenceDataMaintenanceUnavailableException) { throw; }
        catch (Exception exception) { throw new ReferenceDataMaintenanceUnavailableException(exception); }
    }

    private Task<TResult> ExecuteMaintenanceAsync<TResult>(string directoryId,
        Func<CancellationToken, Task<TResult>> action, CancellationToken token) =>
        string.Equals(directoryId.Trim(), "cost-centers", StringComparison.OrdinalIgnoreCase)
            ? transactions.ExecuteOwnerAssignmentMutationAsync(action, token)
            : transactions.ExecuteAsync(action, token);

    internal static ReferenceDataMaintenanceCommand Normalize(
        ReferenceDataMaintenanceCommand command,
        string? directoryId,
        bool allowHistoricalCreate = false)
    {
        ArgumentNullException.ThrowIfNull(command);
        if ((command.FlowDirectionSpecified && !string.Equals(directoryId ?? command.DirectoryId, "cash-flow-items", StringComparison.OrdinalIgnoreCase))
            || (command.FlowDirectionSpecified ? command.FlowDirection is not (null or "Payment" or "Receipt") : command.FlowDirection is not null)
            || (command.IsGroup == true && command.FlowDirection is not null))
            throw new DomainRuleViolationException("The catalog flow direction command is invalid.");
        var code = command.Code?.Trim() ?? string.Empty;
        var name = command.Name?.Trim() ?? string.Empty;
        var parentName = command.ParentName?.Trim();
        if (string.IsNullOrEmpty(parentName)) parentName = null;
        var activityType = command.ActivityType?.Trim();
        if (string.IsNullOrEmpty(activityType)) activityType = null;
        var numericCode = command.NumericCode?.Trim() ?? string.Empty;
        var bin = command.Bin?.Trim();
        if (string.IsNullOrEmpty(bin)) bin = null;
        var target = directoryId?.Trim().ToLowerInvariant()
            ?? command.DirectoryId?.Trim().ToLowerInvariant()
            ?? throw new DomainRuleViolationException("The reference directory is invalid.");
        EnsureDirectory(target);
        if (command.HistoricalForActuals && (!allowHistoricalCreate || target != "cash-flow-items"
            || command.IsGroup != false || !command.ParentIdSpecified
            || command.ParentId is null || command.ParentId == Guid.Empty || parentName is not null
            || !CashFlowActivityType.IsDefined(activityType)))
            throw new DomainRuleViolationException("Historical actuals articles require explicit leaf, parent and activity classification on creation.");
        var (codeMax, nameMax) = target switch
        {
            "organizations" => (100, 500),
            "cost-centers" => (100, 500),
            "cash-flow-items" => (20, 150),
            "expense-items" => (9, 250),
            "counterparties" => (0, 150),
            "contracts" => (30, 150),
            "currencies" => (3, 50),
            _ => throw new DomainRuleViolationException("The reference directory is invalid."),
        };
        if (string.IsNullOrWhiteSpace(name) || name.Length > nameMax || name.Any(char.IsControl))
            throw new DomainRuleViolationException("The reference-data code or name is invalid.");
        if (parentName is not null && (parentName.Length > 500 || parentName.Any(char.IsControl)))
            throw new DomainRuleViolationException("The reference-data group name is invalid.");
        if (target == "currencies")
        {
            if (!CurrencyCodeResolver.IsNumericCode(numericCode))
                throw new DomainRuleViolationException("The currency numeric code is invalid.");
            code = CurrencyCodeResolver.ResolveOrThrow(code, name, numericCode);
        }
        else if (target == "counterparties")
        {
            code = string.Empty;
            if (bin is not null && !Regex.IsMatch(bin, "^\\d{12}$", RegexOptions.CultureInvariant))
                throw new DomainRuleViolationException("The counterparty BIN is invalid.");
        }
        else if (target == "cash-flow-items")
        {
            // Cash-flow codes are internal identifiers. They are generated by
            // the maintenance store and never accepted from the UI.
            code = string.Empty;
            if (activityType is not null && !CashFlowActivityType.IsDefined(activityType))
                throw new DomainRuleViolationException("The cash-flow activity type is invalid.");
        }
        else if (target == "expense-items" && allowHistoricalCreate)
        {
            // The shared create endpoint cannot bypass server numbering either.
            // Keep a valid legacy hint in the request fingerprint for old replays.
            if (code.Length > codeMax || code.Any(char.IsControl))
                throw new DomainRuleViolationException("The reference-data code is invalid.");
        }
        else if (string.IsNullOrWhiteSpace(code) || code.Length > codeMax || code.Any(char.IsControl))
        {
            throw new DomainRuleViolationException("The reference-data code or name is invalid.");
        }
        if (target == "contracts" && (command.CounterpartyId is null || command.OrganizationId is null
            || command.CurrencyId is null || command.ContractDate is null))
            throw new DomainRuleViolationException("Contract scope and date are required.");
        return command with
        {
            Code = code,
            Name = name,
            ParentName = target == "cash-flow-items" ? parentName : command.ParentName?.Trim(),
            ActivityType = target == "cash-flow-items" ? activityType : command.ActivityType?.Trim(),
            NumericCode = target == "currencies" ? numericCode : command.NumericCode?.Trim(),
            Bin = target == "counterparties" ? bin : command.Bin?.Trim(),
            DirectoryId = target,
        };
    }

    private static void EnsureDirectory(string directoryId)
    {
        if (!Directories.Contains(directoryId))
            throw new DomainRuleViolationException("The reference directory is not writable.");
    }

    private static string RequireIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 127)
            throw new InvalidIdempotencyKeyException("One idempotency key is required.");
        return value;
    }

    private DateTimeOffset RequireUtcNow()
    {
        var now = clock.UtcNow;
        if (now == default || now.Offset != TimeSpan.Zero)
            throw new ReferenceDataMaintenanceUnavailableException();
        return now;
    }

    private static string Correlation(string scope, string key) => new Guid(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { scope, key }, JsonOptions))
            .AsSpan(0, 16)).ToString("N");
}
