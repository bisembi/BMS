using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;

namespace Bms.Application.Budgeting;

public interface IBudgetApprovalStore
{
    Task CaptureActiveVersionAsync(Guid reviewId, CancellationToken token) => Task.CompletedTask;
    Task<bool> ActiveVersionMatchesAsync(Guid reviewId, CancellationToken token) => Task.FromResult(true);
    Task<BudgetApprovalSubmission?> LatestAsync(Guid budgetId, CancellationToken token);
    Task<BudgetApprovalSubmission?> FindAsync(Guid budgetId, Guid reviewId, CancellationToken token);
    Task<IReadOnlyList<BudgetApprovalBaselineVersion>> ReadBaselinesAsync(BudgetInstance proposal, CancellationToken token);
    void Add(BudgetApprovalSubmission review);
}

public sealed record SubmitBudgetApprovalCommand(Guid ExpectedConcurrencyStamp);
public sealed record DecideBudgetApprovalCommand(Guid ReviewId, Guid ExpectedConcurrencyStamp,
    string Decision, string? Comment);
public sealed record BudgetApprovalStateDto(Guid BudgetInstanceId, Guid? ReviewId, Guid? ConcurrencyStamp,
    string State, string? CurrentRoleCode, decimal PositiveExcessKzt, bool CanSubmit, bool CanDecide,
    IReadOnlyList<BudgetApprovalReviewStage> Stages, IReadOnlyList<BudgetApprovalReviewDecision> Decisions,
    bool BaselineChanged = false, bool CanReturn = false, bool CanReject = false);

public sealed class BudgetApprovalBaselineChangedException : Exception;

public sealed class BudgetApprovalWorkflowService(IBudgetApprovalStore reviews, IBudgetInstanceStore budgets,
    IBudgetDirectoryStore directory, BudgetDocumentAuthority editors, CfoWorkflowAuthority authority,
    ApprovalRouteParticipantResolver participants, ICurrentActor actor, IBusinessTransactionRunner transactions,
    IIdempotencyStore idempotency, IBusinessAuditSink audit, IOutboxSink outbox, IUnitOfWork work, IClock clock,
    IAccessScope accessScope)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<BudgetApprovalStateDto> GetAsync(Guid id, CancellationToken token) =>
        await transactions.ExecuteAsync(async ct =>
        {
            var employee = await authority.RequireEmployeeAsync(actor, ct);
            var proposal = await LockAsync(id, ct);
            return await ToDtoAsync(proposal, await reviews.LatestAsync(id, ct), employee, ct);
        }, token);

    public async Task<BudgetApprovalStateDto> SubmitAsync(Guid id, SubmitBudgetApprovalCommand command,
        string key, CancellationToken token)
    {
        ValidateKey(key);
        var external = ActorIdentity.RequireBusinessDataAccess(actor);
        return await transactions.ExecuteAsync(async ct =>
        {
            var employee = await authority.RequireEmployeeAsync(actor, ct);
            var proposal = await LockAsync(id, ct);
            if (!await CanSubmitDocumentAsync(proposal, ct))
                throw new ActorForbiddenException();
            var scope = Scope(id, external, "submit");
            var hash = Hash(command);
            var replay = await ReplayAsync(scope, key, hash, ct);
            if (replay is not null) return replay;
            var previous = await reviews.LatestAsync(id, ct);
            if (proposal.Status != BudgetInstanceStatus.Draft || previous?.State == BudgetApprovalReviewState.Pending)
                throw new DomainRuleViolationException("Budget cannot be submitted in its current state.");
            if (proposal.ConcurrencyStamp != command.ExpectedConcurrencyStamp || command.ExpectedConcurrencyStamp == Guid.Empty)
                throw new BudgetInstanceConcurrencyConflictException();
            // Persisted drafts can bypass Create/ReplaceDraft validation. Check
            // the entire proposal before capturing any comparison or review.
            // Keep this submission-only: existing review reads/return/reject and
            // successful idempotent retries retain their original contracts.
            BudgetInstance.EnsureValidityPeriod(proposal.ValidFrom, proposal.ValidTo);
            foreach (var line in proposal.Lines)
            {
                if (line.BudgetMonth.Day != 1 || line.BudgetMonth < proposal.ValidFrom
                    || line.BudgetMonth > proposal.ValidTo)
                    throw new DomainRuleViolationException("Budget instance line months are inconsistent with the header.");
            }
            var assessment = BudgetApprovalAssessment.Capture(proposal, await reviews.ReadBaselinesAsync(proposal, ct));
            var planned = assessment.Plan(false);
            var financial = await participants.ResolveBudgetAsync(planned, ct);
            var route = financial.Select(p => new BudgetApprovalReviewStageInput(p.StageNumber,
                planned[p.StageNumber - 1].RoleCode,
                p.EligibleEmployeeIds.ToArray())).ToList();
            var review = BudgetApprovalReview.Submit(assessment, (previous?.SubmissionNumber ?? 0) + 1,
                employee, external, clock.UtcNow, false, route);
            var row = new BudgetApprovalSubmission
            {
                Id = review.Id, BudgetInstanceId = id, CostCenterId = proposal.CostCenterId,
                SubmissionNumber = review.SubmissionNumber, ProposalConcurrencyStamp = proposal.ConcurrencyStamp,
                ConcurrencyStamp = review.ConcurrencyStamp, SubmittedByEmployeeId = employee,
                SubmittedByExternalId = external, SubmittedAtUtc = review.SubmittedAtUtc, RequiresOwnerReview = false,
                PositiveExcessKzt = assessment.PositiveExcessKzt, State = review.State, CurrentStageNumber = review.CurrentStageNumber,
                CellsJson = JsonSerializer.Serialize(assessment.Cells, Json), StagesJson = JsonSerializer.Serialize(route, Json)
            };
            reviews.Add(row);
            await reviews.CaptureActiveVersionAsync(row.Id, ct);
            var dto = await ToDtoAsync(proposal, row, employee, ct);
            await CommitAsync(scope, key, hash, external, id, "BudgetSubmitted", null, dto, ct);
            return dto;
        }, token);
    }

    public async Task<BudgetApprovalStateDto> DecideAsync(Guid id, DecideBudgetApprovalCommand command,
        string key, CancellationToken token)
    {
        ValidateKey(key);
        var external = ActorIdentity.RequireBusinessDataAccess(actor);
        if (!Enum.TryParse<BudgetApprovalDecisionKind>(command.Decision, false, out var decision)
            || !Enum.IsDefined(decision)) throw new DomainRuleViolationException("Unknown budget decision.");
        return await transactions.ExecuteAsync(async ct =>
        {
            var employee = await authority.RequireEmployeeAsync(actor, ct);
            var proposal = await LockAsync(id, ct);
            var row = await reviews.FindAsync(id, command.ReviewId, ct) ?? throw new BudgetInstanceNotFoundException();
            var review = Restore(row);
            // Prove original captured authority before replay. Advancing a stage
            // must not invalidate a successful same-command retry.
            var scope = Scope(id, external, $"decision:{row.Id:N}");
            var hash = Hash(command);
            var stored = await idempotency.FindAsync(scope, key, ct);
            if (stored is not null && stored.RequestHash != hash) throw new IdempotencyConflictException();
            var storedDto = stored is null ? null : JsonSerializer.Deserialize<BudgetApprovalStateDto>(stored.ResponseJson, Json)
                ?? throw new DomainRuleViolationException("Stored decision response is invalid.");
            var originalDecision = storedDto?.Decisions.LastOrDefault();
            var authorizedStage = stored is not null
                ? originalDecision?.EmployeeId == employee && originalDecision.ActorExternalId == external
                    && originalDecision.Decision == decision ? originalDecision.RoleCode : null
                : review.Stages.SingleOrDefault(s => s.StageNumber == review.CurrentStageNumber)?.RoleCode;
            var controllerStop = authorizedStage == "FIN_DIRECTOR"
                && decision is BudgetApprovalDecisionKind.Return or BudgetApprovalDecisionKind.Reject
                && (stored is null || originalDecision?.ActingRoleCode == "FIN_CONTROLLER")
                && await CanControllerStopAsync(employee, proposal.CostCenterId, ct);
            if (authorizedStage is null || (authorizedStage == "BUDGET_OWNER" && employee == row.SubmittedByEmployeeId)
                || (!controllerStop && (!review.Stages.Any(s => s.RoleCode == authorizedStage && s.EmployeeIds.Contains(employee))
                    || originalDecision?.ActingRoleCode is not null)))
                throw new ActorForbiddenException();
            if (!controllerStop) await authority.RequireStageAsync(employee, proposal.CostCenterId, authorizedStage, ct);
            if (stored is not null)
            {
                return storedDto!;
            }
            if (proposal.Status != BudgetInstanceStatus.Draft || proposal.ConcurrencyStamp != row.ProposalConcurrencyStamp)
                throw new BudgetInstanceConcurrencyConflictException();
            if (command.ExpectedConcurrencyStamp == Guid.Empty || command.ExpectedConcurrencyStamp != review.ConcurrencyStamp)
                throw new BudgetInstanceConcurrencyConflictException();
            // The company version is submission provenance, not a decision lock.
            // Decide the unchanged submitted document using its captured route;
            // company publication and other instance approvals are independent.
            var before = await ToDtoAsync(proposal, row, employee, ct);
            if (controllerStop)
                review.DecideAsFinancialController(command.ExpectedConcurrencyStamp, employee, external, decision, clock.UtcNow, command.Comment);
            else review.Decide(command.ExpectedConcurrencyStamp, employee, external, decision, clock.UtcNow, command.Comment);
            row.ConcurrencyStamp = review.ConcurrencyStamp;
            row.State = review.State;
            row.CurrentStageNumber = review.CurrentStageNumber;
            row.DecisionsJson = JsonSerializer.Serialize(review.Decisions, Json);
            if (review.State == BudgetApprovalReviewState.Approved)
            {
                var ordinal = await budgets.AllocateApprovalOrdinalAsync(proposal.CostCenterId, ct);
                proposal.Approve(proposal.ConcurrencyStamp, ordinal, clock.UtcNow, employee, external);
            }
            var after = await ToDtoAsync(proposal, row, employee, ct);
            await CommitAsync(scope, key, hash, external, id, $"Budget{decision}", before, after, ct);
            return after;
        }, token);
    }

    private async Task<BudgetInstance> LockAsync(Guid id, CancellationToken token)
    {
        var center = await budgets.FindCostCenterIdAsync(id, token) ?? throw new BudgetInstanceNotFoundException();
        if (!await directory.LockCostCenterAsync(center, token)) throw new BudgetInstanceNotFoundException();
        var read = await actor.ResolveBusinessDataReadScopeAsync(token);
        if (!read.Contains(center)) throw new BudgetInstanceNotFoundException();
        var budget = await budgets.FindAsync(id, token) ?? throw new BudgetInstanceNotFoundException();
        if (budget.CostCenterId != center || !await budgets.CostCenterIsActiveNonGroupAsync(center, token))
            throw new BudgetInstanceNotFoundException();
        return budget;
    }

    private static BudgetApprovalReview Restore(BudgetApprovalSubmission row)
    {
        var cells = JsonSerializer.Deserialize<BudgetApprovalCellComparison[]>(row.CellsJson, Json)
            ?? throw new DomainRuleViolationException("Budget comparison is missing.");
        var stages = JsonSerializer.Deserialize<BudgetApprovalReviewStageInput[]>(row.StagesJson, Json)
            ?? throw new DomainRuleViolationException("Budget stages are missing.");
        var decisions = JsonSerializer.Deserialize<BudgetApprovalReviewDecision[]>(row.DecisionsJson, Json)
            ?? throw new DomainRuleViolationException("Budget decisions are missing.");
        return BudgetApprovalReview.Restore(row.Id, row.ConcurrencyStamp,
            BudgetApprovalAssessment.Restore(row.BudgetInstanceId, row.CostCenterId, row.ProposalConcurrencyStamp, cells, row.PositiveExcessKzt),
            row.SubmissionNumber, row.SubmittedByEmployeeId, row.SubmittedByExternalId, row.SubmittedAtUtc,
            row.RequiresOwnerReview, stages, decisions, row.State, row.CurrentStageNumber);
    }

    private async Task<BudgetApprovalStateDto> ToDtoAsync(BudgetInstance proposal, BudgetApprovalSubmission? row, Guid employee, CancellationToken token)
    {
        var pending = row?.State == BudgetApprovalReviewState.Pending;
        var canSubmit = proposal.Status == BudgetInstanceStatus.Draft && !pending
            && await CanSubmitDocumentAsync(proposal, token);
        if (row is null) return new(proposal.Id, null, null, proposal.Status.ToString(), null, 0m, canSubmit, false, [], []);
        var review = Restore(row);
        var stage = review.Stages.SingleOrDefault(s => s.StageNumber == review.CurrentStageNumber);
        var canDecide = pending && proposal.Status == BudgetInstanceStatus.Draft
            && proposal.ConcurrencyStamp == row.ProposalConcurrencyStamp
            && !(stage?.RoleCode == "BUDGET_OWNER" && employee == row.SubmittedByEmployeeId)
            && stage?.EmployeeIds.Contains(employee) == true;
        if (canDecide)
        {
            try { await authority.RequireStageAsync(employee, proposal.CostCenterId, stage!.RoleCode, token); }
            catch (ActorForbiddenException) { canDecide = false; }
        }
        var canStop = canDecide || (pending && proposal.Status == BudgetInstanceStatus.Draft
            && proposal.ConcurrencyStamp == row.ProposalConcurrencyStamp && stage?.RoleCode == "FIN_DIRECTOR"
            && await CanControllerStopAsync(employee, proposal.CostCenterId, token));
        return new(proposal.Id, row.Id, row.ConcurrencyStamp, row.State.ToString(), stage?.RoleCode,
            row.PositiveExcessKzt, canSubmit, canDecide, review.Stages, review.Decisions,
            false, canStop, canStop);
    }

    private async Task<bool> CanControllerStopAsync(Guid employee, Guid costCenterId, CancellationToken token)
    {
        try { await authority.RequireStageAsync(employee, costCenterId, "FIN_CONTROLLER", token); return true; }
        catch (ActorForbiddenException) { return false; }
    }

    private async Task<bool> CanSubmitDocumentAsync(BudgetInstance proposal, CancellationToken token)
    {
        var editor = await editors.ResolveEditorAsync(actor, proposal.CostCenterId, token);
        if (!editor.CanEditBudgetDocument) return false;
        if (editor.HasFullBudgetAccess) return true;
        // Same dated policy as editing a partial-membership budget. The
        // surrounding transaction holds the CFO lock; check before replay.
        var external = ActorIdentity.RequireBusinessDataAccess(actor);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        return await accessScope.CanUseAsync(external, null, [proposal.CostCenterId], today, token)
            && await accessScope.CanUseAsync(external, null, [proposal.CostCenterId], proposal.ValidFrom, token);
    }

    internal static bool BaselineMatches(BudgetApprovalAssessment frozen, BudgetApprovalAssessment current) =>
        frozen.BudgetInstanceId == current.BudgetInstanceId && frozen.CostCenterId == current.CostCenterId
        && frozen.ProposalConcurrencyStamp == current.ProposalConcurrencyStamp
        && frozen.PositiveExcessKzt == current.PositiveExcessKzt
        && frozen.Cells.OrderBy(c => c.BudgetMonth).ThenBy(c => c.CashFlowItemId)
            .SequenceEqual(current.Cells.OrderBy(c => c.BudgetMonth).ThenBy(c => c.CashFlowItemId));

    private async Task CommitAsync(string scope, string key, string hash, string external, Guid id,
        string action, BudgetApprovalStateDto? before, BudgetApprovalStateDto after, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(after, Json);
        var correlation = Guid.NewGuid().ToString("N");
        audit.Append(Guid.NewGuid(), clock.UtcNow, external, "BudgetInstance", id.ToString("D"), action,
            before is null ? null : JsonSerializer.Serialize(before, Json), json, correlation);
        outbox.Enqueue(Guid.NewGuid(), clock.UtcNow, action, json, correlation);
        idempotency.Add(scope, key, hash, 200, json, clock.UtcNow);
        await work.SaveChangesAsync(token);
    }

    private async Task<BudgetApprovalStateDto?> ReplayAsync(string scope, string key, string hash, CancellationToken token)
    {
        var result = await idempotency.FindAsync(scope, key, token);
        if (result is null) return null;
        if (result.RequestHash != hash) throw new IdempotencyConflictException();
        return JsonSerializer.Deserialize<BudgetApprovalStateDto>(result.ResponseJson, Json)
            ?? throw new DomainRuleViolationException("Stored submission response is invalid.");
    }
    private static string Scope(Guid id, string external, string action) =>
        $"budget-review:v1:{id:N}:{action}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(external)))}";
    private static string Hash<T>(T input) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input, Json)));
    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 127 || key != key.Trim() || key.Any(char.IsControl))
            throw new InvalidIdempotencyKeyException("Invalid idempotency key.");
    }
}
