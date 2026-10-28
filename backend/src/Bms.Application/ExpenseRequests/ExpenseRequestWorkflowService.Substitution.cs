using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

public sealed partial class ExpenseRequestWorkflowService
{
    private async Task RequireDocumentReadScopeAsync(ExpenseRequest request, CancellationToken cancellationToken)
    {
        if (!(await currentActor.ResolveBusinessDataReadScopeAsync(cancellationToken)).Contains(request))
            throw new ExpenseRequestNotFoundException();
    }

    private async Task RequireWorkflowReplayReadScopeAsync(ExpenseRequest request, string commandCode, CancellationToken cancellationToken)
    {
        var scope = await currentActor.ResolveBusinessDataReadScopeAsync(cancellationToken);
        // A lost NSI response remains recoverable only from this actor's command
        // receipt after reconfirming the current NSI role and organization grant.
        if (commandCode == Bms.Domain.Workflow.ExpenseRequestWorkflowCodes.ConfirmNsiCommandCode
            && scope.CanReadNsiOrganization(request.OrganizationId)) return;
        if (!scope.Contains(request)) throw new ExpenseRequestNotFoundException();
    }

    private async Task<ExpenseRequestDto> ReadStoredResponseAsync(string json, ExpenseRequest request, CancellationToken cancellationToken,
        bool nsiCommandReceipt = false)
    {
        using var storedDocument = System.Text.Json.JsonDocument.Parse(json);
        var response = storedDocument.RootElement.TryGetProperty("response", out var nested)
            ? DeserializeStoredResponse(nested.GetRawText()) : DeserializeStoredResponse(json);
        var scope = await currentActor.ResolveBusinessDataReadScopeAsync(cancellationToken);
        if (response.Id != request.Id || !(scope.Contains(response.DefaultCostCenterId, response.Lines.Select(line => line.CostCenterId))
            || (request.PaymentOperation?.IsPayrollAdvance == true && response.PaymentOperation?.IsPayrollAdvance == true
                && scope.PayrollRequestIds.Contains(request.Id))
            || (nsiCommandReceipt && response.OrganizationId == request.OrganizationId && scope.CanReadNsiOrganization(response.OrganizationId))))
            throw new ExpenseRequestNotFoundException();
        return response with { RequestNumber = request.RequestNumber };
    }

    private async Task<ExpenseRequestActionAuthority> RequireSubmitAuthorityAsync(
        ExpenseRequest request,
        string actorExternalId,
        DateTimeOffset authorizationAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await RequireDocumentReadScopeAsync(request, cancellationToken);
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

    private async Task<Guid> RequireSubmitCreationAuthorityAsync(
        ExpenseRequest request,
        ExpenseRequestActionAuthority actionAuthority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actionAuthority);
        var costCenterId = RequireCostCenterId(request);
        if (!await currentActor.CanCreateExpenseRequestAsync(cancellationToken))
            throw new ActorForbiddenException();
        var principalAuthority = string.Equals(actionAuthority.InitiatorExternalId, currentActor.ExternalId, StringComparison.Ordinal)
            ? budgetOwnerCreationAuthority
            : await budgetOwnerCreationAuthority.ForAuthorizedSubstitutionPrincipalAsync(currentActor, cancellationToken);
        var canCreate = await principalAuthority.CanCreateForCostCenterAsync(
            actionAuthority.InitiatorExternalId,
            costCenterId,
            cancellationToken);
        if (!canCreate)
        {
            throw new ActorForbiddenException();
        }

        // Legacy isolated owner adapters can use V1; a finance creator must
        // never silently fall back to V1 when the CFO route resolver is absent.
        if (initiatorRoutes is null && await principalAuthority.HasFinancialDraftAuthorityAsync(
                actionAuthority.InitiatorExternalId, cancellationToken))
            throw new ExpenseRequestWorkflowUnavailableException();

        return costCenterId;
    }
}
