using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Application.Security;
using Bms.Domain.Workflow;

namespace Bms.Application.Authorization;

public sealed record ReferenceDataMaintenanceActor(Guid EmployeeId, string RoleCode);

/// <summary>Fresh authority for shared-directory writes, evaluated inside each transaction attempt.</summary>
public sealed class ReferenceDataMaintenanceAuthority(
    EmployeeActorResolver employees, EmployeeFunctionalRoleResolver roles)
{
    public async Task<ReferenceDataMaintenanceActor?> ResolveAsync(ICurrentActor actor, CancellationToken token)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        var selectedRole = actor.ActiveRoleCode;
        token.ThrowIfCancellationRequested();
        try
        {
            if (actor.UsesActiveRoleSelection && !roles.UsesActiveRoleSelection)
                throw new BudgetDocumentAuthorityUnavailableException();
            var employee = await employees.ResolveAsync(externalId, token);
            if (employee.EmployeeId is not { } employeeId) return null;
            // Explicit legacy adapters are not the authenticated production path.
            if (!actor.UsesActiveRoleSelection && actor.IsBmsAdministrator)
                return new(employeeId, BmsAccessProfileCodes.SystemAdministrator);

            var current = await roles.ResolveAsync(employeeId, token);
            token.ThrowIfCancellationRequested();
            if (current.EmployeeId != employeeId || !current.EmployeeExists || !current.EmployeeIsActive)
                throw new BudgetDocumentAuthorityUnavailableException();
            if (!string.Equals(actor.ExternalId, externalId, StringComparison.Ordinal)
                || !string.Equals(actor.ActiveRoleCode, selectedRole, StringComparison.Ordinal))
                throw new ActorForbiddenException();

            var maintainers = new[] { ExpenseRequestWorkflowCodes.FinancialControllerRoleCode,
                ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode,
                ExpenseRequestWorkflowCodes.GeneralDirectorRoleCode };
            if (actor.UsesActiveRoleSelection)
            {
                if (selectedRole is null || (selectedRole != BmsAccessProfileCodes.SystemAdministrator
                    && !maintainers.Contains(selectedRole, StringComparer.Ordinal))) return null;
                // Technical aliases are raw SQL backing codes, never invented business roles.
                return ActiveRoleCatalog.FindActiveBackingAssignment(selectedRole, current.ActiveRoleCodes) is null
                    ? null : new(employeeId, selectedRole);
            }
            var legacyRole = maintainers.FirstOrDefault(current.HasExactRole);
            return legacyRole is null ? null : new(employeeId, legacyRole);
        }
        catch (OperationCanceledException) { throw; }
        catch (ActorNotAuthenticatedException) { throw; }
        catch (ActorForbiddenException) { throw; }
        catch (BudgetDocumentAuthorityUnavailableException) { throw; }
        catch (Exception exception) { throw new BudgetDocumentAuthorityUnavailableException(exception); }
    }
}
