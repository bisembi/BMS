using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.ReferenceData;

namespace Bms.Application.ReferenceData;

public sealed partial class CashFlowCatalogOrderService(
    ICashFlowCatalogOrderStore store,
    CashFlowAccrualSettingsAuthority authority,
    ICurrentActor actor,
    IBusinessTransactionRunner transactions,
    IIdempotencyStore idempotency,
    IBusinessAuditSink audit,
    IOutboxSink outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private const int MaximumRows = 10_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CashFlowCatalogOrderSnapshotDto> ReadAsync(
        CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        try
        {
            return await transactions.ExecuteAsync(async token =>
            {
                var data = await store.ReadAsync(false, token);
                var editor = await authority.ResolveAsync(externalId, token);
                return Snapshot(data.Rows, data.SupportsDisplayOrder && editor is not null);
            }, cancellationToken);
        }
        catch (Exception exception) when (MustWrap(exception))
        {
            throw new CashFlowCatalogOrderUnavailableException(exception);
        }
    }

    public async Task<CashFlowCatalogOrderCommandResult> MoveAsync(
        MoveCashFlowCatalogOrderCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command);
        RequireKey(idempotencyKey);
        var requestHash = Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(command, JsonOptions)));

        try
        {
            return await transactions.ExecuteAsync(async token =>
            {
                // Current authority is re-evaluated before idempotent recovery.
                var editor = await authority.ResolveAsync(externalId, token)
                    ?? throw new ActorForbiddenException();
                var actorHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(externalId)));
                var scope = $"cashflow-order:v1:{editor.EmployeeId:N}:{actorHash}";
                var stored = await idempotency.FindAsync(scope, idempotencyKey, token);
                if (stored is not null)
                {
                    if (!string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal))
                        throw new IdempotencyConflictException();
                    return ReadReplay(stored, editor.EmployeeId, command);
                }

                var before = await store.ReadAsync(true, token);
                if (!before.SupportsDisplayOrder)
                    throw new CashFlowCatalogOrderUnavailableException();
                var beforeSnapshot = Snapshot(before.Rows, true);
                if (!string.Equals(beforeSnapshot.Version, command.ExpectedVersion, StringComparison.Ordinal))
                    throw new CashFlowCatalogOrderVersionConflictException();

                var ordered = Move(before.Rows, command);
                var beforeById = before.Rows.ToDictionary(row => row.Id);
                var changes = ordered.Select((row, rank) => new CashFlowCatalogDisplayOrderChange(row.Id, rank))
                    .Where(change => beforeById[change.Id].DisplayOrder != change.DisplayOrder)
                    .ToArray();
                if (changes.Length > 0)
                    await store.StageDisplayOrdersAsync(changes, token);

                // Read back the same locked catalogue so the command response and
                // a subsequent GET use one public snapshot contract.
                var after = await store.ReadAsync(false, token);
                var afterSnapshot = Snapshot(after.Rows, true);
                if (changes.Length > 0
                    && string.Equals(beforeSnapshot.Version, afterSnapshot.Version, StringComparison.Ordinal))
                    throw new CashFlowCatalogOrderUnavailableException();

                var now = clock.UtcNow;
                if (now == default || now.Offset != TimeSpan.Zero)
                    throw new CashFlowCatalogOrderUnavailableException();
                if (changes.Length > 0)
                {
                    var correlationId = new Guid(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                        new { scope, key = idempotencyKey }, JsonOptions)).AsSpan(0, 16)).ToString("N");
                    var beforeJson = JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        version = beforeSnapshot.Version,
                        ranks = changes.Select(change => new
                        {
                            change.Id,
                            displayOrder = beforeById[change.Id].DisplayOrder
                        })
                    }, JsonOptions);
                    var payload = JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        executorEmployeeId = editor.EmployeeId,
                        functionalRole = editor.RoleCode,
                        command.SourceId,
                        command.TargetId,
                        command.Placement,
                        previousVersion = beforeSnapshot.Version,
                        version = afterSnapshot.Version,
                        ranks = changes
                    }, JsonOptions);
                    audit.Append(Guid.NewGuid(), now, externalId, "CashFlowCatalog", "cash-flow-items",
                        "CashFlowCatalogOrderChanged", beforeJson, payload, correlationId);
                    outbox.Enqueue(Guid.NewGuid(), now, "CashFlowCatalogOrderChanged", payload, correlationId);
                }

                var storedResponse = new StoredOrderResponse(
                    editor.EmployeeId,
                    command.ExpectedVersion,
                    command.SourceId,
                    command.TargetId,
                    command.Placement,
                    afterSnapshot,
                    changes.Length > 0);
                idempotency.Add(scope, idempotencyKey, requestHash, 200,
                    JsonSerializer.Serialize(storedResponse, JsonOptions), now);
                try
                {
                    await unitOfWork.SaveChangesAsync(token);
                }
                catch (ExpenseRequestVersionConflictException exception)
                {
                    throw new CashFlowCatalogOrderVersionConflictException(
                        "The cash-flow catalogue order changed.", exception);
                }
                return new CashFlowCatalogOrderCommandResult(afterSnapshot, changes.Length > 0, false);
            }, cancellationToken);
        }
        catch (Exception exception) when (MustWrap(exception))
        {
            throw new CashFlowCatalogOrderUnavailableException(exception);
        }
    }

    internal static string CanonicalGroupKey(
        string activityType,
        string? flowDirection,
        string? parentName)
    {
        if (parentName is null) return string.Empty;
        var value = CollapseWhitespace(parentName.Normalize(NormalizationForm.FormC));
        string prior;
        do
        {
            prior = value;
            value = CollapseWhitespace(HierarchyPrefixRegex().Replace(value, string.Empty));
            value = CollapseWhitespace(CashFlowPrefixRegex().Replace(value, string.Empty));
        }
        while (!string.Equals(prior, value, StringComparison.Ordinal));
        value = value.ToUpperInvariant();
        if (activityType == CashFlowActivityType.Operating
            && flowDirection == "Receipt"
            && value is "ПРОЧИЕ ПОСТУПЛЕНИЯ" or "ПРОЧИЕ ОПЕРАЦИОННЫЕ ПОСТУПЛЕНИЯ")
            return "ПРОЧИЕ ОПЕРАЦИОННЫЕ ПОСТУПЛЕНИЯ";
        return value;
    }

    private static CashFlowCatalogOrderSnapshotDto Snapshot(
        IReadOnlyList<CashFlowCatalogOrderRow> rows,
        bool canEdit)
    {
        var items = OrderRowsForDisplay(rows).Select(row => new CashFlowCatalogOrderItemDto(
            row.Id,
            row.Code,
            row.Name,
            row.ParentId,
            row.ParentCode,
            row.ParentName,
            row.ActivityType,
            row.FormsManagerialAccrual,
            row.AccrualExpenseItemName,
            row.CostCenterId,
            row.CostCenterName,
            row.FlowDirection,
            CanonicalGroupKey(row.ActivityType, row.FlowDirection, row.ParentName),
            row.DisplayOrder)).ToArray();
        return new CashFlowCatalogOrderSnapshotDto(ComputeVersion(rows), canEdit, items);
    }

    /// <summary>
    /// Shared public projection order for the full order snapshot and the
    /// schema-52 windowed reference-data page. Paging must happen only after
    /// the global article order has been computed. Membership is not an order constraint.
    /// </summary>
    public static IReadOnlyList<CashFlowCatalogOrderRow> OrderRowsForDisplay(
        IReadOnlyList<CashFlowCatalogOrderRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ValidateRows(rows);
        return OrderRows(rows);
    }

    internal static CashFlowCatalogOrderRow[] Move(
        IReadOnlyList<CashFlowCatalogOrderRow> rows,
        MoveCashFlowCatalogOrderCommand command)
    {
        var byId = rows.ToDictionary(row => row.Id);
        if (!byId.TryGetValue(command.SourceId, out var source)
            || !byId.TryGetValue(command.TargetId, out var target))
            throw new CashFlowCatalogOrderNotFoundException();
        var ordered = OrderRowsForDisplay(rows).ToList();
        MoveElement(ordered, source, target, command.Placement);
        return ordered.ToArray();
    }

    private static IReadOnlyList<CashFlowCatalogOrderRow> OrderRows(
        IReadOnlyList<CashFlowCatalogOrderRow> rows) =>
        rows.OrderBy(row => row.DisplayOrder.HasValue ? 0 : 1)
            .ThenBy(row => row.DisplayOrder)
            // Unranked imports follow the explicit order; deterministic legacy fallback.
            .ThenBy(row => ActivityOrder(row.ActivityType))
            .ThenBy(row => DirectionOrder(row.FlowDirection))
            .ThenBy(row => CanonicalGroupKey(row.ActivityType, row.FlowDirection, row.ParentName), StringComparer.Ordinal)
            .ThenBy(row => row.Code, StringComparer.Ordinal)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .ThenBy(row => row.Id).ToArray();

    private static void MoveElement<T>(List<T> values, T source, T target, string placement)
        where T : notnull
    {
        var sourceIndex = values.IndexOf(source);
        var targetIndex = values.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
            throw new DomainRuleViolationException("The cash-flow order move is invalid.");
        values.RemoveAt(sourceIndex);
        targetIndex = values.IndexOf(target);
        values.Insert(placement == "Before" ? targetIndex : targetIndex + 1, source);
    }

    private static int ActivityOrder(string activityType) => activityType switch
    {
        CashFlowActivityType.Operating => 0,
        CashFlowActivityType.Investing => 1,
        CashFlowActivityType.Financing => 2,
        _ => int.MaxValue
    };

    private static int DirectionOrder(string? flowDirection) => flowDirection switch
    {
        "Receipt" => 0,
        "Payment" => 1,
        null => 2,
        _ => int.MaxValue
    };

    private static string ComputeVersion(IReadOnlyList<CashFlowCatalogOrderRow> rows)
    {
        var canonical = rows.OrderBy(row => row.Id).Select(row => new
        {
            row.Id,
            row.Code,
            row.Name,
            row.ParentId,
            row.ParentCode,
            row.ParentName,
            row.ActivityType,
            row.FlowDirection,
            groupKey = CanonicalGroupKey(row.ActivityType, row.FlowDirection, row.ParentName),
            row.FormsManagerialAccrual,
            row.AccrualExpenseItemId,
            row.AccrualExpenseItemName,
            row.CostCenterId,
            row.CostCenterName,
            row.DisplayOrder
        });
        return Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new { orderPolicy = "individual-global-v2", rows = canonical }, JsonOptions)));
    }

    private static void ValidateCommand(MoveCashFlowCatalogOrderCommand command)
    {
        if (!IsVersion(command.ExpectedVersion)
            || command.SourceId == Guid.Empty
            || command.TargetId == Guid.Empty
            || command.SourceId == command.TargetId
            || command.Placement is not ("Before" or "After"))
            throw new DomainRuleViolationException("The cash-flow order command is invalid.");
    }

    private static void ValidateRows(IReadOnlyList<CashFlowCatalogOrderRow> rows)
    {
        if (rows.Count > MaximumRows
            || rows.Select(row => row.Id).Distinct().Count() != rows.Count)
            throw new CashFlowCatalogOrderUnavailableException();
        foreach (var row in rows)
        {
            var key = CanonicalGroupKey(row.ActivityType, row.FlowDirection, row.ParentName);
            if (row.Id == Guid.Empty
                || !IsText(row.Code, 20)
                || !IsText(row.Name, 150)
                || !CashFlowActivityType.IsDefined(row.ActivityType)
                || row.FlowDirection is not (null or "Payment" or "Receipt")
                || row.DisplayOrder < 0
                || (row.ParentId.HasValue != (row.ParentCode is not null && row.ParentName is not null))
                || (row.ParentCode is null != (row.ParentName is null))
                || (row.ParentId.HasValue && key.Length == 0)
                || (row.ParentCode is not null && !IsText(row.ParentCode, 20))
                || (row.ParentName is not null && !IsText(row.ParentName, 150))
                || (row.CostCenterId.HasValue != (row.CostCenterName is not null))
                || (row.CostCenterName is not null && !IsText(row.CostCenterName, 500))
                || (row.AccrualExpenseItemId.HasValue != (row.AccrualExpenseItemName is not null))
                || (row.FormsManagerialAccrual != row.AccrualExpenseItemId.HasValue)
                || (row.AccrualExpenseItemName is not null && !IsText(row.AccrualExpenseItemName, 250))
                || key.Length > 2_000 || key.Any(char.IsControl))
                throw new CashFlowCatalogOrderUnavailableException();
        }
    }

    private static CashFlowCatalogOrderCommandResult ReadReplay(
        StoredIdempotentResponse stored,
        Guid employeeId,
        MoveCashFlowCatalogOrderCommand command)
    {
        if (stored.ResponseStatusCode != 200)
            throw new CashFlowCatalogOrderUnavailableException();
        var response = JsonSerializer.Deserialize<StoredOrderResponse>(stored.ResponseJson, JsonOptions)
            ?? throw new CashFlowCatalogOrderUnavailableException();
        ValidateSnapshot(response.Snapshot);
        if (response.EmployeeId != employeeId
            || response.ExpectedVersion != command.ExpectedVersion
            || response.SourceId != command.SourceId
            || response.TargetId != command.TargetId
            || response.Placement != command.Placement
            || !response.Snapshot.CanEdit)
            throw new CashFlowCatalogOrderUnavailableException();
        return new CashFlowCatalogOrderCommandResult(response.Snapshot, response.Changed, true);
    }

    private static void ValidateSnapshot(CashFlowCatalogOrderSnapshotDto snapshot)
    {
        if (!IsVersion(snapshot.Version)
            || snapshot.Items.Count > MaximumRows
            || snapshot.Items.Select(item => item.Id).Distinct().Count() != snapshot.Items.Count
            || snapshot.Items.Any(item => item.Id == Guid.Empty
                || !CashFlowActivityType.IsDefined(item.ActivityType)
                || item.FlowDirection is not (null or "Payment" or "Receipt")
                || item.DisplayOrder < 0
                || item.GroupKey.Length > 2_000
                || item.GroupKey.Any(char.IsControl)))
            throw new CashFlowCatalogOrderUnavailableException();
    }

    private static bool IsVersion(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static bool IsText(string? value, int maximum) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximum && value == value.Trim() && !value.Any(char.IsControl);

    private static void RequireKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 127 || key != key.Trim() || key.Any(char.IsControl))
            throw new InvalidIdempotencyKeyException("Idempotency-Key is invalid.");
    }

    private static string CollapseWhitespace(string value) =>
        WhitespaceRegex().Replace(value.Trim(), " ");

    private static bool MustWrap(Exception exception) => !ProcessFatalExceptionClassifier.IsFatal(exception)
        && exception is not (OperationCanceledException or ActorNotAuthenticatedException or ActorForbiddenException
            or CashFlowCatalogOrderNotFoundException or CashFlowCatalogOrderVersionConflictException
            or CashFlowCatalogOrderUnavailableException or DomainRuleViolationException
            or InvalidIdempotencyKeyException or IdempotencyConflictException);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record StoredOrderResponse(
        [property: JsonRequired] Guid EmployeeId,
        [property: JsonRequired] string ExpectedVersion,
        [property: JsonRequired] Guid SourceId,
        [property: JsonRequired] Guid TargetId,
        [property: JsonRequired] string Placement,
        [property: JsonRequired] CashFlowCatalogOrderSnapshotDto Snapshot,
        [property: JsonRequired] bool Changed);

    [GeneratedRegex(@"^\d+(?:\.\d+)*\.\s*", RegexOptions.CultureInvariant)]
    private static partial Regex HierarchyPrefixRegex();

    [GeneratedRegex(@"^CF[-_. ]*\d+\s*[-—.:]?\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CashFlowPrefixRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
