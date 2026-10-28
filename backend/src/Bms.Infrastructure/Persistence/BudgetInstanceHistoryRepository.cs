using System.Data;
using System.Text.Json;
using Bms.Application.Budgeting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class BudgetInstanceHistoryRepository(BmsDbContext db) : IBudgetInstanceHistoryRepository
{
    public async Task<BudgetHistoryAuditPage> ListAsync(Guid budgetId, int skip, int take, CancellationToken token)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Budget history requires a captured Serializable read.");
        var objectId = budgetId.ToString("D");
        var query = db.BusinessAuditEntries.AsNoTracking()
            .Where(a => a.ObjectType == "BudgetInstance" && a.ObjectId == objectId);
        var total = await query.CountAsync(token);
        var rows = await query.OrderBy(a => a.OccurredAtUtc).ThenBy(a => a.Id).Skip(skip).Take(take)
            .Select(a => new { a.Id, a.OccurredAtUtc, a.ActorExternalId, a.Action,
                AfterJson = a.AfterJson.Length <= 1_048_576 ? a.AfterJson : null }).ToArrayAsync(token);
        var identities = rows.ToDictionary(a => a.Id, a => ReadIdentity(a.AfterJson, a.Action, a.ActorExternalId, budgetId));
        var reviewIds = identities.Values.Where(i => i.ReviewId.HasValue).Select(i => i.ReviewId!.Value).Distinct().ToArray();
        var submitters = await db.BudgetApprovalSubmissions.AsNoTracking()
            .Where(r => r.BudgetInstanceId == budgetId && reviewIds.Contains(r.Id))
            .Select(r => new { r.Id, r.SubmittedByEmployeeId, r.SubmittedByExternalId }).ToDictionaryAsync(r => r.Id, token);
        var employeeIds = identities.Values.Where(i => i.EmployeeId.HasValue).Select(i => i.EmployeeId!.Value)
            .Concat(submitters.Values.Select(s => s.SubmittedByEmployeeId)).Distinct().ToArray();
        var employeeNames = await db.Employees.AsNoTracking().Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.Name, token);
        return new(rows.Select(a =>
        {
            var identity = identities[a.Id];
            var submitted = identity.ReviewId is Guid reviewId ? submitters.GetValueOrDefault(reviewId) : null;
            var employee = identity.EmployeeId ?? (submitted?.SubmittedByExternalId == a.ActorExternalId
                ? submitted.SubmittedByEmployeeId : Guid.Empty);
            // A current login mapping cannot establish who acted before a reassignment.
            var name = employee != Guid.Empty ? employeeNames.GetValueOrDefault(employee) : null;
            return new BudgetHistoryAuditRow(a.Id, a.OccurredAtUtc, a.ActorExternalId, name, a.Action, a.AfterJson)
            { CapturedActorEmployeeId = employee != Guid.Empty ? employee : null };
        }).ToArray(), total);
    }

    internal static (Guid? EmployeeId, Guid? ReviewId) ReadIdentity(string? json, string action, string actor, Guid budgetId)
    {
        try
        {
            using var document = JsonDocument.Parse(json is { Length: <= 1_048_576 } ? json : "null", new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);
            if (action is "BudgetInstanceDraftCreated" or "BudgetInstanceDraftUpdated" or "BudgetInstanceApproved")
            {
                if (!root.TryGetProperty("id", out var id) || !id.TryGetGuid(out var parsedId) || parsedId != budgetId) return (null, null);
                // An update's owner is not necessarily the employee who edited it.
                var field = action == "BudgetInstanceDraftCreated" ? "ownerEmployeeId"
                    : "historyActorEmployeeId";
                if (action == "BudgetInstanceApproved" && !root.TryGetProperty(field, out _)) field = "approvedByEmployeeId";
                return field is not null && root.TryGetProperty(field, out var employee) && employee.TryGetGuid(out var parsed)
                    && parsed != Guid.Empty ? (parsed, null) : (null, null);
            }
            if (!root.TryGetProperty("budgetInstanceId", out var budget) || !budget.TryGetGuid(out var parsedBudget) || parsedBudget != budgetId)
                return (null, null);
            if (action == "BudgetSubmitted")
                return root.TryGetProperty("reviewId", out var review) && review.TryGetGuid(out var parsedReview)
                    && parsedReview != Guid.Empty ? (null, parsedReview) : (null, null);
            if (action is "BudgetApprove" or "BudgetReturn" or "BudgetReject"
                && root.TryGetProperty("decisions", out var decisions) && decisions.ValueKind == JsonValueKind.Array
                && decisions.GetArrayLength() is > 0 and <= 4)
            {
                var last = decisions[decisions.GetArrayLength() - 1];
                if (last.ValueKind == JsonValueKind.Object && last.TryGetProperty("actorExternalId", out var external)
                    && external.ValueKind == JsonValueKind.String && external.GetString() == actor
                    && last.TryGetProperty("employeeId", out var employee) && employee.TryGetGuid(out var parsed)
                    && parsed != Guid.Empty) return (parsed, null);
            }
        }
        catch (JsonException) { }
        catch (InvalidOperationException) { }
        catch (FormatException) { }
        return (null, null);
    }
}
