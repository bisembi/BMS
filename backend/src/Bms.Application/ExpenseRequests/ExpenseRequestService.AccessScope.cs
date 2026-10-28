using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// Who may issue a business command, and over which organisations and cost
/// centres. Every command passes through here before it touches state.
public sealed partial class ExpenseRequestService
{
    private async Task<ExpenseRequestDto> RequireStoredResultReadScopeAsync(ExpenseRequestDto response, CancellationToken cancellationToken)
    {
        var live = await repository.FindVisibleAsync(response.Id,
            ActorIdentity.RequireBusinessDataAccess(currentActor), includeAll: false, cancellationToken);
        var scope = await currentActor.ResolveBusinessDataReadScopeAsync(cancellationToken);
        if (live is null || !scope.Contains(live)
            || !scope.Contains(response.DefaultCostCenterId, response.Lines.Select(line => line.CostCenterId)))
            throw new ExpenseRequestNotFoundException();
        // Renumbering changes only the current document identity label, not
        // the captured financial/workflow result or immutable audit JSON.
        return response with { RequestNumber = live.RequestNumber };
    }

    private async Task RequireDocumentReadScopeAsync(Guid? header, IEnumerable<Guid?> lineCostCenters,
        CancellationToken cancellationToken)
    {
        if (!(await currentActor.ResolveBusinessDataReadScopeAsync(cancellationToken)).Contains(header, lineCostCenters))
            throw new ExpenseRequestNotFoundException();
    }


    private async Task<string> RequireBusinessCommandActorAsync(
        CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (!await currentActor.CanCreateExpenseRequestAsync(cancellationToken))
        {
            throw new ActorForbiddenException();
        }

        return actorExternalId;
    }

    private string RequireExistingRequestCommandActor()
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (currentActor.IsBmsAdministrator)
        {
            throw new ActorForbiddenException();
        }

        return actorExternalId;
    }

    private async Task<string> RequireCopyCommandActorAsync(
        CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (currentActor.IsBmsAdministrator ||
            !await currentActor.CanCreateExpenseRequestAsync(cancellationToken))
        {
            throw new ActorForbiddenException();
        }

        return actorExternalId;
    }

    // A functional approver's broad draft-creation capability is not permission
    // to duplicate a document. Check the selected role on commands and replays;
    // another role assigned to the same employee must not leak into this one.
    private void RequireCopyAuthoringRole351()
    {
        if (currentActor.ActiveRoleCode is not
            (Bms.Domain.Workflow.ExpenseRequestWorkflowCodes.CfoInitiatorRoleCode or
             Bms.Domain.Workflow.ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode))
            throw new ActorForbiddenException();
    }

    private static void ValidateExpectedVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty)
        {
            throw new DomainRuleViolationException("ExpectedVersion is required.");
        }
    }

    private async Task ValidateAccessScopeAsync(
        string actorExternalId,
        Guid? organizationId,
        Guid? defaultCostCenterId,
        IEnumerable<Guid?>? lineCostCenterIds,
        DateOnly asOf,
        CancellationToken cancellationToken,
        BudgetOwnerCreationAuthority? principalAuthority = null)
    {
        var requestedCostCenterIds = (lineCostCenterIds ?? [])
            .Append(defaultCostCenterId)
            .Where(id => id.HasValue && id.Value != Guid.Empty)
            .Select(id => id.GetValueOrDefault())
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        var organization = organizationId is { } value && value != Guid.Empty ? value : (Guid?)null;
        var allowed = principalAuthority is null
            ? await accessScope.CanUseForExpenseDraftAsync(
                actorExternalId,
                organization,
                requestedCostCenterIds,
                asOf,
                cancellationToken)
            : await accessScope.CanUseForAuthorizedExpenseDraftAsync(actorExternalId, organization,
                requestedCostCenterIds, asOf, principalAuthority, cancellationToken);
        if (!allowed)
        {
            throw new ActorForbiddenException();
        }
    }

    /// <summary>
    /// A selected header CFO is authoritative: its owner is the only actor
    /// allowed to create, edit or copy the draft unless the initiator has the
    /// separately resolved financial-director draft authority. Every material
    /// line must belong to that same CFO. Incomplete drafts without a header CFO remain
    /// editable; they cannot be submitted by the workflow service.
    /// </summary>
    private async Task EnsureBudgetOwnerControlsHeaderCostCenterAsync(
        string actorExternalId,
        Guid? defaultCostCenterId,
        IEnumerable<Guid?>? lineCostCenterIds,
        CancellationToken cancellationToken,
        BudgetOwnerCreationAuthority? principalAuthority = null)
    {
        if (defaultCostCenterId is not { } costCenterId || costCenterId == Guid.Empty)
        {
            return;
        }

        if ((lineCostCenterIds ?? []).Any(lineCostCenterId =>
                lineCostCenterId is { } value &&
                value != Guid.Empty &&
                value != costCenterId))
        {
            throw new DomainRuleViolationException(
                "Every expense-request line must use the header cost center.", DomainRuleViolationReason.ExpenseDraftLineCfoMismatch);
        }

        var ownerAuthority = principalAuthority ?? budgetOwnerCreationAuthority;
        if (await ownerAuthority.HasFinancialDraftAuthorityAsync(
                actorExternalId, cancellationToken))
        {
            return;
        }

        var authority = await ownerAuthority.ResolveAsync(
            actorExternalId,
            costCenterId,
            cancellationToken);
        if (!authority.CanCreateExpenseRequest)
        {
            throw new ActorForbiddenException();
        }
    }

    private async Task EnsureBudgetOwnerMayEditExistingAsync(
        string principalActorExternalId,
        Guid? defaultCostCenterId,
        IEnumerable<Guid?>? lineCostCenterIds,
        CancellationToken cancellationToken)
    {
        var principalAuthority = await PrincipalBudgetAuthorityAsync(principalActorExternalId, cancellationToken);
        if (defaultCostCenterId is { } costCenterId && costCenterId != Guid.Empty)
        {
            if ((lineCostCenterIds ?? []).Any(lineCostCenterId =>
                    lineCostCenterId is { } value &&
                    value != Guid.Empty &&
                    value != costCenterId))
            {
                throw new DomainRuleViolationException(
                    "Every expense-request line must use the header cost center.", DomainRuleViolationReason.ExpenseDraftLineCfoMismatch);
            }

            await EnsureBudgetOwnerControlsHeaderCostCenterAsync(
                principalActorExternalId,
                costCenterId,
                lineCostCenterIds,
                cancellationToken, principalAuthority);
            return;
        }

        if (await principalAuthority.HasFinancialDraftAuthorityAsync(
                principalActorExternalId, cancellationToken))
        {
            return;
        }

        var authority = await principalAuthority.ResolveAnyAsync(
            principalActorExternalId,
            cancellationToken);
        if (!authority.CanCreateExpenseRequest)
        {
            throw new ActorForbiddenException();
        }
    }

    /// <summary>
    /// A readable card is not necessarily editable. The direct initiator or an
    /// active substitute must also retain the principal's current budget-owner
    /// assignment. This is a boolean UI capability, so stale or structurally
    /// invalid business data closes editing instead of hiding the readable card.
    /// Dependency failures still propagate and are mapped to the fixed 503.
    /// </summary>
    private async Task<bool> CanBudgetOwnerEditExistingAsync(
        string principalActorExternalId,
        Guid? defaultCostCenterId,
        IEnumerable<Guid?>? lineCostCenterIds,
        CancellationToken cancellationToken)
    {
        BudgetOwnerCreationAuthority principalAuthority;
        try { principalAuthority = await PrincipalBudgetAuthorityAsync(principalActorExternalId, cancellationToken); }
        catch (ActorForbiddenException) { return false; }
        if (defaultCostCenterId is { } costCenterId && costCenterId != Guid.Empty)
        {
            if ((lineCostCenterIds ?? []).Any(lineCostCenterId =>
                    lineCostCenterId is { } value &&
                    value != Guid.Empty &&
                    value != costCenterId))
            {
                return false;
            }

            if (await principalAuthority.HasFinancialDraftAuthorityAsync(
                    principalActorExternalId, cancellationToken))
            {
                return true;
            }

            var exact = await principalAuthority.ResolveAsync(
                principalActorExternalId,
                costCenterId,
                cancellationToken);
            return exact.CanCreateExpenseRequest;
        }

        if (await principalAuthority.HasFinancialDraftAuthorityAsync(
                principalActorExternalId, cancellationToken))
        {
            return true;
        }

        var any = await principalAuthority.ResolveAnyAsync(
            principalActorExternalId,
            cancellationToken);
        return any.CanCreateExpenseRequest;
    }

    // Callers have already proved direct initiator or active substitution.
    private Task<BudgetOwnerCreationAuthority> PrincipalBudgetAuthorityAsync(
        string principalActorExternalId, CancellationToken cancellationToken) =>
        string.Equals(principalActorExternalId, currentActor.ExternalId, StringComparison.Ordinal)
            ? Task.FromResult(budgetOwnerCreationAuthority)
            : budgetOwnerCreationAuthority.ForAuthorizedSubstitutionPrincipalAsync(currentActor, cancellationToken);

    private async Task<ExpenseRequestActionAuthority> RequireExistingRequestAuthorityAsync(
        ExpenseRequest request,
        string actorExternalId,
        DateTimeOffset authorizationAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!(await currentActor.ResolveBusinessDataReadScopeAsync(cancellationToken)).Contains(request))
        {
            throw new ExpenseRequestNotFoundException();
        }
        ExpenseRequestActionAuthority resolved;
        if (string.Equals(
                request.InitiatorExternalId,
                actorExternalId,
                StringComparison.Ordinal))
        {
            resolved = ExpenseRequestActionAuthority.Direct(actorExternalId);
        }
        else if (substitutionAuthority is null)
        {
            resolved = ExpenseRequestActionAuthority.Denied(
                request.InitiatorExternalId,
                actorExternalId);
        }
        else
        {
            resolved = await substitutionAuthority.ResolveAsync(
                request.InitiatorExternalId,
                actorExternalId,
                authorizationAtUtc,
                cancellationToken);
        }

        if (!resolved.IsAuthorized)
        {
            throw new ExpenseRequestNotFoundException();
        }

        return resolved;
    }
}
