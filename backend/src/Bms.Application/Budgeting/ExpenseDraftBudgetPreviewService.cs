using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.Budgeting;

public sealed record ExpenseDraftBudgetLine(Guid CashFlowItemId, Guid? CostCenterId, decimal Amount,
    ExpenseVatRate VatRateCode);
public sealed record ExpenseDraftBudgetCommand(Guid CostCenterId, DateOnly PlannedPaymentDate,
    string CurrencyCode, IReadOnlyList<ExpenseDraftBudgetLine> Lines,
    Guid? ExpenseRequestId = null, Guid? ExpectedVersion = null, bool LimitsOnly = false);
public sealed record ExpenseDraftBudgetPosition(Guid CostCenterId, Guid CashFlowItemId, DateOnly BudgetMonth,
    decimal LimitAmountKzt, decimal OtherReservedAmountKzt, decimal PaidAmountKzt,
    decimal RequestedAmountKzt, decimal AvailableAmountKzt, decimal PositiveDeficitKzt);
public sealed record ExpenseDraftBudgetPreview(IReadOnlyList<ExpenseDraftBudgetPosition> Positions,
    ExpenseApprovalRoutePresentationDto? ApprovalRoute = null);

/// Preview never saves the transient aggregate, allocates a document number or
/// changes reservations. Shared budget locks and approved-limit resolution are
/// the same as submission preflight, including keys with no persisted position.
public sealed class ExpenseDraftBudgetPreviewService(
    ISubmissionBudgetSnapshotReader snapshots, IExpenseRequestRepository requests,
    IReferenceDataRepository references, ICurrentActor actor, IBusinessTransactionRunner transactions,
    ExpenseRequestDraftCurrencyService currencies, IBudgetRepository? budgetLimits = null,
    IExpenseApprovalRoutePresentation? approvalRoutes = null)
{
    public async Task<ExpenseDraftBudgetPreview> PreviewAsync(ExpenseDraftBudgetCommand command,
        CancellationToken cancellationToken)
    {
        var identity = ActorIdentity.RequireBusinessDataAccess(actor);
        if (command.CostCenterId == Guid.Empty || command.Lines is null || command.Lines.Count is < 1 or > 1000
            || command.ExpenseRequestId.HasValue != command.ExpectedVersion.HasValue
            || command.ExpenseRequestId == Guid.Empty || command.ExpectedVersion == Guid.Empty
            || string.IsNullOrWhiteSpace(command.CurrencyCode))
            throw new DomainRuleViolationException("Invalid preview selection.");
        if (command.LimitsOnly && (command.ExpenseRequestId.HasValue || command.ExpectedVersion.HasValue))
            throw new DomainRuleViolationException("Limit lookup has no request context.");
        foreach (var line in command.Lines)
        {
            if (line is null || line.CashFlowItemId == Guid.Empty || (command.LimitsOnly ? line.Amount != 0m : line.Amount <= 0m)
                || (line.CostCenterId.HasValue && line.CostCenterId != command.CostCenterId)
                || !Enum.IsDefined(line.VatRateCode))
                throw new DomainRuleViolationException("Invalid preview line.");
            MoneyAmountPolicy.EnsureRepresentable(line.Amount, "Preview amount");
        }
        return await transactions.ExecuteAsync(async token =>
        {
            var scope = await actor.ResolveBusinessDataReadScopeAsync(token);
            if (!scope.Contains(command.CostCenterId)) throw new ActorForbiddenException();
            var previewId = command.ExpenseRequestId ?? Guid.NewGuid();
            var initiator = identity;
            var budgetDate=command.PlannedPaymentDate;
            if (command.ExpenseRequestId.HasValue)
            {
                var saved = await requests.FindVisibleAsync(previewId, identity, actor.HasFullBusinessDataAccess, token);
                if (saved is null || !scope.Contains(saved)) throw new ExpenseRequestNotFoundException();
                if (saved.ConcurrencyStamp != command.ExpectedVersion) throw new ExpenseRequestVersionConflictException();
                if (saved.Status is not (ExpenseRequestStatus.Draft or ExpenseRequestStatus.Returned))
                    throw new DomainRuleViolationException("Only editable drafts support preview.");
                initiator = saved.InitiatorExternalId;
                budgetDate=saved.TreasuryBudgetMonth??command.PlannedPaymentDate;
            }
            var articles = await references.ResolveCashFlowItemsAsync(
                command.Lines.Select(line => line.CashFlowItemId).Distinct().ToArray(), token);
            if (command.Lines.Any(line => !articles.Any(article => article.Id == line.CashFlowItemId
                    && article.CostCenterId == command.CostCenterId)))
                throw new DomainRuleViolationException("Article does not belong to the selected CFO.");

            if (command.LimitsOnly)
            {
                // Read the same approved payment limits as operational control,
                // without currency conversion, a synthetic amount or a reserve.
                // The execution report is deliberately not used: its fact source
                // and year restrictions do not belong to request preparation.
                if (budgetLimits is null) throw new SubmissionPreflightUnavailableException("Budget limits unavailable.");
                var limitKeys = command.Lines.Select(line => BudgetKey.Create(command.CostCenterId,
                    line.CashFlowItemId, command.PlannedPaymentDate)).Distinct().ToArray();
                var positions = await budgetLimits.ListPositionsAsync(limitKeys, token);
                if (positions.Count != limitKeys.Length || positions.Select(p => p.Key).Distinct().Count() != limitKeys.Length
                    || positions.Any(p => !limitKeys.Contains(p.Key)))
                    throw new SubmissionPreflightUnavailableException("Budget limit capture incomplete.");
                return new ExpenseDraftBudgetPreview(positions.Select(position =>
                {
                    var limit = position.LimitAmountKzt ?? 0m;
                    var remaining = limit - position.ReservedAmountKzt - position.PaidAmountKzt;
                    MoneyAmountPolicy.EnsureRepresentable(remaining, "Budget limit lookup remainder");
                    return new ExpenseDraftBudgetPosition(position.CostCenterId, position.CashFlowItemId,
                        position.BudgetMonth, limit, position.ReservedAmountKzt, position.PaidAmountKzt,
                        0m, remaining, Math.Max(0m, -remaining));
                }).ToArray());
            }

            // Internal non-persisted fields satisfy the existing currency
            // calculator only; no organization or justification is required
            // from an otherwise incomplete form to preview its valid rows.
            var draft = ExpenseRequest.CreateDraft(previewId, initiator, DateTimeOffset.UtcNow,
                organizationId: Guid.NewGuid(), defaultCostCenterId: command.CostCenterId,
                plannedPaymentDate: command.PlannedPaymentDate, currencyCode: command.CurrencyCode,
                counterpartyId: null, contractId: null, purpose: null, comment: null,
                isUrgent: false, urgencyReason: null, isNewCounterparty: false, isNewContract: false,
                businessJustification: "Read-only budget preview",
                lines: command.Lines.Select(line => new ExpenseRequestLineDraft(line.CashFlowItemId,
                    command.CostCenterId, command.PlannedPaymentDate, line.Amount, VatRateCode: line.VatRateCode)));
            await currencies.RefreshAsync(draft, token);
            if (draft.Lines.Any(line => !line.AmountKzt.HasValue || !line.HasValidKztCalculatedAmounts()))
                throw new DraftCurrencyCalculationUnavailableException("Preview currency rate unavailable.");
            var desired = draft.Lines.Select(line => BudgetRequestProjection.Create(previewId, line.Id,
                BudgetKey.Create(command.CostCenterId, line.CashFlowItemId!.Value, budgetDate),
                line.AmountKzt!.Value)).ToArray();
            var snapshot = await snapshots.ReadAsync(previewId, desired, token);
            var keys = desired.Select(line => line.Key).Distinct().ToArray();
            if (snapshot.Positions.Any(position => !position.IsAggregateConsistent
                    || position.ExistingRequestProjectionAmountKzt > position.CurrentReservedAmountKzt))
                throw new SubmissionPreflightUnavailableException("Budget aggregate mismatch.");
            var result = keys.Select(key =>
            {
                var position = snapshot.Positions.Single(position => position.Key == key);
                var limit = position.LimitAmountKzt ?? 0m;
                var other = position.CurrentReservedAmountKzt - position.ExistingRequestProjectionAmountKzt;
                var remaining = limit - other - position.PaidAmountKzt - position.DesiredRequestProjectionAmountKzt;
                MoneyAmountPolicy.EnsureRepresentable(remaining, "Remaining preview amount");
                return new ExpenseDraftBudgetPosition(key.CostCenterId, key.CashFlowItemId, key.BudgetMonth,
                    limit, other, position.PaidAmountKzt, position.DesiredRequestProjectionAmountKzt,
                    remaining, Math.Max(0m, -remaining));
            }).ToArray();
            var route = approvalRoutes is null ? null : await approvalRoutes.BuildDraftAsync(draft,
                result.Sum(position => position.PositiveDeficitKzt), token);
            if (route is not null && command.ExpectedVersion is Guid expected)
                route = route with { RequestVersion = expected };
            return new ExpenseDraftBudgetPreview(result, route);
        }, cancellationToken);
    }
}
