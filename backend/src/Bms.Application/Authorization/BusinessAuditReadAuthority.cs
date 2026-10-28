using Bms.Application.Abstractions;
using Bms.Domain.Workflow;

namespace Bms.Application.Authorization;

/// <summary>
/// Resolves the approved AUDITOR capability through the authoritative
/// actor-to-employee and employee-to-functional-role projections. It grants
/// read access only; no business-command service consumes this authority.
/// </summary>
public sealed class BusinessAuditReadAuthority(
    EmployeeActorResolver employeeActorResolver,
    EmployeeFunctionalRoleResolver employeeFunctionalRoleResolver) :
    IBusinessAuditReadAuthority
{
    public async Task<bool> CanReadAsync(
        string actorExternalId,
        CancellationToken cancellationToken)
    {
        try
        {
            var employee = await employeeActorResolver.ResolveAsync(
                actorExternalId,
                cancellationToken);
            if (!employee.HasActiveEmployee || employee.EmployeeId is not Guid employeeId)
            {
                return false;
            }

            var roles = await employeeFunctionalRoleResolver.ResolveAsync(
                employeeId,
                cancellationToken);
            if (!roles.EmployeeExists
                || !roles.EmployeeIsActive
                || roles.EmployeeId != employeeId)
            {
                throw new BusinessAuditReadAuthorityUnavailableException();
            }

            // FIN_DIRECTOR is the owner-approved global business role. It may
            // inspect the immutable business audit across organizations and
            // cost centres, but this read-only capability grants no technical
            // administration or business-command permission by itself.
            return roles.HasExactRole(ExpenseRequestWorkflowCodes.AuditorRoleCode)
                || roles.HasExactRole(
                    ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BusinessAuditReadAuthorityUnavailableException)
        {
            throw;
        }
        catch (EmployeeActorResolutionUnavailableException exception)
        {
            throw new BusinessAuditReadAuthorityUnavailableException(exception);
        }
        catch (EmployeeFunctionalRoleResolutionUnavailableException exception)
        {
            throw new BusinessAuditReadAuthorityUnavailableException(exception);
        }
        catch (Exception exception)
        {
            throw new BusinessAuditReadAuthorityUnavailableException(exception);
        }
    }
}
