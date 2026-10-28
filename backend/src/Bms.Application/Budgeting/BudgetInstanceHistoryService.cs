using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.Budgeting;

namespace Bms.Application.Budgeting;

public sealed record BudgetHistoryAuditRow(Guid Id, DateTimeOffset OccurredAtUtc,
    string ActorExternalId, string? ActorName, string Action, string? AfterJson)
{
    public Guid? CapturedActorEmployeeId { get; init; }
}
public sealed record BudgetHistoryAuditPage(IReadOnlyList<BudgetHistoryAuditRow> Items, int Total);
public interface IBudgetInstanceHistoryRepository
{
    Task<BudgetHistoryAuditPage> ListAsync(Guid budgetId, int skip, int take, CancellationToken token);
}
public sealed record BudgetHistoryEventDto(Guid Id, DateTimeOffset OccurredAtUtc, string EventCode,
    string Title, string ActorName, string? RoleName, string? Comment, bool DetailsAvailable)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActorKey { get; init; }
}
public sealed record BudgetHistoryPageDto(Guid BudgetInstanceId, IReadOnlyList<BudgetHistoryEventDto> Items,
    int Skip, int Take, int Total);

/// <summary>Document-scoped projection of all persisted audit cycles, not global audit access.</summary>
public sealed class BudgetInstanceHistoryService(IBudgetInstanceHistoryRepository history,
    IBudgetInstanceStore budgets, IBudgetDirectoryStore directory, BudgetDocumentAuthority authority,
    ICurrentActor actor, IBusinessTransactionRunner transactions)
{
    public async Task<BudgetHistoryPageDto> ListAsync(Guid id, int skip, int take, CancellationToken token)
    {
        ActorIdentity.RequireBusinessDataAccess(actor);
        if (id == Guid.Empty) throw new BudgetInstanceNotFoundException();
        if (skip < 0 || take < 1 || take > 50)
            throw new DomainRuleViolationException("History paging is invalid.");
        _ = await authority.RequireEmployeeAsync(actor, token);
        var center = await budgets.FindCostCenterIdAsync(id, token) ?? throw new BudgetInstanceNotFoundException();
        return await transactions.ExecuteAsync(async ct =>
        {
            var exists = await directory.LockCostCenterAsync(center, ct);
            _ = await authority.RequireEmployeeAsync(actor, ct);
            if (!exists) throw new BudgetInstanceNotFoundException();
            var scope = await actor.ResolveBusinessDataReadScopeAsync(ct);
            if (!scope.Contains(center)) throw new BudgetInstanceNotFoundException();
            var budget = await budgets.FindAsync(id, ct);
            if (budget?.CostCenterId != center
                || !await budgets.CostCenterIsActiveNonGroupAsync(center, ct))
                throw new BudgetInstanceNotFoundException();
            var page = await history.ListAsync(id, skip, take, ct);
            ct.ThrowIfCancellationRequested();
            return new BudgetHistoryPageDto(id, page.Items.Select(row => Project(row, id, center)).ToArray(),
                skip, take, page.Total);
        }, token);
    }

    internal static BudgetHistoryEventDto Project(BudgetHistoryAuditRow row, Guid budgetId, Guid centerId)
    {
        var actorName = SafeText(row.ActorName, 500) ? row.ActorName!.Trim() : "Автор не определён";
        var code = row.Action switch
        {
            "BudgetInstanceDraftCreated" => "created",
            "BudgetInstanceDraftUpdated" => "saved",
            "BudgetSubmitted" => "submitted",
            "BudgetApprove" => "agreed",
            "BudgetReturn" => "returned",
            "BudgetReject" => "rejected",
            "BudgetInstanceApproved" => "approved",
            _ => "other"
        };
        string? role = null, comment = null;
        var details = code is "created" or "saved" or "approved";
        if (code is "submitted" or "agreed" or "returned" or "rejected")
        {
            details = false;
            try
            {
                using var document = JsonDocument.Parse(BoundedJson(row.AfterJson),
                    new JsonDocumentOptions { MaxDepth = 32 });
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("budgetInstanceId", out var id) && id.TryGetGuid(out var parsedId)
                    && parsedId == budgetId)
                {
                    if (code == "submitted") details = true;
                    else if (root.TryGetProperty("decisions", out var decisions)
                        && decisions.ValueKind == JsonValueKind.Array && decisions.GetArrayLength() is > 0 and <= 4)
                    {
                        var decision = decisions[decisions.GetArrayLength() - 1];
                        var expectedKind = code == "agreed" ? 0 : code == "returned" ? 1 : 2;
                        if (decision.ValueKind == JsonValueKind.Object
                            && decision.TryGetProperty("decision", out var kind) && kind.TryGetInt32(out var parsedKind)
                            && parsedKind == expectedKind
                            && decision.TryGetProperty("actorExternalId", out var external)
                            && external.ValueKind == JsonValueKind.String && external.GetString() == row.ActorExternalId
                            && decision.TryGetProperty("roleCode", out var roleCode)
                            && roleCode.ValueKind == JsonValueKind.String)
                        {
                            role = RoleName(roleCode.GetString());
                            if (decision.TryGetProperty("actingRoleCode", out var actingRole)
                                && actingRole.ValueKind != JsonValueKind.Null)
                            {
                                // Preserve the captured route role but display the actual
                                // controller who stopped a director-stage submission.
                                role = expectedKind is 1 or 2 && roleCode.GetString() == "FIN_DIRECTOR"
                                    && actingRole.ValueKind == JsonValueKind.String
                                    && actingRole.GetString() == "FIN_CONTROLLER"
                                    ? RoleName("FIN_CONTROLLER") : null;
                            }
                            if (role is not null && root.TryGetProperty("state", out var state)
                                && state.ValueKind == JsonValueKind.String
                                && state.GetString() == (code == "returned" ? "Returned" : code == "rejected" ? "Rejected"
                                    : state.GetString() == "Approved" ? "Approved" : "Pending"))
                            {
                                if (code == "agreed" && state.GetString() == "Approved") code = "approved";
                                if (decision.TryGetProperty("comment", out var reason))
                                {
                                    if (reason.ValueKind == JsonValueKind.Null && expectedKind == 0) details = true;
                                    else if (reason.ValueKind == JsonValueKind.String && SafeText(reason.GetString(), 2000, expectedKind == 0))
                                    { comment = reason.GetString(); details = true; }
                                }
                            }
                        }
                    }
                }
            }
            catch (JsonException) { /* No raw audit payload or guessed details are exposed. */ }
            catch (InvalidOperationException) { }
            catch (FormatException) { }
            if (!details) { role = null; comment = null; }
        }
        if (row.Action is "BudgetInstanceDraftCreated" or "BudgetInstanceDraftUpdated" or "BudgetInstanceApproved")
        {
            // These snapshots can carry financial fields; only their document binding is read.
            try
            {
                using var document = JsonDocument.Parse(BoundedJson(row.AfterJson), new JsonDocumentOptions { MaxDepth = 32 });
                var root = document.RootElement;
                details = root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("id", out var id) && id.TryGetGuid(out var parsedId) && parsedId == budgetId
                    && root.TryGetProperty("costCenterId", out var center) && center.TryGetGuid(out var parsedCenter)
                    && parsedCenter == centerId;
            }
            catch (JsonException) { details = false; }
            catch (InvalidOperationException) { details = false; }
            catch (FormatException) { details = false; }
        }
        var title = code switch
        {
            "created" => "Создан черновик", "saved" => "Сохранены изменения черновика",
            "submitted" => "Отправлен на согласование", "agreed" => "Согласовано",
            "returned" => "Отправлен на доработку", "rejected" => "Отклонён",
            "approved" => "Утверждён", _ => "Действие с документом"
        };
        // The captured audit identity, never a current login mapping or a display-name match.
        var actorKey = row.CapturedActorEmployeeId is Guid employee && employee != Guid.Empty
            && SafeText(row.ActorName, 500) && actorName != "Автор не определён"
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                "BmsBudgetHistoryActor:v1:" + budgetId.ToString("D") + ":" + employee.ToString("D")))).ToLowerInvariant()
            : null;
        return new(row.Id, row.OccurredAtUtc, code, title, actorName, role, comment, details) { ActorKey = actorKey };
    }

    private static string BoundedJson(string? json) => json is { Length: <= 1_048_576 } ? json : "null";
    private static string? RoleName(string? code) => code switch
    {
        "BUDGET_OWNER" => "Владелец ЦФО", "FIN_CONTROLLER" => "Финансовый контролёр",
        "FIN_DIRECTOR" => "Финансовый директор", "GENERAL_DIRECTOR" => "Генеральный директор", _ => null
    };
    private static bool SafeText(string? value, int max, bool allowEmpty = false) => value is not null
        && value.Length <= max && (allowEmpty || value.Trim().Length > 0)
        && !value.Any(ch => char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t');
}
