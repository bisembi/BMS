using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Application.Authorization;
using System.Text.Json;

namespace Bms.Application.ExpenseRequests;

public sealed class ExpenseRequestHistoryService(
    IExpenseRequestHistoryRepository repository,
    ICurrentActor currentActor,
    IBusinessTransactionRunner transactions)
{
    public Task<ExpenseRequestHistoryPageDto?> ListAsync(Guid expenseRequestId, int skip, int take,
        CancellationToken cancellationToken) => ListAsync(expenseRequestId, skip, take, true, cancellationToken);

    public Task<ExpenseRequestHistoryPageDto?> ListAsync(Guid expenseRequestId, int skip, int take,
        bool includeActorNames, CancellationToken cancellationToken) => transactions.ExecuteAsync(
            token => ReadCoreAsync(expenseRequestId, skip, take, includeActorNames, token), cancellationToken);

    public Task<ExpenseRequestHistoryPageDto?> ListCommentsAsync(Guid expenseRequestId, int skip, int take,
        bool includeActorNames, CancellationToken cancellationToken) => transactions.ExecuteAsync(
            token => ReadCoreAsync(expenseRequestId, skip, take, includeActorNames, token, true), cancellationToken);

    private async Task<ExpenseRequestHistoryPageDto?> ReadCoreAsync(Guid expenseRequestId, int skip, int take,
        bool includeActorNames, CancellationToken cancellationToken, bool commentsOnly = false)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 50);

        var page = commentsOnly ? await repository.ListCommentedWorkflowVisibleAsync(
            expenseRequestId, actorExternalId, currentActor.HasFullBusinessDataAccess,
            skip, take, cancellationToken) : await repository.ListVisibleAsync(
            expenseRequestId,
            actorExternalId,
            currentActor.HasFullBusinessDataAccess,
            skip,
            take,
            cancellationToken);
        if (page is null)
        {
            return null;
        }

        var scope = await currentActor.ResolveBusinessDataReadScopeAsync(cancellationToken);
        return new ExpenseRequestHistoryPageDto(
            page.Items.Select(row =>
            {
                // The repository already proved current card visibility. A valid
                // attachment event contains only bounded public file metadata,
                // not a historical financial/CFO snapshot. Permit only this
                // exact projection; all financial history keeps its scope gate.
                if (scope.HasAny && !commentsOnly && row.Action is "AttachmentAdded" or "AttachmentDeleted")
                {
                    var attachment = ProjectRow(row, expenseRequestId, includeActorNames, false);
                    if (attachment.Kind == "attachment" && attachment.DetailsStatus == "complete") return attachment;
                }
                return (BusinessSnapshotReadScope.CanRead(scope, row.BeforeJson)
                    && BusinessSnapshotReadScope.CanRead(scope, row.AfterJson))
                    || (commentsOnly && CanReadNsiComment(scope, row.BeforeJson) && CanReadNsiComment(scope, row.AfterJson))
                    ? ProjectRow(row, expenseRequestId, includeActorNames, commentsOnly)
                    : new ExpenseRequestHistoryEventDto(row.OccurredAtUtc, "unsupported", "Изменения документа",
                        "Пользователь BMS", "unavailable", [], 0);
            }).ToArray(),
            skip,
            take,
            page.Total);
    }

    private static ExpenseRequestHistoryEventDto ProjectRow(ExpenseRequestAuditSnapshotRow row,
        Guid requestId, bool includeActorNames, bool commentsOnly)
    {
        var projected = ExpenseRequestHistoryProjector.Project(
            includeActorNames ? row : row with { ActorName = null, References = null }, requestId);
        if (!commentsOnly) return projected;
        // This representation never returns financial changes, even to a
        // globally authorized reader. NSI participants get only the recorded
        // instruction, not historical amounts or foreign CFO details.
        return projected with { Changes = projected.Kind == "workflow" ? projected.Changes
            .Where(c => c.Section == "Согласование" && c.Field == "Причина").ToArray() : [], OmittedChanges = 0 };
    }

    private static bool CanReadNsiComment(BusinessDataReadScope scope, string? json)
    {
        if (scope.NsiOrganizationIds.Count == 0 || string.IsNullOrWhiteSpace(json) || json.Length > 1_000_000) return false;
        try
        {
            using var document = JsonDocument.Parse(json); var snapshot = document.RootElement;
            if (snapshot.ValueKind != JsonValueKind.Object) return false;
            if (snapshot.TryGetProperty("expenseRequest", out var nested)) snapshot = nested;
            return snapshot.ValueKind == JsonValueKind.Object && snapshot.TryGetProperty("organizationId", out var org)
                && org.TryGetGuid(out var id) && scope.CanReadNsiOrganization(id);
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

}
