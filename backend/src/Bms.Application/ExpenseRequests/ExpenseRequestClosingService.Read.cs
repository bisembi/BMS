using Bms.Application.Common;
using Bms.Application.Authorization;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;

namespace Bms.Application.ExpenseRequests;

internal sealed partial class ExpenseRequestClosingService
{
    public async Task<ExpenseRequestClosingStateDto?> GetStateAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (id == Guid.Empty)
        {
            return null;
        }

        try
        {
            return await transactionRunner.ExecuteAsync<ExpenseRequestClosingStateDto?>(
                async transactionCancellationToken =>
                {
                    var request = await repository.FindVisibleAsync(
                        id,
                        actorExternalId,
                        currentActor.HasFullBusinessDataAccess,
                        transactionCancellationToken);
                    if (request is null)
                    {
                        return null;
                    }

                    var canExecute = await CanExecuteClosingCommandsAsync(
                        request,
                        actorExternalId,
                        transactionCancellationToken);
                    return ExpenseRequestClosingProjection.FromDomain(request, canExecute);
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            ActorNotAuthenticatedException or
            ActorForbiddenException or
            ExpenseRequestClosingUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ExpenseRequestClosingUnavailableException(exception);
        }
    }

    private async Task<bool> CanExecuteClosingCommandsAsync(
        ExpenseRequest request,
        string actorExternalId,
        CancellationToken cancellationToken)
    {
        if (currentActor.IsBmsAdministrator ||
            request.Status is not (ExpenseRequestStatus.Paid or ExpenseRequestStatus.Closed))
        {
            return false;
        }

        var actor = await employeeActorResolver.ResolveAsync(
            actorExternalId,
            cancellationToken);
        if (!actor.HasActiveEmployee || actor.EmployeeId is not { } employeeId)
        {
            return false;
        }

        var roles = await employeeFunctionalRoleResolver.ResolveAsync(
            employeeId,
            cancellationToken);
        var commandCode = request.Status == ExpenseRequestStatus.Closed
            ? ExpenseRequestWorkflowCodes.ReopenCommandCode
            : ExpenseRequestWorkflowCodes.ClosePartialCommandCode;
        if (ClosingRoleForCommand(roles, commandCode) is null)
        {
            return false;
        }
        if (request.OrganizationId is not { } organizationId || organizationId == Guid.Empty)
        {
            throw new ExpenseRequestClosingUnavailableException();
        }

        var accessDate = DateOnly.FromDateTime(RequireUtcNow().UtcDateTime);
        return await accessScope.CanUseAsync(
            actorExternalId,
            organizationId,
            [],
            accessDate,
            cancellationToken);
    }

    private static string? ClosingRoleForCommand(
        EmployeeFunctionalRoleResolution roles, string commandCode)
    {
        if (commandCode is not (ExpenseRequestWorkflowCodes.ClosePartialCommandCode or
            ExpenseRequestWorkflowCodes.CloseFullCommandCode or
            ExpenseRequestWorkflowCodes.ReopenCommandCode))
        {
            return null;
        }
        if (roles.HasExactRole(ExpenseRequestWorkflowCodes.ResponsibleAccountantRoleCode))
        {
            return ExpenseRequestWorkflowCodes.ResponsibleAccountantRoleCode;
        }
        // Chiefs may close and reopen, subject to the same live organization,
        // employee and transaction checks as responsible accountants.
        return roles.HasExactRole(BusinessDataReadPolicy.ChiefAccountantRoleCode)
                ? BusinessDataReadPolicy.ChiefAccountantRoleCode
                : null;
    }

    private static bool CanExecuteClosingForRole(string roleCode, ExpenseRequestStatus status) =>
        status == ExpenseRequestStatus.Paid ||
        (status == ExpenseRequestStatus.Closed &&
            (roleCode == ExpenseRequestWorkflowCodes.ResponsibleAccountantRoleCode ||
             roleCode == BusinessDataReadPolicy.ChiefAccountantRoleCode));
}
